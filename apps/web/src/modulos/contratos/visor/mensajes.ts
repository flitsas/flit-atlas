/**
 * Mensajes entre la pantalla del contrato y el visor OpenAPI aislado (`/visor/openapi.html`, ATLAS-ADR-0039). El visor
 * corre en un iframe con `sandbox="allow-scripts"` (origen opaco): no tiene la cookie del BFF ni puede llamar a la API, así
 * que el spec se lo entrega la pantalla por `postMessage`.
 */
export const RUTA_VISOR = '/visor/openapi.html';

/** El visor ya escucha: la pantalla le puede mandar el spec. */
export interface MensajeListo {
  tipo: 'atlas:visor-listo';
}

/** El spec a mostrar, los servidores de la consola (solo QA y sandbox) y el tema. */
export interface MensajeSpec {
  tipo: 'atlas:visor-spec';
  contenido: string;
  servidores: Array<{ url: string; description: string }>;
  oscuro: boolean;
}

/** El visor no pudo mostrar el spec. */
export interface MensajeError {
  tipo: 'atlas:visor-error';
}

export type MensajeVisor = MensajeListo | MensajeError;

export function esMensajeVisor(dato: unknown): dato is MensajeVisor {
  const tipo = (dato as { tipo?: unknown } | null)?.tipo;
  return tipo === 'atlas:visor-listo' || tipo === 'atlas:visor-error';
}

export function esMensajeSpec(dato: unknown): dato is MensajeSpec {
  const d = dato as Partial<MensajeSpec> | null;
  return d?.tipo === 'atlas:visor-spec' && typeof d.contenido === 'string' && Array.isArray(d.servidores) && typeof d.oscuro === 'boolean';
}
