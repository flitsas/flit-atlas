import { DiagramaMermaid } from './DiagramaMermaid';
import { leerLienzo } from './lienzo';
import { LienzoSvg } from './LienzoSvg';
import { VisorBpmn } from './VisorBpmn';

export type TipoDiagrama = 'lienzo' | 'mermaid' | 'bpmn';

/** Un diagrama según su tipo (ATLAS-ADR-0054 §6): lo usan la página del módulo y `<Diagrama/>` de los documentos. */
export function VistaDiagrama({ tipo, fuente, titulo }: { tipo: TipoDiagrama; fuente: string; titulo: string }) {
  if (tipo === 'mermaid') return <DiagramaMermaid fuente={fuente} />;
  if (tipo === 'bpmn') return <VisorBpmn xml={fuente} titulo={titulo} />;
  const lienzo = leerLienzo(fuente);
  if (!lienzo) return <p className="my-3 text-sm text-atlas-texto-suave">No pudimos leer este lienzo.</p>;
  return <LienzoSvg lienzo={lienzo} titulo={titulo} />;
}
