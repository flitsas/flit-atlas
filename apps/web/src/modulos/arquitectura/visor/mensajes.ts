/**
 * Mensajes con el visor de LikeC4 aislado (`/visor/likec4.html`, ATLAS-ADR-0056 §7). Corre en un iframe con `sandbox="allow-scripts"`
 * (origen opaco): sin cookie del BFF ni acceso a la API. El JSON llega por `postMessage`; lo que sale son solo ids, y quien los recibe
 * los revisa contra el modelo antes de pedir nada.
 */
export const RUTA_VISOR_LIKEC4 = '/visor/likec4.html';

/** El id de un elemento o una vista, con el mismo tope del servidor. */
const LARGO_MAXIMO_ID = 300;

export interface MensajeModelo {
  tipo: 'atlas:likec4-modelo';
  /** Identidad de la publicación: el modelo solo se vuelve a armar si cambia. */
  clave: string;
  modelo: unknown;
  vista: string;
  oscuro: boolean;
}

export type MensajeVisorLikeC4 =
  | { tipo: 'atlas:likec4-listo' }
  | { tipo: 'atlas:likec4-pintado' }
  | { tipo: 'atlas:likec4-error' }
  | { tipo: 'atlas:likec4-elemento'; id: string }
  | { tipo: 'atlas:likec4-navegar'; vista: string };

const esId = (valor: unknown): valor is string => typeof valor === 'string' && valor.length > 0 && valor.length <= LARGO_MAXIMO_ID;

export function esMensajeVisorLikeC4(dato: unknown): dato is MensajeVisorLikeC4 {
  const d = dato as { tipo?: unknown; id?: unknown; vista?: unknown } | null;
  return (
    d?.tipo === 'atlas:likec4-listo' ||
    d?.tipo === 'atlas:likec4-pintado' ||
    d?.tipo === 'atlas:likec4-error' ||
    (d?.tipo === 'atlas:likec4-elemento' && esId(d.id)) ||
    (d?.tipo === 'atlas:likec4-navegar' && esId(d.vista))
  );
}

export function esMensajeModelo(dato: unknown): dato is MensajeModelo {
  const d = dato as Partial<MensajeModelo> | null;
  return (
    d?.tipo === 'atlas:likec4-modelo' &&
    typeof d.clave === 'string' &&
    esId(d.vista) &&
    typeof d.oscuro === 'boolean' &&
    typeof d.modelo === 'object' &&
    d.modelo !== null
  );
}
