/**
 * Mensajes entre el lector y el visor Mermaid aislado (`/visor/mermaid.html`, ATLAS-ADR-0053 §5). Corre en un iframe con
 * `sandbox="allow-scripts"` (origen opaco): sin cookie del BFF ni acceso a la API. La fuente llega por `postMessage`.
 */
export const RUTA_VISOR_MERMAID = '/visor/mermaid.html';

/** Tope de la fuente de un diagrama: lo demás se muestra como código. */
export const LARGO_MAXIMO_MERMAID = 20_000;

export interface MensajeFuente {
  tipo: 'atlas:mermaid-fuente';
  fuente: string;
  oscuro: boolean;
}

export type MensajeVisorMermaid =
  | { tipo: 'atlas:mermaid-listo' }
  | { tipo: 'atlas:mermaid-pintado'; alto: number }
  | { tipo: 'atlas:mermaid-error' };

export function esMensajeVisorMermaid(dato: unknown): dato is MensajeVisorMermaid {
  const d = dato as { tipo?: unknown; alto?: unknown } | null;
  return (
    d?.tipo === 'atlas:mermaid-listo' ||
    d?.tipo === 'atlas:mermaid-error' ||
    (d?.tipo === 'atlas:mermaid-pintado' && typeof d.alto === 'number' && Number.isFinite(d.alto))
  );
}

export function esMensajeFuente(dato: unknown): dato is MensajeFuente {
  const d = dato as Partial<MensajeFuente> | null;
  return d?.tipo === 'atlas:mermaid-fuente' && typeof d.fuente === 'string' && d.fuente.length <= LARGO_MAXIMO_MERMAID && typeof d.oscuro === 'boolean';
}
