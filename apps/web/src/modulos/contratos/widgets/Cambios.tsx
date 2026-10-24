import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router';

import { formatearRelativo } from '../../../app/ui/formato';
import { useContratos } from '../api';

/** Widget de Inicio: las últimas versiones publicadas desde la CI (las alertas de cambios llegan con el diff, HU 3.5). */
export default function Cambios() {
  const lista = useContratos();
  if (lista.isPending) return <div role="status" aria-label="Cargando los contratos" className="h-20 animate-pulse rounded-xl bg-atlas-activo" />;
  if (lista.isError) return <p className="text-sm text-atlas-texto-suave">No pudimos cargar los contratos.</p>;

  const recientes = lista.data.elementos
    .filter((c) => c.ultimaVersion)
    .sort((a, b) => b.ultimaVersion!.publicadoEn.localeCompare(a.ultimaVersion!.publicadoEn))
    .slice(0, 3);

  return (
    <div className="flex h-full flex-col gap-3">
      {recientes.length === 0 ? (
        <p className="text-sm text-atlas-texto-suave">Todavía no hay contratos publicados desde la CI.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {recientes.map((c) => (
            <li key={c.id}>
              <Link
                to={`/contratos?contrato=${c.id}`}
                className="flex min-h-toque items-center justify-between gap-2 rounded-xl border border-border px-3 py-2 text-sm hover:bg-atlas-activo"
              >
                <span className="min-w-0">
                  <span className="block truncate font-medium">{c.nombre}</span>
                  <span className="block truncate text-xs text-atlas-texto-suave">
                    {c.ultimaVersion!.version} · {formatearRelativo(new Date(c.ultimaVersion!.publicadoEn))}
                  </span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
      <Link
        to="/contratos"
        className="mt-auto inline-flex min-h-toque items-center gap-1 self-end rounded-lg px-2 text-sm font-medium text-atlas-tinta hover:bg-atlas-activo"
      >
        Ver los contratos
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </div>
  );
}
