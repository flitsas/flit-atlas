import { esObjeto, nombreDeRef, type Objeto, resolver, texto } from './documento';

/**
 * Lo que la vista propia de operaciones (HU 3.6) necesita de un OpenAPI 3.x: cada operación y webhook con sus cabeceras,
 * parámetros, cuerpo, respuesta de ejemplo y errores, ya con los `$ref` resueltos. Es la vista del móvil y la de
 * escritorio cuando no se usa Scalar.
 */
export interface ValorPermitido {
  codigo: string;
  descripcion?: string;
}

export interface Campo {
  nombre: string;
  ubicacion: 'path' | 'query' | 'header' | 'cookie' | 'cuerpo';
  tipo: string;
  obligatorio: boolean;
  descripcion?: string;
  valores?: ValorPermitido[];
  /** `máx. 12 caracteres`, `0 a 99`, `formato date`… */
  restricciones: string[];
  /** 0 para los de primer nivel; los anidados (`vendedor.documento`) van debajo de su padre. */
  profundidad: number;
}

export interface Respuesta {
  codigo: string;
  nombre: string;
  descripcion?: string;
  tipoContenido?: string;
  ejemplo?: unknown;
}

export interface OperacionOpenApi {
  /** `GET /tramites/{id}` o `POST webhook:estadoTramite`, como el índice del servidor. */
  clave: string;
  metodo: string;
  ruta: string;
  esWebhook: boolean;
  operationId?: string;
  resumen?: string;
  descripcion?: string;
  etiquetas: string[];
  publica: boolean;
  interna: boolean;
  deprecada: boolean;
  cabeceras: Campo[];
  parametros: Campo[];
  cuerpo?: { tipoContenido: string; campos: Campo[]; ejemplo?: unknown };
  respuestas: Respuesta[];
  errores: Respuesta[];
  /** Pide `Authorization: Bearer $TOKEN` (o un esquema http/oauth2/openIdConnect). */
  requiereToken: boolean;
  /** Cabeceras de API key que exige su seguridad. */
  llaves: string[];
}

const METODOS = ['get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace'] as const;

const NOMBRES_HTTP: Record<string, string> = {
  '200': 'OK',
  '201': 'Creado',
  '202': 'Aceptado',
  '204': 'Sin contenido',
  '400': 'Solicitud inválida',
  '401': 'No autenticado',
  '403': 'Prohibido',
  '404': 'No encontrado',
  '405': 'Método no permitido',
  '409': 'Conflicto',
  '410': 'Ya no existe',
  '413': 'Cuerpo demasiado grande',
  '415': 'Tipo de contenido no soportado',
  '422': 'No procesable',
  '429': 'Demasiadas solicitudes',
  '500': 'Error interno',
  '502': 'Puerta de enlace inválida',
  '503': 'Servicio no disponible',
  '504': 'Tiempo de espera agotado',
  default: 'Cualquier otro código',
};

export const nombreHttp = (codigo: string): string =>
  NOMBRES_HTTP[codigo] ?? (/^[1-5]XX$/i.test(codigo) ? `Familia ${codigo.toUpperCase()}` : `HTTP ${codigo}`);

const MAX_PROFUNDIDAD = 3;
const MAX_CAMPOS = 400;

/** Une `allOf` (propiedades y obligatorios) y toma la primera variante de `oneOf`/`anyOf` si no hay propiedades propias. */
function aplanar(raiz: Objeto, esquema: unknown, saltos = 0): Objeto {
  const nodo = resolver(raiz, esquema);
  if (!esObjeto(nodo) || saltos > 10) return {};
  let resultado: Objeto = { ...nodo };
  if (Array.isArray(nodo.allOf)) {
    const propiedades: Objeto = {};
    const obligatorios = new Set<string>(Array.isArray(nodo.required) ? (nodo.required as string[]) : []);
    for (const parte of nodo.allOf) {
      const plano = aplanar(raiz, parte, saltos + 1);
      // Una variante que restringe una propiedad (`transaction_type: { const: 3 }`) se suma a la del esquema base.
      for (const [nombre, propiedad] of Object.entries(esObjeto(plano.properties) ? plano.properties : {})) {
        const previa = resolver(raiz, propiedades[nombre]);
        const nueva = resolver(raiz, propiedad);
        propiedades[nombre] = esObjeto(previa) && esObjeto(nueva) ? { ...previa, ...nueva } : propiedad;
      }
      for (const r of Array.isArray(plano.required) ? (plano.required as string[]) : []) obligatorios.add(r);
      resultado = { ...plano, ...resultado };
    }
    resultado.properties = { ...propiedades, ...(esObjeto(nodo.properties) ? nodo.properties : {}) };
    resultado.required = [...obligatorios];
  }
  const variantes = Array.isArray(nodo.oneOf) ? nodo.oneOf : Array.isArray(nodo.anyOf) ? nodo.anyOf : null;
  if (variantes && variantes.length > 0 && !esObjeto(resultado.properties)) {
    const primera = aplanar(raiz, variantes[0], saltos + 1);
    resultado = { ...primera, ...resultado, properties: primera.properties, required: primera.required };
  }
  return resultado;
}

