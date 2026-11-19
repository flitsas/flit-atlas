import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Copy, ExternalLink } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  CLAVE,
  crearFeed,
  type EstadoM365,
  type FeedIcs,
  type FeedIcsCreado,
  listarFeeds,
  obtenerEstadoM365,
  revocarFeed,
  type TipoFeed,
  useEquipos,
} from '../api';
import { type Aviso, avisoDeError, claseCampo, MostrarAviso } from '../componentes';

const fechaHora = new Intl.DateTimeFormat('es-CO', { timeZone: 'America/Bogota', dateStyle: 'short', timeStyle: 'short' });

/**
 * Microsoft 365 del soporte (HU 2.10, ATLAS-ADR-0033): si el calendario compartido de Outlook y los avisos de Teams están
 * activos en este ambiente (y, si no, por qué) y los feeds .ics para suscribirse a los turnos propios o de un equipo desde
 * cualquier app de calendario. La URL de un feed lleva su token y solo se muestra al crearla.
 */
export default function Microsoft365() {
  const estado = useQuery({ queryKey: [...CLAVE, 'microsoft-365'], queryFn: obtenerEstadoM365 });
  const [aviso, setAviso] = useState<Aviso>(null);

  return (
    <div className="mx-auto flex max-w-3xl min-w-0 flex-col gap-5">
      <Link to="/soporte" className="inline-flex min-h-toque items-center gap-1 self-start text-sm font-medium text-atlas-tinta hover:underline">
        <ArrowLeft className="size-4" aria-hidden="true" />
        Volver al calendario
      </Link>
      <div>
        <h1 className="text-2xl font-semibold">Outlook, Teams y calendarios</h1>
        <p className="text-sm text-atlas-texto-suave">Los turnos del soporte en tu calendario de siempre.</p>
      </div>
      <MostrarAviso aviso={aviso} />

      {estado.isPending && <p role="status">Cargando el estado de Microsoft 365…</p>}
      {estado.isError && <p role="alert">No se pudo cargar el estado de Microsoft 365. Revisa tu conexión.</p>}
      {estado.data && <EstadoIntegracion estado={estado.data} />}

      <Feeds alAvisar={setAviso} />
    </div>
  );
}

function EstadoIntegracion({ estado }: { estado: EstadoM365 }) {
  const { calendario, teams } = estado;
  return (
    <>
      <Tarjeta titulo="Calendario compartido de Outlook" icono="calendario" accion={<Activo activo={calendario.activo} />}>
        <p className="text-sm">
          Cada turno es un evento de «{calendario.nombreCalendario}» con quien atiende como asistente: aparece en su Outlook y en el calendario de
          Teams. Un cambio de turno aprobado lo pasa solo a la otra persona.
        </p>
        {calendario.activo ? (
          <dl className="mt-3 grid grid-cols-1 gap-2 text-sm sm:grid-cols-3">
            <Dato titulo="Por sincronizar" valor={String(calendario.pendientes)} />
            <Dato titulo="Con reintentos" valor={String(calendario.conErrores)} />
            <Dato
              titulo="Última sincronización"
              valor={calendario.ultimaSincronizacion ? fechaHora.format(new Date(calendario.ultimaSincronizacion)) : 'Aún ninguna'}
            />
          </dl>
        ) : (
          <Apagado motivo={calendario.motivo} />
        )}
      </Tarjeta>

      <Tarjeta titulo="Avisos en el canal de Teams" icono="equipo" accion={<Activo activo={teams.activo} />}>
        <p className="text-sm">Se avisa en el canal del soporte cuando se publica una rotación y cuando se aprueba un cambio de turno.</p>
        {teams.activo ? (
          <p className="mt-2 text-sm text-atlas-texto-suave">
            {teams.pendientes === 0 ? 'No hay avisos en cola.' : `${teams.pendientes} ${teams.pendientes === 1 ? 'aviso en cola' : 'avisos en cola'}.`}
          </p>
        ) : (
          <Apagado motivo={teams.motivo} />
        )}
      </Tarjeta>
    </>
  );
}

function Activo({ activo }: { activo: boolean }) {
  return <Insignia tono={activo ? 'exito' : 'neutral'}>{activo ? 'Activo' : 'Apagado'}</Insignia>;
}

function Apagado({ motivo }: { motivo: string | null }) {
  return (
    <p role="note" className="mt-2 rounded-xl border border-dashed border-border p-3 text-sm wrap-anywhere text-atlas-texto-suave">
      Apagado en este ambiente: {motivo ?? 'falta la configuración.'} El calendario de Atlas y los feeds .ics siguen funcionando.
    </p>
  );
}

function Dato({ titulo, valor }: { titulo: string; valor: string }) {
  return (
    <div className="min-w-0 rounded-xl border border-border p-2">
      <dt className="text-xs text-atlas-texto-suave">{titulo}</dt>
      <dd className="font-medium break-words">{valor}</dd>
    </div>
  );
}

