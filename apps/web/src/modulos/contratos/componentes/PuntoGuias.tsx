import { lazy, Suspense } from 'react';

/**
 * Pestaña «Guías» de la pantalla del contrato (HU 3.6): monta las guías con revisiones de la HU 3.7
 * (`guias/GuiasDelContrato.tsx`, ATLAS-ADR-0040) en su propio chunk, filtradas a la versión que se está viendo. El
 * enlace directo `/contratos/:contratoId/guias/:guiaId` monta el mismo componente.
 */
export interface PropsGuiasDelContrato {
  contratoId: string;
  /** La versión que se está viendo. */
  version: string;
}

const GuiasDelContrato = lazy(() => import('../guias/GuiasDelContrato'));

export function PuntoGuias({ contratoId, version }: PropsGuiasDelContrato) {
  return (
    <Suspense fallback={<div role="status" aria-label="Cargando las guías" className="h-32 animate-pulse rounded-2xl bg-card" />}>
      <GuiasDelContrato key={`${contratoId}/${version}`} contratoId={contratoId} version={version} />
    </Suspense>
  );
}