function tipoDe(raiz: Objeto, crudo: unknown): string {
  const ref = esObjeto(crudo) && typeof crudo.$ref === 'string' ? nombreDeRef(crudo.$ref) : undefined;
  const nodo = aplanar(raiz, crudo);
  const tipos = Array.isArray(nodo.type) ? (nodo.type as string[]).filter((t) => t !== 'null') : typeof nodo.type === 'string' ? [nodo.type] : [];
  let base = tipos.join(' | ');
  if (base === 'array') base = `array de ${tipoDe(raiz, nodo.items)}`;
  if (!base) base = ref ?? (esObjeto(nodo.properties) ? 'object' : 'cualquiera');
  else if (ref && base !== 'object' && !base.startsWith('array')) base = `${base} (${ref})`;
  else if (ref && base === 'object') base = ref;
  if (typeof nodo.format === 'string') base += ` · ${nodo.format}`;
  return base;
}

/** Valores de un `enum`, con su descripción de `x-enum-descriptions` (lista paralela u objeto por valor). */
export function valoresPermitidos(nodo: Objeto): ValorPermitido[] | undefined {
  if (!Array.isArray(nodo.enum)) return undefined;
  const descripciones = nodo['x-enum-descriptions'];
  return nodo.enum.map((valor, i) => {
    const codigo = String(valor);
    const descripcion = Array.isArray(descripciones)
      ? texto(descripciones[i])
      : esObjeto(descripciones)
        ? texto(descripciones[codigo])
        : undefined;
    return { codigo, descripcion };
  });
}

function restriccionesDe(nodo: Objeto): string[] {
  const r: string[] = [];
  if (typeof nodo.minLength === 'number' && typeof nodo.maxLength === 'number') r.push(`${nodo.minLength} a ${nodo.maxLength} caracteres`);
  else if (typeof nodo.maxLength === 'number') r.push(`máx. ${nodo.maxLength} caracteres`);
  else if (typeof nodo.minLength === 'number') r.push(`mín. ${nodo.minLength} caracteres`);
  if (typeof nodo.minimum === 'number' && typeof nodo.maximum === 'number') r.push(`${nodo.minimum} a ${nodo.maximum}`);
  else if (typeof nodo.maximum === 'number') r.push(`hasta ${nodo.maximum}`);
  else if (typeof nodo.minimum === 'number') r.push(`desde ${nodo.minimum}`);
  if (typeof nodo.pattern === 'string') r.push(`patrón ${nodo.pattern}`);
  if (typeof nodo['x-flit-decimales'] === 'number') r.push(`${nodo['x-flit-decimales']} decimales`);
  if (nodo.deprecated === true) r.push('deprecado');
  return r;
}

function campo(raiz: Objeto, nombre: string, crudo: unknown, ubicacion: Campo['ubicacion'], obligatorio: boolean, profundidad: number, descripcionExtra?: string): Campo {
  const nodo = aplanar(raiz, crudo);
  const items = nodo.type === 'array' ? aplanar(raiz, nodo.items) : null;
  return {
    nombre,
    ubicacion,
    tipo: tipoDe(raiz, crudo),
    obligatorio,
    descripcion: texto(descripcionExtra) ?? texto(nodo.description) ?? texto(nodo.title),
    valores: valoresPermitidos(nodo) ?? (items ? valoresPermitidos(items) : undefined),
    restricciones: restriccionesDe(nodo),
    profundidad,
  };
}

