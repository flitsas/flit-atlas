/**
 * Mensajes con el visor BPMN aislado (`/visor/bpmn.html`, ATLAS-ADR-0054 §5). Corre en un iframe con `sandbox="allow-scripts"`
 * (origen opaco): sin cookie del BFF ni acceso a la API. El XML llega y sale por `postMessage`.
 */
export const RUTA_VISOR_BPMN = '/visor/bpmn.html';

/** El tope del servidor: lo demás no se dibuja. */
export const LARGO_MAXIMO_BPMN = 1_000_000;

export interface MensajeXmlBpmn {
  tipo: 'atlas:bpmn-xml';
  xml: string;
  editable: boolean;
}

export type MensajeVisorBpmn =
  | { tipo: 'atlas:bpmn-listo' }
  | { tipo: 'atlas:bpmn-pintado' }
  | { tipo: 'atlas:bpmn-error' }
  | { tipo: 'atlas:bpmn-cambio'; xml: string };

export function esMensajeVisorBpmn(dato: unknown): dato is MensajeVisorBpmn {
  const d = dato as { tipo?: unknown; xml?: unknown } | null;
  return (
    d?.tipo === 'atlas:bpmn-listo' ||
    d?.tipo === 'atlas:bpmn-pintado' ||
    d?.tipo === 'atlas:bpmn-error' ||
    (d?.tipo === 'atlas:bpmn-cambio' && typeof d.xml === 'string' && d.xml.length <= LARGO_MAXIMO_BPMN)
  );
}

export function esMensajeXmlBpmn(dato: unknown): dato is MensajeXmlBpmn {
  const d = dato as Partial<MensajeXmlBpmn> | null;
  return d?.tipo === 'atlas:bpmn-xml' && typeof d.xml === 'string' && d.xml.length <= LARGO_MAXIMO_BPMN && typeof d.editable === 'boolean';
}
