import { useQuery } from '@tanstack/react-query';
import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router';

import { Insignia } from '../../../app/ui/Tarjeta';
import { CLAVE, horario, obtenerEsteSabado } from '../api';
import { Avatar } from '../componentes';
import { formatearDia } from '../fechas';

/** Widget de Inicio «Soporte de este sábado» (prompt maestro §8.3): a quién llamar en cada equipo, con horario y contacto. */
export default function EsteSabado() {
  const sabado = useQuery({ queryKey: [...CLAVE, 'este-sabado'], queryFn: obtenerEsteSabado, staleTime: 60_000 });
  const datos = sabado.data;

  return (
    <div className="flex flex-col gap-3 text-sm">
      {sabado.isPending && <p role="status">Cargando el turno…</p>}
      {sabado.isError && <p role="alert">No pudimos cargar el soporte de este sábado.</p>}
      {datos && (
        <>
          <p className="flex flex-wrap items-baseline gap-2">
            <span className="text-2xl font-semibold">Sábado</span>
            <span className="text-lg text-atlas-texto-suave">{formatearDia(datos.fecha)}</span>
            {datos.festivo && <Insignia tono="neutral">Festivo: {datos.festivo}</Insignia>}
          </p>
          {datos.equipos.length === 0 && <p className="text-atlas-texto-suave">No hay equipos de soporte activos.</p>}
          <ul aria-label="A quién llamar por equipo" className="flex flex-col gap-3">
            {datos.equipos.map(({ equipo, turnos }) => (
              <li key={equipo.id} className="min-w-0">
                <p className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">{equipo.nombre}</p>
                {turnos.length === 0 && <p className="text-atlas-texto-suave">{datos.festivo ? 'Sin turno: es festivo.' : 'Aún no hay turno.'}</p>}
                <ul className="mt-1 flex flex-col gap-2">
                  {turnos.map((t) => (
                    <li key={t.id} className="flex min-w-0 items-start gap-3">
                      <Avatar nombre={t.persona.nombre} />
                      <div className="min-w-0">
                        <p className="font-medium break-words">{t.persona.nombre}</p>
                        <p>{horario(t) ?? <span className="text-atlas-texto-suave">Horario por definir</span>}</p>
                        {t.persona.telefono && (
                          <a href={`tel:${t.persona.telefono}`} className="inline-flex min-h-toque items-center font-medium text-atlas-tinta hover:underline">
                            {t.persona.telefono}
                          </a>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </>
      )}
      <Link to="/soporte" className="inline-flex min-h-toque items-center gap-1 self-start font-medium text-atlas-tinta hover:underline">
        Ver el calendario
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </div>
  );
}