/** Los campos de un esquema (objeto), en orden, con los anidados debajo de su padre (`vendedor.documento`). */
export function camposDeEsquema(raiz: Objeto, esquema: unknown, ubicacion: Campo['ubicacion'] = 'cuerpo'): Campo[] {
  const salida: Campo[] = [];
  const visitados = new Set<unknown>();
  function recorrer(crudo: unknown, prefijo: string, profundidad: number) {
    const nodo = aplanar(raiz, crudo);
    const objeto = nodo.type === 'array' ? aplanar(raiz, nodo.items) : nodo;
    if (!esObjeto(objeto.properties) || visitados.has(objeto.properties) || profundidad > MAX_PROFUNDIDAD) return;
    visitados.add(objeto.properties);
    const obligatorios = new Set(Array.isArray(objeto.required) ? (objeto.required as string[]) : []);
    for (const [nombre, propiedad] of Object.entries(objeto.properties)) {
      if (salida.length >= MAX_CAMPOS) return;
      const ruta = prefijo ? `${prefijo}${nodo.type === 'array' ? '[]' : ''}.${nombre}` : nombre;
      salida.push(campo(raiz, ruta, propiedad, ubicacion, obligatorios.has(nombre), profundidad));
      recorrer(propiedad, ruta, profundidad + 1);
    }
    visitados.delete(objeto.properties);
  }
  recorrer(esquema, '', 0);
  return salida;
}

/** Un ejemplo para mostrar: el del contenido, el primero de `examples` o uno armado desde el esquema. */
function ejemploDe(raiz: Objeto, contenido: Objeto): unknown {
  if (contenido.example !== undefined) return contenido.example;
  if (esObjeto(contenido.examples)) {
    const primero = resolver(raiz, Object.values(contenido.examples)[0]);
    if (esObjeto(primero) && primero.value !== undefined) return primero.value;
  }
  return ejemploDeEsquema(raiz, contenido.schema, 0);
}

export function ejemploDeEsquema(raiz: Objeto, crudo: unknown, profundidad: number): unknown {
  const nodo = aplanar(raiz, crudo);
  if (nodo.example !== undefined) return nodo.example;
  if (Array.isArray(nodo.examples) && nodo.examples.length > 0) return nodo.examples[0];
  if (nodo.const !== undefined) return nodo.const;
  if (Array.isArray(nodo.enum) && nodo.enum.length > 0) return nodo.enum[0];
  if (profundidad > 4) return undefined;
  const tipo = Array.isArray(nodo.type) ? (nodo.type as string[]).find((t) => t !== 'null') : nodo.type;
  if (tipo === 'array') {
    const item = ejemploDeEsquema(raiz, nodo.items, profundidad + 1);
    return item === undefined ? [] : [item];
  }
  if (tipo === 'object' || esObjeto(nodo.properties)) {
    const salida: Objeto = {};
    for (const [nombre, propiedad] of Object.entries(esObjeto(nodo.properties) ? nodo.properties : {})) {
      const valor = ejemploDeEsquema(raiz, propiedad, profundidad + 1);
      if (valor !== undefined) salida[nombre] = valor;
    }
    return salida;
  }
  if (tipo === 'integer' || tipo === 'number') return typeof nodo.minimum === 'number' ? nodo.minimum : 0;
  if (tipo === 'boolean') return true;
  if (tipo === 'string') return nodo.format === 'date' ? '2026-01-31' : nodo.format === 'date-time' ? '2026-01-31T10:00:00-05:00' : 'texto';
  return undefined;
}

/** El primer contenido JSON (o el primero que haya) de un cuerpo o una respuesta. */
function contenidoPreferido(raiz: Objeto, contenido: unknown): { tipo: string; medio: Objeto } | undefined {
  if (!esObjeto(contenido)) return undefined;
  const entradas = Object.entries(contenido);
  const elegido = entradas.find(([t]) => /json/i.test(t)) ?? entradas[0];
  if (!elegido) return undefined;
  const medio = resolver(raiz, elegido[1]);
  return esObjeto(medio) ? { tipo: elegido[0], medio } : undefined;
}

function respuestas(raiz: Objeto, crudas: unknown): Respuesta[] {
  if (!esObjeto(crudas)) return [];
  return Object.entries(crudas)
    .map(([codigo, cruda]) => {
      const r = resolver(raiz, cruda);
      if (!esObjeto(r)) return { codigo, nombre: nombreHttp(codigo) };
      const contenido = contenidoPreferido(raiz, r.content);
      return {
        codigo,
        nombre: nombreHttp(codigo),
        descripcion: texto(r.description),
        tipoContenido: contenido?.tipo,
        ejemplo: contenido ? ejemploDe(raiz, contenido.medio) : undefined,
      };
    })
    .sort((a, b) => a.codigo.localeCompare(b.codigo));
}

