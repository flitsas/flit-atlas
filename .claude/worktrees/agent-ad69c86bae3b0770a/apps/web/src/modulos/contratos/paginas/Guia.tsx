import { ArrowLeft } from 'lucide-react';
import { Link, useParams } from 'react-router';

import GuiasDelContrato from '../guias/GuiasDelContrato';

/**
 * Enlace directo a una guía (`/contratos/:contratoId/guias/:guiaId`, HU 3.7): abre las guías del contrato en esa guía.
 * La pestaña «Guías» de la pantalla del contrato (HU 3.6) monta el mismo componente.
 */
export default function Guia() {
  const { contratoId = '', guiaId } = useParams();
  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-4">
      <Link to={`/contratos/${encodeURIComponent(contratoId)}?pestana=guias`} className="inline-flex min-h-toque items-center gap-2 self-start text-sm underline">
        <ArrowLeft className="size-4" aria-hidden="true" /> Contrato
      </Link>
      <GuiasDelContrato key={`${contratoId}/${guiaId ?? ''}`} contratoId={contratoId} guiaId={guiaId} />
    </div>
  );
}
