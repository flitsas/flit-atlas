import { useQuery } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { Boton, Tarjeta } from '../../../app/ui/Tarjeta';
import { CLAVE, obtenerIndicadores, useEquipos } from '../api';
import { FiltroEquipo } from '../componentes';
import { hoy } from '../fechas';

const porcentaje = (valor: number | null) => (valor === null ? '—' : `${Math.round(valor * 100)} %`);

/** Indicadores básicos (HU 2.6): turnos por persona, reemplazos, compensaciones pendientes y cumplimiento, por año. */
export default function Indicadores() {
  const [anio, setAnio] = useState(() => Number(hoy().slice(0, 4)));
  const [equipo, setEquipo] = useState<string | null>(null);
  const equipos = useEquipos();
  const desde = `${anio}-01-01`;
  const hasta = `${anio}-12-31`;
  const datos = useQuery({ queryKey: [...CLAVE, 'indicadores', anio, equipo ?? 'todos'], queryFn: () => obtenerIndicadores(desde, hasta, equipo) });

  return (
    <div className="mx-auto flex max-w-4xl min-w-0 flex-col gap-5">
      <Link to="/soporte" className="inline-flex min-h-toque items-center gap-1 self-start text-sm font-medium text-atlas-tinta hover:underline">
        <ArrowLeft className="size-4" aria-hidden="true" />
        Volver al calendario
      </Link>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Indicadores del soporte {anio}</h1>
        <div className="flex gap-2">
          <Boton variante="secundario" onClick={() => setAnio(anio - 1)}>
            {anio - 1}
          </Boton>
          <Boton variante="secundario" onClick={() => setAnio(anio + 1)}>
            {anio + 1}
          </Boton>
        </div>
      </div>
      {equipos.data && equipos.data.length > 1 && <FiltroEquipo equipos={equipos.data} valor={equipo} alCambiar={setEquipo} />}
      {datos.isPending && <p role="status">Cargando los indicadores…</p>}
      {datos.isError && <p role="alert">No se pudieron cargar los indicadores.</p>}
      {datos.data && (
        <>
          <dl className="grid grid-cols-2 gap-3 md:grid-cols-4">
            {[
              ['Turnos', datos.data.turnos.toString()],
              ['Reemplazos', datos.data.reemplazos.toString()],
              ['Compensaciones pendientes', datos.data.compensacionesPendientes.toString()],
              ['Cumplimiento', porcentaje(datos.data.cumplimiento)],
            ].map(([nombre, valor]) => (
              <div key={nombre} className="min-w-0 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
                <dt className="text-xs text-atlas-texto-suave">{nombre}</dt>
                <dd className="text-2xl font-semibold">{valor}</dd>
              </div>
            ))}
          </dl>
          <Tarjeta titulo="Por persona" icono="equipo">
            {datos.data.personas.length === 0 ? (
              <p className="text-sm text-atlas-texto-suave">No hay turnos en {anio}.</p>
            ) : (
              <ul aria-label="Indicadores por persona" className="flex flex-col gap-2 text-sm">
                {datos.data.personas.map((p) => (
                  <li key={p.personaId} className="min-w-0 rounded-xl border border-border p-3">
                    <p className="font-medium break-words">{p.nombre}</p>
                    <p className="text-atlas-texto-suave">
                      {p.turnos} turnos · {p.aplicados} aplicados · {p.reemplazados} reemplazados · {p.reemplazosHechos} en reemplazo · {p.cancelados} cancelados ·{' '}
                      {p.compensacionesPendientes} compensaciones pendientes · cumplimiento {porcentaje(p.cumplimiento)}
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </Tarjeta>
          <p className="text-xs text-atlas-texto-suave">El cumplimiento es la parte de los turnos ya pasados que quedó aplicada; los reemplazados no cuentan en contra.</p>
        </>
      )}
    </div>
  );
}