function seguridad(raiz: Objeto, requisitos: unknown): { token: boolean; llaves: string[] } {
  const lista = Array.isArray(requisitos) ? requisitos : [];
  const esquemas = esObjeto(raiz.components) && esObjeto(raiz.components.securitySchemes) ? raiz.components.securitySchemes : {};
  let token = false;
  const llaves: string[] = [];
  // Basta con la primera alternativa: es la que muestran los ejemplos.
  const primera = lista.find(esObjeto);
  for (const nombre of Object.keys(primera ?? {})) {
    const esquema = resolver(raiz, esquemas[nombre]);
    if (!esObjeto(esquema)) continue;
    if (esquema.type === 'apiKey' && esquema.in === 'header' && typeof esquema.name === 'string') llaves.push(esquema.name);
    else if (esquema.type === 'http' || esquema.type === 'oauth2' || esquema.type === 'openIdConnect') token = true;
  }
  return { token, llaves };
}

function parametros(raiz: Objeto, ...listas: unknown[]): Campo[] {
  const porClave = new Map<string, Campo>();
  for (const lista of listas) {
    for (const crudo of Array.isArray(lista) ? lista : []) {
      const p = resolver(raiz, crudo);
      if (!esObjeto(p) || typeof p.name !== 'string' || typeof p.in !== 'string') continue;
      const ubicacion = (['path', 'query', 'header', 'cookie'] as const).find((u) => u === p.in);
      if (!ubicacion) continue;
      const nodo = esObjeto(p.schema) ? p.schema : {};
      porClave.set(`${p.in}:${p.name}`, campo(raiz, p.name, nodo, ubicacion, p.required === true || p.in === 'path', 0, texto(p.description)));
    }
  }
  return [...porClave.values()];
}

/** Todas las operaciones y webhooks del spec, en el orden del documento. */
export function operacionesOpenApi(raiz: Objeto): OperacionOpenApi[] {
  const salida: OperacionOpenApi[] = [];
  const seguridadGlobal = raiz.security;
  const grupos: Array<[Objeto, boolean]> = [];
  if (esObjeto(raiz.paths)) grupos.push([raiz.paths, false]);
  if (esObjeto(raiz.webhooks)) grupos.push([raiz.webhooks, true]);
  for (const [grupo, esWebhook] of grupos) {
    for (const [ruta, crudo] of Object.entries(grupo)) {
      const item = resolver(raiz, crudo);
      if (!esObjeto(item)) continue;
      for (const metodo of METODOS) {
        const op = item[metodo];
        if (!esObjeto(op)) continue;
        const todos = parametros(raiz, item.parameters, op.parameters);
        const cuerpo = esObjeto(resolver(raiz, op.requestBody)) ? (resolver(raiz, op.requestBody) as Objeto) : undefined;
        const contenido = cuerpo ? contenidoPreferido(raiz, cuerpo.content) : undefined;
        const { token, llaves } = seguridad(raiz, op.security ?? seguridadGlobal);
        const todas = respuestas(raiz, op.responses);
        const etiquetas = Array.isArray(op.tags) ? op.tags.filter((t): t is string => typeof t === 'string') : [];
        salida.push({
          clave: esWebhook ? `${metodo.toUpperCase()} webhook:${ruta}` : `${metodo.toUpperCase()} ${ruta}`,
          metodo: metodo.toUpperCase(),
          ruta,
          esWebhook,
          operationId: texto(op.operationId),
          resumen: texto(op.summary),
          descripcion: texto(op.description),
          etiquetas,
          publica: op['x-public'] === true || etiquetas.includes('public'),
          interna: op['x-internal'] === true,
          deprecada: op.deprecated === true,
          cabeceras: todos.filter((p) => p.ubicacion === 'header'),
          parametros: todos.filter((p) => p.ubicacion !== 'header'),
          cuerpo: contenido
            ? { tipoContenido: contenido.tipo, campos: camposDeEsquema(raiz, contenido.medio.schema), ejemplo: ejemploDe(raiz, contenido.medio) }
            : undefined,
          respuestas: todas.filter((r) => !/^[45]/.test(r.codigo) && r.codigo !== 'default'),
          errores: todas.filter((r) => /^[45]/.test(r.codigo) || r.codigo === 'default'),
          requiereToken: token,
          llaves,
        });
      }
    }
  }
  return salida;
}

/** Título, versión y descripción del `info`. */
export function infoDe(raiz: Objeto): { titulo?: string; version?: string; descripcion?: string } {
  const info = esObjeto(raiz.info) ? raiz.info : {};
  return { titulo: texto(info.title), version: texto(info.version), descripcion: texto(info.description) };
}
