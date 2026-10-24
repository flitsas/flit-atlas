import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { Boton, cn, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  cambiarCompensacion,
  CLAVE,
  type Compensacion,
  listarSolicitudes,
  obtenerReporteCompensaciones,
  type SolicitudCambio,
  TEXTO_PASO,
  TEXTO_SOLICITUD,
} from '../api';
import { cancelarSolicitud, resolverSolicitud, responderSolicitud, textoResultado, usePendientesSoporte } from '../acciones';
import { type Aviso, avisoDeError, claseCampo, MostrarAviso, PendientesDeEnviar } from '../componentes';
import { formatearDia, formatearDiaLargo, hoy } from '../fechas';

const fechaHora = new Intl.DateTimeFormat('es-CO', { timeZone: 'America/Bogota', dateStyle: 'short', timeStyle: 'short' });

/**
 * Cambios de turno y compensaciones (HU 2.9): las solicitudes en las que participas o que puedes aprobar, con su
 * historial y sus acciones (que funcionan sin conexión), y el reporte de compensaciones pendientes por persona.
 */
export default function Cambios() {
  const [aviso, setAviso] = useState<Aviso>(null);
  const [cerradas, setCerradas] = useState(false);
  const pendientes = usePendientesSoporte();
  const solicitudes = useQuery({
    queryKey: [...CLAVE, 'solicitudes', cerradas ? 'todas' : 'abiertas'],
    queryFn: () => listarSolicitudes(cerradas ? 'todas' : 'abiertas'),
  });
  const reporte = useQuery({ queryKey: [...CLAVE, 'compensaciones', 'pendientes'], queryFn: obtenerReporteCompensaciones });
  const enCola = new Set(pendientes.map((p) => p.recurso));

  return (
    <div className="mx-auto flex max-w-4xl min-w-0 flex-col gap-5">
      <Link to="/soporte" className="inline-flex min-h-toque items-center gap-1 self-start text-sm font-medium text-atlas-tinta hover:underline">
        <ArrowLeft className="size-4" aria-hidden="true" />
        Volver al calendario
      </Link>
      <h1 className="text-2xl font-semibold">Cambios de turno y compensaciones</h1>
      <MostrarAviso aviso={aviso} />
      <PendientesDeEnviar acciones={pendientes} />

      <Tarjeta titulo="Solicitudes de cambio" icono="calendario">
        <label className="mb-2 flex min-h-toque cursor-pointer items-center gap-2 text-sm">
          <input type="checkbox" checked={cerradas} onChange={(e) => setCerradas(e.target.checked)} className="size-5 shrink-0" />
          Ver también las cerradas
        </label>
        {solicitudes.isPending && <p role="status">Cargando las solicitudes…</p>}
        {solicitudes.isError && <p role="alert">No se pudieron cargar las solicitudes. Revisa tu conexión.</p>}
        {solicitudes.data?.length === 0 && (
          <p className="text-sm text-atlas-texto-suave">
            No hay solicitudes {cerradas ? '' : 'abiertas '}en las que participes. Pide un cambio desde tu turno en el calendario.
          </p>
        )}
        {solicitudes.data && solicitudes.data.length > 0 && (
          <ul aria-label="Solicitudes" className="flex min-w-0 flex-col gap-3">
            {solicitudes.data.map((s) => (
              <li key={s.id} className="min-w-0">
                <TarjetaSolicitud solicitud={s} enCola={enCola.has(s.id)} alAvisar={setAviso} />
              </li>
            ))}
          </ul>
        )}
      </Tarjeta>

      <Tarjeta titulo="Compensaciones pendientes" icono="equipo">
        {reporte.isPending && <p role="status">Cargando las compensaciones…</p>}
        {reporte.isError && <p role="alert">No se pudieron cargar las compensaciones.</p>}
        {reporte.data?.total === 0 && <p className="text-sm text-atlas-texto-suave">No hay compensaciones pendientes.</p>}
        {reporte.data && reporte.data.total > 0 && (
          <ul aria-label="Compensaciones pendientes por persona" className="flex min-w-0 flex-col gap-3">
            {reporte.data.personas.map((p) => (
              <li key={p.persona.id} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
                <p className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-medium break-words">{p.persona.nombre}</span>
                  <Insignia tono="info">
                    {p.pendientes} {p.pendientes === 1 ? 'pendiente' : 'pendientes'}
                  </Insignia>
                </p>
                <ul className="flex min-w-0 flex-col gap-2">
                  {p.compensaciones.map((c) => (
                    <FilaCompensacion key={c.id} compensacion={c} alAvisar={setAviso} />
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        )}
      </Tarjeta>
    </div>
  );
}

function TarjetaSolicitud({ solicitud: s, enCola, alAvisar }: { solicitud: SolicitudCambio; enCola: boolean; alAvisar: (a: Aviso) => void }) {
  const cliente = useQueryClient();
  const [nota, setNota] = useState('');
  const [enviando, setEnviando] = useState(false);
  const abierta = s.estado === 'pendiente_reemplazo' || s.estado === 'pendiente_aprobacion';
  const conAcciones = !enCola && (s.puedeResponder || s.puedeResolver || s.puedeCancelar);

  async function hacer(trabajo: () => ReturnType<typeof cancelarSolicitud>, exito: string) {
    setEnviando(true);
    alAvisar(null);
    try {
      const resultado = await trabajo();
      if (resultado.estado === 'enviada') await cliente.invalidateQueries({ queryKey: CLAVE });
      alAvisar({ tono: 'exito', texto: textoResultado(resultado, exito) });
      setNota('');
    } catch (e) {
      alAvisar(avisoDeError(e, 'No se pudo hacer la acción.'));
    } finally {
      setEnviando(false);
    }
  }
  const textoNota = () => nota.trim() || null;

  return (
    <article
      aria-label={`Cambio del turno del ${formatearDia(s.turno.fecha)}`}
      className={cn('flex min-w-0 flex-col gap-3 rounded-xl border border-border p-3', !abierta && 'border-dashed')}
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold first-letter:uppercase">
          {formatearDiaLargo(s.turno.fecha)} · {s.turno.equipo.nombre}
        </h3>
        <div className="flex flex-wrap gap-1.5">
          {enCola && <Insignia tono="neutral">Pendiente de enviar</Insignia>}
          <Insignia tono={s.estado === 'aprobada' ? 'exito' : abierta ? 'info' : 'neutral'}>{TEXTO_SOLICITUD[s.estado]}</Insignia>
        </div>
      </header>
      <p className="flex min-w-0 flex-wrap items-center gap-2 text-sm">
        <span className="break-words">{s.solicitante.nombre}</span>
        <ArrowRight className="size-4 shrink-0" aria-label="lo toma" />
        <span className="break-words font-medium">{s.reemplazo.nombre}</span>
      </p>
      {s.motivo && <p className="text-sm break-words text-atlas-texto-suave">Motivo: {s.motivo}</p>}
      <ol aria-label="Historial" className="flex flex-col gap-1 border-l-2 border-border pl-3 text-xs">
        {s.historial.map((h, i) => (
          <li key={i} className="break-words">
            <span className="font-medium">{h.persona.nombre}</span> · {TEXTO_PASO[h.accion] ?? h.accion} ·{' '}
            {fechaHora.format(new Date(h.hechaEn ?? h.ocurridoEn))}
            {h.nota && <span className="block text-atlas-texto-suave">«{h.nota}»</span>}
          </li>
        ))}
      </ol>
      {conAcciones && (
        <div className="flex min-w-0 flex-col gap-2">
          <label className="text-sm font-medium">
            Nota (opcional)
            <input value={nota} maxLength={500} onChange={(e) => setNota(e.target.value)} className={claseCampo} />
          </label>
          <div className="flex flex-wrap gap-2">
            {s.puedeResponder && (
              <>
                <Boton disabled={enviando} onClick={() => void hacer(() => responderSolicitud(s, true, textoNota()), 'Aceptaste el reemplazo.')}>
                  Aceptar el reemplazo
                </Boton>
                <Boton variante="secundario" disabled={enviando} onClick={() => void hacer(() => responderSolicitud(s, false, textoNota()), 'Rechazaste el reemplazo.')}>
                  No puedo
                </Boton>
              </>
            )}
            {s.puedeResolver && (
              <>
                <Boton disabled={enviando} onClick={() => void hacer(() => resolverSolicitud(s, true, textoNota()), 'Se aprobó el cambio de turno.')}>
                  Aprobar el cambio
                </Boton>
                <Boton variante="secundario" disabled={enviando} onClick={() => void hacer(() => resolverSolicitud(s, false, textoNota()), 'Se rechazó el cambio.')}>
                  Rechazar
                </Boton>
              </>
            )}
            {s.puedeCancelar && (
              <Boton variante="fantasma" disabled={enviando} onClick={() => void hacer(() => cancelarSolicitud(s, textoNota()), 'Cancelaste la solicitud.')}>
                Cancelar la solicitud
              </Boton>
            )}
          </div>
        </div>
      )}
    </article>
  );
}

function FilaCompensacion({ compensacion: c, alAvisar }: { compensacion: Compensacion; alAvisar: (a: Aviso) => void }) {
  const cliente = useQueryClient();
  const [fecha, setFecha] = useState(c.fechaCompensada ?? '');
  const [enviando, setEnviando] = useState(false);

  async function guardar(estado: 'pendiente' | 'tomada') {
    setEnviando(true);
    alAvisar(null);
    try {
      await cambiarCompensacion(c.id, { fechaCompensada: fecha || null, estado, nota: c.nota });
      await cliente.invalidateQueries({ queryKey: CLAVE });
      alAvisar({ tono: 'exito', texto: estado === 'tomada' ? 'Compensación marcada como tomada.' : 'Se guardó el día compensado.' });
    } catch (e) {
      alAvisar(avisoDeError(e, 'No se pudo guardar la compensación.'));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <li className="flex min-w-0 flex-col gap-2 text-sm">
      <p className="break-words">
        Por el turno del {formatearDia(c.turno.fecha)} ({c.turno.equipo.nombre}) ·{' '}
        {c.fechaCompensada ? `día compensado: ${formatearDia(c.fechaCompensada)}` : 'sin día elegido'}
        {c.aprobadaPor && <span className="text-atlas-texto-suave"> · aprobó {c.aprobadaPor.nombre}</span>}
      </p>
      {c.nota && <p className="text-xs break-words text-atlas-texto-suave">{c.nota}</p>}
      {c.puedeCambiar && (
        <div className="flex min-w-0 flex-wrap items-end gap-2">
          <label className="min-w-0 font-medium">
            Día compensado
            <input type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} className={claseCampo} />
          </label>
          <Boton variante="secundario" disabled={enviando} onClick={() => void guardar('pendiente')}>
            Guardar el día
          </Boton>
          <Boton disabled={enviando || !fecha || fecha > hoy()} title={fecha > hoy() ? 'Se marca tomada el día compensado o después' : undefined} onClick={() => void guardar('tomada')}>
            Marcar tomada
          </Boton>
        </div>
      )}
    </li>
  );
}
