import { ArrowRight, TriangleAlert } from 'lucide-react';
import { Link } from 'react-router';

import { Cargando } from '../guias/componentes';
import { useInicioPortal } from './api';

/** Inicio del cliente en la página de Inicio (solo externos): paquetes compartidos, avisos y el acceso al portal. */
export default function WidgetPortal() {
  const inicio = useInicioPortal();
  if (inicio.isPending) return <Cargando etiqueta="Cargando lo compartido" />;
  if (inicio.isError) return <p role="alert" className="text-sm">No pudimos cargar lo que FLIT comparte contigo.</p>;
  const { paquetes, avisos } = inicio.data;
  return (
    <div className="flex flex-col gap-3 text-sm">
      {paquetes.length === 0 ? (
        <p className="text-atlas-texto-suave">Todavía no hay paquetes compartidos con tu organización.</p>
      ) : (
        <ul aria-label="Paquetes compartidos" className="flex flex-col gap-1">
          {paquetes.slice(0, 4).map((p) => (
            <li key={p.id}>
              <Link to={`/portal/paquetes/${p.id}`} className="flex min-h-toque items-center rounded-lg px-2 font-medium break-words hover:bg-atlas-activo">
                {p.nombre}
              </Link>
            </li>
          ))}
        </ul>
      )}
      {avisos.length > 0 && (
        <p role="note" className="flex items-start gap-2">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          {avisos.length === 1 ? 'Hay 1 aviso de deprecación.' : `Hay ${avisos.length} avisos de deprecación.`}
        </p>
      )}
      <Link to="/portal" className="inline-flex min-h-toque items-center gap-1 self-start rounded-lg px-2 font-medium text-atlas-tinta hover:bg-atlas-activo">
        Abrir el portal
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </div>
  );
}
