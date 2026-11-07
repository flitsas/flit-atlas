/** El modelo propio del lienzo (ATLAS-ADR-0054 §2): el JSON de cada versión. */
export type TipoNodo = 'actor' | 'sistema' | 'contenedor' | 'componente' | 'nota';

export interface NodoLienzo {
  id: string;
  tipo: TipoNodo;
  etiqueta: string;
  x: number;
  y: number;
  aplicativoId?: string;
  elementoLikeC4?: string;
}

export interface RelacionLienzo {
  id: string;
  origen: string;
  destino: string;
  etiqueta?: string;
}

export interface Lienzo {
  nodos: NodoLienzo[];
  relaciones: RelacionLienzo[];
}

export const TEXTO_TIPO_NODO: Record<TipoNodo, string> = {
  actor: 'Actor',
  sistema: 'Sistema',
  contenedor: 'Contenedor',
  componente: 'Componente',
  nota: 'Nota',
};

/** Tamaño fijo de los nodos: el modelo guarda la posición, no el dibujo. */
export const ANCHO_NODO = 180;
export const ALTO_NODO = 64;

/** Lee el JSON que devuelve la API; `null` si no tiene la forma esperada. */
export function leerLienzo(fuente: string): Lienzo | null {
  try {
    const dato = JSON.parse(fuente) as Partial<Lienzo> | null;
    if (!Array.isArray(dato?.nodos) || !Array.isArray(dato.relaciones)) return null;
    return { nodos: dato.nodos, relaciones: dato.relaciones };
  } catch {
    return null;
  }
}