function Feeds({ alAvisar }: { alAvisar: (a: Aviso) => void }) {
  const cliente = useQueryClient();
  const feeds = useQuery({ queryKey: [...CLAVE, 'feeds'], queryFn: listarFeeds });
  const equipos = useEquipos();
  const [equipo, setEquipo] = useState('');
  const [creado, setCreado] = useState<FeedIcsCreado | null>(null);
  const [enviando, setEnviando] = useState(false);
  const activos = (equipos.data ?? []).filter((e) => e.activo);

  async function crear(tipo: TipoFeed, equipoId: string | null) {
    setEnviando(true);
    alAvisar(null);
    try {
      setCreado(await crearFeed(tipo, equipoId));
      await cliente.invalidateQueries({ queryKey: [...CLAVE, 'feeds'] });
    } catch (e) {
      alAvisar(avisoDeError(e, 'No se pudo crear el feed.'));
    } finally {
      setEnviando(false);
    }
  }

  async function revocar(feed: FeedIcs) {
    setEnviando(true);
    alAvisar(null);
    try {
      await revocarFeed(feed.id);
      if (creado?.id === feed.id) setCreado(null);
      await cliente.invalidateQueries({ queryKey: [...CLAVE, 'feeds'] });
      alAvisar({ tono: 'exito', texto: 'Feed revocado: su dirección ya no funciona.' });
    } catch (e) {
      alAvisar(avisoDeError(e, 'No se pudo revocar el feed.'));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Tarjeta titulo="Suscribirte desde tu calendario (.ics)" icono="calendario">
      <p className="text-sm">
        Una dirección para agregar los turnos a Outlook, Google Calendar o el calendario de tu teléfono («Suscribirse desde internet»). Trae los turnos y
        los nombres, nunca correos ni teléfonos. Si la compartes por error, regénerala.
      </p>

      {creado && <FeedNuevo feed={creado} alCerrar={() => setCreado(null)} />}

      {feeds.isPending && <p role="status" className="mt-3">Cargando tus feeds…</p>}
      {feeds.isError && <p role="alert" className="mt-3">No se pudieron cargar tus feeds.</p>}
      {feeds.data && feeds.data.length > 0 && (
        <ul aria-label="Mis feeds" className="mt-3 flex min-w-0 flex-col gap-2">
          {feeds.data.map((f) => (
            <li key={f.id} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <p className="font-medium break-words">{f.tipo === 'personal' ? 'Mis turnos' : `Equipo ${f.equipo?.nombre ?? ''}`}</p>
                <p className="text-xs text-atlas-texto-suave">
                  Creado el {fechaHora.format(new Date(f.creadoEn))} · {f.ultimoUsoEn ? `leído el ${fechaHora.format(new Date(f.ultimoUsoEn))}` : 'sin leer aún'}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Boton variante="secundario" disabled={enviando} onClick={() => void crear(f.tipo, f.equipo?.id ?? null)}>
                  Regenerar
                </Boton>
                <Boton variante="fantasma" disabled={enviando} onClick={() => void revocar(f)}>
                  Revocar
                </Boton>
              </div>
            </li>
          ))}
        </ul>
      )}

      <div className="mt-4 flex min-w-0 flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-end">
        <Boton disabled={enviando} onClick={() => void crear('personal', null)}>
          Feed de mis turnos
        </Boton>
        <div className="flex min-w-0 flex-col gap-2 sm:flex-row sm:items-end">
          <label className="min-w-0 text-sm font-medium">
            Equipo
            <select value={equipo} onChange={(e) => setEquipo(e.target.value)} className={claseCampo}>
              <option value="">Elige un equipo</option>
              {activos.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.nombre}
                </option>
              ))}
            </select>
          </label>
          <Boton variante="secundario" disabled={enviando || !equipo} onClick={() => void crear('equipo', equipo)}>
            Feed del equipo
          </Boton>
        </div>
      </div>
    </Tarjeta>
  );
}

function FeedNuevo({ feed, alCerrar }: { feed: FeedIcsCreado; alCerrar: () => void }) {
  const [copiado, setCopiado] = useState(false);

  async function copiar() {
    try {
      await navigator.clipboard.writeText(feed.url);
      setCopiado(true);
    } catch {
      setCopiado(false);
    }
  }

  return (
    <div role="status" className="mt-3 flex min-w-0 flex-col gap-2 rounded-xl border border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] p-3 text-sm">
      <p className="font-medium">
        {feed.tipo === 'personal' ? 'Feed de tus turnos listo.' : `Feed del equipo ${feed.equipo?.nombre ?? ''} listo.`} Copia la dirección ahora: no se vuelve a
        mostrar.
      </p>
      <label className="min-w-0 text-xs font-medium">
        Dirección del feed
        <input readOnly value={feed.url} onFocus={(e) => e.target.select()} className={`${claseCampo} font-mono text-xs`} />
      </label>
      <div className="flex flex-wrap gap-2">
        <Boton onClick={() => void copiar()}>
          <Copy className="size-4" aria-hidden="true" />
          {copiado ? 'Copiada' : 'Copiar'}
        </Boton>
        <a
          href={feed.urlWebcal}
          className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo"
        >
          <ExternalLink className="size-4" aria-hidden="true" />
          Abrir en la app de calendario
        </a>
        <Boton variante="fantasma" onClick={alCerrar}>
          Listo
        </Boton>
      </div>
    </div>
  );
}
