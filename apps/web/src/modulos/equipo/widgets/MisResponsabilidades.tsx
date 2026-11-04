import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router';

import { obtenerMias, useReparto } from '../apiResponsables';
import { InsigniaTraspaso } from '../componentes';

/** Widget de Inicio «Mis responsabilidades» (§8.9): de qué aplicativos es principal o secundaria la persona. */
export default function MisResponsabilidades() {
  const mias = useReparto(['mias'], obtenerMias);
  const datos = mias.data;
  const total = (datos?.principalDe.length ?? 0) + (datos?.secundarioDe.length ?? 0);

  return (
    <div className="flex flex-col gap-3 text-sm">
      {mias.isPending && <p role="status">Cargando tus responsabilidades…</p>}
      {mias.isError && <p role="alert">No pudimos cargar tus responsabilidades.</p>}
      {datos && total === 0 && <p className="text-atlas-texto-suave">No eres responsable principal ni secundario de ningún aplicativo.</p>}
      {datos &&
        (
          [
            ['Principal de', datos.principalDe],
            ['Secundario de', datos.secundarioDe],
          ] as const
        )
          .filter(([, lista]) => lista.length > 0)
          .map(([titulo, lista]) => (
            <div key={titulo} className="min-w-0">
              <p className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">{titulo}</p>
              <ul aria-label={titulo} className="mt-1 flex flex-wrap gap-1.5">
                {lista.map(({ aplicativo, responsable }) => (
                  <li key={aplicativo.id} className="flex items-center gap-1">
                    <Link
                      to={`/catalogo/${aplicativo.slug}?pestana=responsables`}
                      className="inline-flex min-h-toque items-center rounded-xl border border-border px-3 break-words hover:bg-atlas-activo"
                    >
                      {aplicativo.nombre}
                    </Link>
                    {responsable.estadoTraspaso === 'en_acompanamiento' && <InsigniaTraspaso responsable={responsable} />}
                  </li>
                ))}
              </ul>
            </div>
          ))}
      <Link to="/equipo/responsables" className="inline-flex min-h-toque items-center gap-1 self-start font-medium text-atlas-tinta hover:underline">
        Ver el reparto
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </div>
  );
}
