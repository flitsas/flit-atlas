import { esObjeto, nombreDeRef, type Objeto, resolver, texto } from './documento';
import { type Campo, camposDeEsquema, ejemploDeEsquema } from './openapi';

/**
 * Vista legible de un AsyncAPI 2.x o 3.x (HU 3.6): canales, operaciones, mensajes y esquemas. Es propia y simple:
 * `@asyncapi/react-component` arrastra el parser oficial (Ajv compila esquemas con `new Function`, que la CSP sin
 * `unsafe-eval` no permite) y pesa varios MB (ATLAS-ADR-0039).
 */
export interface MensajeAsync {
  id: string;
  nombre: string;
  resumen?: string;
  descripcion?: string;
  tipoContenido?: string;
  campos: Campo[];
  ejemplo?: unknown;
}

export interface CanalAsync {
  id: string;
  direccion: string;
  descripcion?: string;
  mensajes: string[];
}

export interface OperacionAsync {
  id: string;
  /** send/receive (3.x) o publish/subscribe (2.x). */
  accion: string;
  canal: string;
  resumen?: string;
  descripcion?: string;
  mensajes: string[];
  etiquetas: string[];
}

export interface DocumentoAsync {
  version: string;
  canales: CanalAsync[];
  operaciones: OperacionAsync[];
  mensajes: MensajeAsync[];
  esquemas: Array<{ nombre: string; campos: Campo[] }>;
}

function etiquetas(op: Objeto): string[] {
  return Array.isArray(op.tags) ? op.tags.flatMap((t) => (esObjeto(t) && typeof t.name === 'string' ? [t.name] : typeof t === 'string' ? [t] : [])) : [];
}

export function documentoAsync(raiz: Objeto): DocumentoAsync {
  const version = String(raiz.asyncapi ?? '');
  const mensajes = new Map<string, MensajeAsync>();

  /** Registra un mensaje (referencia o en línea) y devuelve su id. */
  function mensaje(crudo: unknown, sugerido: string): string[] {
    const nodo = esObjeto(crudo) && Array.isArray(crudo.oneOf) ? crudo.oneOf : [crudo];
    return nodo.flatMap((m, i) => {
      const id = esObjeto(m) && typeof m.$ref === 'string' ? nombreDeRef(m.$ref) : nodo.length > 1 ? `${sugerido}-${i + 1}` : sugerido;
      const resuelto = resolver(raiz, m);
      if (!esObjeto(resuelto)) return [];
      if (!mensajes.has(id)) {
        const ejemplo = Array.isArray(resuelto.examples) && esObjeto(resuelto.examples[0]) ? resuelto.examples[0].payload : undefined;
        mensajes.set(id, {
          id,
          nombre: texto(resuelto.name) ?? texto(resuelto.title) ?? id,
          resumen: texto(resuelto.summary),
          descripcion: texto(resuelto.description),
          tipoContenido: texto(resuelto.contentType) ?? texto(raiz.defaultContentType),
          campos: camposDeEsquema(raiz, resuelto.payload),
          ejemplo: ejemplo ?? ejemploDeEsquema(raiz, resuelto.payload, 0),
        });
      }
      return [id];
    });
  }

  const canales: CanalAsync[] = [];
  const operaciones: OperacionAsync[] = [];
  const crudosCanales = esObjeto(raiz.channels) ? raiz.channels : {};

  if (version.startsWith('2')) {
    for (const [nombre, crudo] of Object.entries(crudosCanales)) {
      const canal = resolver(raiz, crudo);
      if (!esObjeto(canal)) continue;
      const idsCanal: string[] = [];
      for (const accion of ['publish', 'subscribe'] as const) {
        const op = canal[accion];
        if (!esObjeto(op)) continue;
        const ids = mensaje(op.message, `${texto(op.operationId) ?? nombre}-mensaje`);
        idsCanal.push(...ids);
        operaciones.push({
          id: texto(op.operationId) ?? `${accion} ${nombre}`,
          accion,
          canal: nombre,
          resumen: texto(op.summary),
          descripcion: texto(op.description),
          mensajes: ids,
          etiquetas: etiquetas(op),
        });
      }
      canales.push({ id: nombre, direccion: nombre, descripcion: texto(canal.description), mensajes: [...new Set(idsCanal)] });
    }
  } else {
    const direcciones = new Map<string, string>();
    for (const [id, crudo] of Object.entries(crudosCanales)) {
      const canal = resolver(raiz, crudo);
      if (!esObjeto(canal)) continue;
      const direccion = texto(canal.address) ?? id;
      direcciones.set(`#/channels/${id}`, direccion);
      const ids = Object.entries(esObjeto(canal.messages) ? canal.messages : {}).flatMap(([clave, m]) => mensaje(m, clave));
      canales.push({ id, direccion, descripcion: texto(canal.description), mensajes: ids });
    }
    for (const [id, crudo] of Object.entries(esObjeto(raiz.operations) ? raiz.operations : {})) {
      const op = resolver(raiz, crudo);
      if (!esObjeto(op)) continue;
      const ref = esObjeto(op.channel) && typeof op.channel.$ref === 'string' ? op.channel.$ref : '';
      const canal = direcciones.get(ref) ?? nombreDeRef(ref);
      const ids = Array.isArray(op.messages)
        ? op.messages.flatMap((m) => (esObjeto(m) && typeof m.$ref === 'string' ? [nombreDeRef(m.$ref)] : []))
        : (canales.find((c) => c.direccion === canal)?.mensajes ?? []);
      operaciones.push({ id, accion: texto(op.action) ?? '?', canal, resumen: texto(op.summary), descripcion: texto(op.description), mensajes: ids, etiquetas: etiquetas(op) });
    }
  }

  // Los mensajes de components que ningún canal usa también se muestran.
  const componentes = esObjeto(raiz.components) ? raiz.components : {};
  for (const id of Object.keys(esObjeto(componentes.messages) ? componentes.messages : {})) mensaje({ $ref: `#/components/messages/${id}` }, id);
  const esquemas = Object.entries(esObjeto(componentes.schemas) ? componentes.schemas : {}).map(([nombre, esquema]) => ({
    nombre,
    campos: camposDeEsquema(raiz, esquema),
  }));

  return { version, canales, operaciones, mensajes: [...mensajes.values()], esquemas };
}
