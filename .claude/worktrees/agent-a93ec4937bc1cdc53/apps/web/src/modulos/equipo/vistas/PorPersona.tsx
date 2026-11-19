import { Link } from 'react-router';

import { iniciales } from '../../../app/ui/formato';
import { Avatar } from '../../../app/ui/Marca';
import { Tarjeta } from '../../../app/ui/Tarjeta';
import { type AplicativoRef, obtenerReparto, useReparto } from '../apiResponsables';

function Lista({ titulo, aplicativos }: { titulo: string; aplicativos: AplicativoRef[] }) {
  return (
    <div className="min-w-0">
      <p className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">{titulo}</p>
      {aplicativos.length === 0 ? (
        <p className="text-sm text-atlas-texto-suave">—</p>
      ) : (
        <ul className="mt-1 flex flex-wrap gap-1.5" aria-label={titulo}>
          {aplicativos.map((a) => (
            <li key={a.id}>
              <Link
                to={`/catalogo/${a.slug}`}
                className="inline-flex min-h-toque items-center rounded-xl border border-border px-3 text-sm break-words hover:bg-atlas-activo"
              >
                {a.nombre}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Reparto por persona (tabla 2 del documento): de qué es principal y de qué es secundaria cada una. */
export default function PorPersona() {
  const reparto = useReparto(['vigente'], obtenerReparto);
  if (reparto.isPending) return <p role="status">Cargando el reparto…</p>;
  if (reparto.isError || !reparto.data) return <p role="alert">No pudimos cargar el reparto. Revisa tu conexión.</p>;

  return (
    <Tarjeta titulo="Reparto por persona">
      <ul aria-label="Personas responsables" className="grid gap-3 lg:grid-cols-2">
        {reparto.data.porPersona.map(({ persona, principalDe, secundarioDe }) => (
          <li key={persona.id} className="flex min-w-0 flex-col gap-3 rounded-xl border border-border p-3">
            <div className="flex min-w-0 items-center gap-3">
              <Avatar iniciales={iniciales(persona.nombre)} foto={persona.foto} className="size-10" />
              <div className="min-w-0">
                <p className="font-semibold break-words">{persona.nombre}</p>
                {persona.cargo && <p className="text-xs break-words text-atlas-texto-suave">{persona.cargo}</p>}
              </div>
            </div>
            <Lista titulo="Principal de" aplicativos={principalDe} />
            <Lista titulo="Secundario de" aplicativos={secundarioDe} />
          </li>
        ))}
      </ul>
    </Tarjeta>
  );
}
