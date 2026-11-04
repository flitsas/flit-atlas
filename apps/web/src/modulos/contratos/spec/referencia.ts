import { esObjeto, type Objeto, texto } from './documento';
import { valoresPermitidos } from './openapi';

/**
 * Matriz de obligatoriedad y tablas de referencia (HU 3.9, ATLAS-ADR-0037): lo que el contrato de ICT trae del Excel como
 * extensiones del OpenAPI. `x-flit-obligatoriedad` va en un esquema de `components.schemas` con los tipos de trámite
 * (`tipos`) y una fila por campo (`campos`) o por documento adjunto (`documentos`); cada celda es `obligatorio`,
 * `opcional` o `no_aplica` (si falta, no aplica). Las tablas son los esquemas con `enum` y `x-enum-descriptions`.
 */
export type Obligatoriedad = 'obligatorio' | 'opcional' | 'no_aplica';

export interface TipoTramite {
  clave: string;
  nombre: string;
  /** Valor de la propiedad (`transaction_type`) para ese tipo, si se conoce. */
  valor?: string;
}

export interface FilaMatriz {
  campo: string;
  /** Descripción o nombre legible (en los documentos adjuntos, el nombre del documento). */
  detalle?: string;
  valores: Record<string, Obligatoriedad>;
  condicion?: string;
}

export interface MatrizObligatoriedad {
  /** Esquema que la trae (`SolicitudRegistroTramite`). */
  esquema: string;
  /** Propiedad que decide el tipo de trámite (`transaction_type`). */
  propiedad?: string;
  tipos: TipoTramite[];
  filas: FilaMatriz[];
  /** `campos` o `documentos`. */
  clase: 'campos' | 'documentos';
}

export interface FilaTabla {
  codigo: string;
  descripcion?: string;
}

export interface TablaReferencia {
  /** Nombre del componente (`TipoDocumento`). */
  id: string;
  titulo: string;
  descripcion?: string;
  filas: FilaTabla[];
}

const NIVELES: Record<string, Obligatoriedad> = {
  obligatorio: 'obligatorio',
  obligatoria: 'obligatorio',
  si: 'obligatorio',
  opcional: 'opcional',
  no_aplica: 'no_aplica',
  'no aplica': 'no_aplica',
  na: 'no_aplica',
};

export const ETIQUETAS_OBLIGATORIEDAD: Record<Obligatoriedad, string> = {
  obligatorio: 'Obligatorio',
  opcional: 'Opcional',
  no_aplica: 'No aplica',
};

function nivel(valor: unknown): Obligatoriedad {
  return typeof valor === 'string' ? (NIVELES[valor.trim().toLowerCase()] ?? 'no_aplica') : 'no_aplica';
}

/** «traspaso_bilateral» → «Traspaso bilateral» cuando el spec no trae el nombre. */
const legible = (clave: string): string => {
  const t = clave.replace(/_/g, ' ');
  return t.charAt(0).toUpperCase() + t.slice(1);
};

function esquemas(raiz: Objeto): Array<[string, Objeto]> {
  const componentes = esObjeto(raiz.components) && esObjeto(raiz.components.schemas) ? raiz.components.schemas : {};
  return Object.entries(componentes).filter((e): e is [string, Objeto] => esObjeto(e[1]));
}

export function matricesDeObligatoriedad(raiz: Objeto): MatrizObligatoriedad[] {
  const salida: MatrizObligatoriedad[] = [];
  for (const [nombre, esquema] of esquemas(raiz)) {
    const extension = esquema['x-flit-obligatoriedad'];
    if (!esObjeto(extension) || !Array.isArray(extension.tipos)) continue;
    const tipos: TipoTramite[] = extension.tipos.filter(esObjeto).flatMap((t) => {
      const clave = texto(t.clave);
      if (!clave) return [];
      return [{ clave, nombre: texto(t.nombre) ?? texto(t.columna_excel) ?? legible(clave), valor: t.valor === undefined ? undefined : String(t.valor) }];
    });
    if (tipos.length === 0) continue;
    const propiedades = esObjeto(esquema.properties) ? esquema.properties : {};
    const clase = Array.isArray(extension.documentos) ? 'documentos' : 'campos';
    const crudas = (clase === 'documentos' ? extension.documentos : extension.campos) as unknown;
    const filas: FilaMatriz[] = (Array.isArray(crudas) ? crudas : []).filter(esObjeto).flatMap((f) => {
      const campo = clase === 'documentos' ? (f.valor === undefined ? texto(f.nombre) : String(f.valor)) : texto(f.campo);
      if (!campo) return [];
      const propiedad = propiedades[campo];
      const detalle = clase === 'documentos' ? texto(f.nombre) : esObjeto(propiedad) ? texto(propiedad.description) : undefined;
      return [{ campo, detalle, condicion: texto(f.condicion), valores: Object.fromEntries(tipos.map((t) => [t.clave, nivel(f[t.clave])])) }];
    });
    salida.push({ esquema: nombre, propiedad: texto(extension.propiedad), tipos, filas, clase });
  }
  return salida;
}

/** Los esquemas reutilizables con `enum` (y sus descripciones) como tablas de código y descripción. */
export function tablasDeReferencia(raiz: Objeto): TablaReferencia[] {
  const salida: TablaReferencia[] = [];
  for (const [nombre, esquema] of esquemas(raiz)) {
    const valores = valoresPermitidos(esquema);
    if (!valores || valores.length === 0) continue;
    const tabla = esObjeto(esquema['x-flit-tabla']) ? esquema['x-flit-tabla'] : {};
    salida.push({ id: nombre, titulo: texto(tabla.titulo) ?? nombre, descripcion: texto(esquema.description), filas: valores });
  }
  return salida.sort((a, b) => a.titulo.localeCompare(b.titulo, 'es'));
}

/** Quita tildes y pasa a minúsculas, para buscar «Cédula» con «cedula». */
export const normalizar = (t: string): string =>
  t
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase();
