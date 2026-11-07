import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router';

import { formatearRelativo } from '../../../app/ui/formato';
import { useDocumentos } from '../api';

/** Widget de Inicio: lo último que cambió entre los primeros 100 documentos que la persona puede ver. */
export default function Recientes() {
  const lista = useDocumentos();
  if (lista.isPending) return <div role="status" aria-label="Cargando los documentos" className="h-20 animate-pulse rounded-xl bg-atlas-activo" />;
  if (lista.isError) return <p className="text-sm text-atlas-texto-suave">No pudimos cargar los documentos.</p>;
  const recientes = [...lista.data.elementos].sort((a, b) => b.actualizadoEn.localeCompare(a.actualizadoEn)).slice(0, 3);
  return (
    <div className="flex h-full flex-col gap-3">
      {recientes.length === 0 ? (
        <p className="text-sm text-atlas-texto-suave">Todavía no hay documentos que puedas ver.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {recientes.map((d) => (
            <li key={d.id}>
              <Link to={`/documentacion/${d.id}`} className="flex min-h-toque min-w-0 flex-col rounded-xl border border-border px-3 py-2 text-sm hover:bg-atlas-activo">
                <span className="truncate font-medium">{d.titulo}</span>
                <span className="truncate text-xs text-atlas-texto-suave">{formatearRelativo(new Date(d.actualizadoEn))}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
      <Link to="/documentacion" className="mt-auto inline-flex min-h-toque items-center gap-1 self-end rounded-lg px-2 text-sm font-medium text-atlas-tinta hover:bg-atlas-activo">
        Ver la documentación
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </div>
  );
}
