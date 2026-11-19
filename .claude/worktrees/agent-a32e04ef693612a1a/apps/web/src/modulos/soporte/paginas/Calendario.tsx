import { useQueryClient } from '@tanstack/react-query';
import { ArrowLeftRight, BarChart3, CalendarPlus, ChevronLeft, ChevronRight, Plus, Settings2 } from 'lucide-react';
import { type FormEvent, type ReactNode, useState } from 'react';
import { Link } from 'react-router';

import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { DatosGuardados } from '../../../app/ui/DatosGuardados';
import { Hoja } from '../../../app/ui/Hoja';
import { Icono } from '../../../app/ui/Icono';
import { Boton, cn, Insignia } from '../../../app/ui/Tarjeta';
import {
  type Calendario as DatosCalendario,
  cambiarEstadoTurno,
  cambiarTurno,
  CLAVE,
  crearTurno,
  type Equipo,
  reemplazarTurno,
  registrarCompensacion,
  textoCupo,
  type Turno,
  useCalendario,
  useEquipos,
  useParticipantes,
} from '../api';
import { solicitarCambio, textoResultado, usePendientesSoporte } from '../acciones';
import { type Aviso, avisoDeError, claseCampo, FiltroEquipo, MostrarAviso, Opciones, PendientesDeEnviar, TarjetaTurno } from '../componentes';
import {
  cuadriculaDelMes,
  DIAS_CORTOS,
  type Dia,
  diaSemana,
  formatearDia,
  formatearDiaLargo,
  formatearMes,
  hoy,
  lunesDe,
  primeroDelMes,
  sabadoDesde,
  sumarDias,
  sumarMeses,
} from '../fechas';
import { moduloSoporte } from '../modulo';
import { RECURSO_TURNOS, tresMeses } from '../recursos';

type Vista = 'lista' | 'semana' | 'mes';

/** En el móvil la vista por defecto es la lista (prompt maestro §8.3); en escritorio, el mes. */
function vistaInicial(): Vista {
  const movil = typeof window.matchMedia === 'function' && window.matchMedia('(max-width: 767px)').matches;
  return movil ? 'lista' : 'mes';
}

function rango(vista: Vista, ancla: Dia): { desde: Dia; hasta: Dia; titulo: string } {
  if (vista === 'semana') {
    const desde = lunesDe(ancla);
    return { desde, hasta: sumarDias(desde, 6), titulo: `Semana del ${formatearDia(desde)}` };
  }
  if (vista === 'mes') {
    const dias = cuadriculaDelMes(ancla);
    return { desde: dias[0]!, hasta: dias[dias.length - 1]!, titulo: formatearMes(ancla) };
  }
  return { desde: ancla, hasta: sumarDias(sumarMeses(ancla, 3), -1), titulo: `Desde el ${formatearDia(ancla)}, tres meses` };
}

function mover(vista: Vista, ancla: Dia, paso: number): Dia {
  if (vista === 'semana') return sumarDias(ancla, 7 * paso);
  if (vista === 'mes') return sumarMeses(primeroDelMes(ancla), paso);
  return sumarMeses(ancla, 3 * paso);
}

/**
 * Calendario del soporte de los sábados: lista, semana y mes, de todos los equipos o de uno, con las acciones de quien
 * gestiona el equipo sobre cada turno.
 */
export default function Calendario() {
  const { acceso, yo } = useSesion();
  const equipos = useEquipos();
  const gestionables = (equipos.data ?? []).filter((e) => e.puedeGestionar);
  const gestiona = tienePermiso('atlas.soporte.gestionar', acceso.permisos);
  const solicita = tienePermiso('atlas.soporte.solicitar_cambio', acceso.permisos);
  const pendientes = usePendientesSoporte();
  const [pidiendo, setPidiendo] = useState<Turno | null>(null);
  const [vista, setVista] = useState<Vista>(vistaInicial);
  const [ancla, setAncla] = useState<Dia>(() => hoy());
  const [equipo, setEquipo] = useState<string | null>(null);
  const [editando, setEditando] = useState<Turno | null>(null);
  const [creando, setCreando] = useState(false);
  const [aviso, setAviso] = useState<Aviso>(null);
  const { desde, hasta, titulo } = rango(vista, ancla);
  const calendario = useCalendario(desde, hasta, equipo);
  const puedeGestionarTurno = (t: Turno) => gestionables.some((e) => e.id === t.equipo.id);
  const enCola = new Set(pendientes.map((p) => p.recurso));
  /** Acciones de cada turno: la solicitud de cambio de la persona de turno y la gestión de quien gestiona el equipo. */
  const acciones = (t: Turno): ReactNode => {
    const abierto = t.estado === 'programado' || t.estado === 'confirmado';
    const pedir = solicita && abierto && yo?.personaId === t.persona.id && t.fecha >= hoy() && !enCola.has(t.id);
    const gestionar = puedeGestionarTurno(t) && abierto;
    if (!pedir && !gestionar && !enCola.has(t.id)) return null;
    return (
      <div className="flex flex-wrap items-center gap-2">
        {enCola.has(t.id) && <Insignia tono="neutral">Cambio pendiente de enviar</Insignia>}
        {pedir && (
          <Boton variante="secundario" onClick={() => setPidiendo(t)} aria-label={`Solicitar el cambio del turno del ${formatearDia(t.fecha)}`}>
            <ArrowLeftRight className="size-4" aria-hidden="true" />
            Solicitar cambio
          </Boton>
        )}
        {gestionar && (
          <Boton variante="secundario" onClick={() => setEditando(t)} aria-label={`Gestionar el turno del ${formatearDia(t.fecha)}`}>
            Gestionar turno
          </Boton>
        )}
      </div>
    );
  };

  function cambiarVista(nueva: Vista) {
    setVista(nueva);
    setAncla(nueva === 'lista' ? hoy() : nueva === 'mes' ? primeroDelMes(ancla) : ancla);
  }

  return (
    <div className="mx-auto flex max-w-5xl min-w-0 flex-col gap-4">
      <header className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloSoporte.icono} className="size-6" />
        </span>
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">Soporte de los sábados</h1>
          <p className="text-sm text-atlas-texto-suave">Quién atiende cada sábado, su horario y cómo contactarlo.</p>
        </div>
      </header>

      <div className="flex flex-wrap items-center gap-2">
        <Link to="/soporte/indicadores" className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo">
          <BarChart3 className="size-4" aria-hidden="true" />
          Indicadores
        </Link>
        <Link to="/soporte/cambios" className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo">
          <ArrowLeftRight className="size-4" aria-hidden="true" />
          Cambios y compensaciones
        </Link>
        <Link to="/soporte/microsoft-365" className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo">
          <CalendarPlus className="size-4" aria-hidden="true" />
          Outlook y calendarios
        </Link>
        {gestiona && (
          <>
            <Link to="/soporte/gestion" className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo">
              <Settings2 className="size-4" aria-hidden="true" />
              Gestionar
            </Link>
            {gestionables.length > 0 && (
              <Boton onClick={() => setCreando(true)}>
                <Plus className="size-4" aria-hidden="true" />
                Nuevo turno
              </Boton>
            )}
          </>
        )}
      </div>

      <DatosGuardados recurso={RECURSO_TURNOS.clave} />
      <ProximoTurno personaId={yo.personaId} />
      <MostrarAviso aviso={aviso} />
      <PendientesDeEnviar acciones={pendientes} />
      {equipos.data && equipos.data.length > 1 && <FiltroEquipo equipos={equipos.data} valor={equipo} alCambiar={setEquipo} />}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <fieldset className="flex rounded-xl border border-border bg-card p-1" aria-label="Vista del calendario">
          {(['lista', 'semana', 'mes'] as const).map((v) => (
            <label
              key={v}
              className={cn(
                'flex min-h-toque flex-1 cursor-pointer items-center justify-center rounded-lg px-4 text-sm font-medium has-[:focus-visible]:ring-2',
                vista === v ? 'bg-flit-primary text-white dark:bg-flit-brand dark:text-[#0b1220]' : 'hover:bg-atlas-activo',
              )}
            >
              <input type="radio" name="vista" value={v} checked={vista === v} onChange={() => cambiarVista(v)} className="sr-only" />
              {v === 'lista' ? 'Lista' : v === 'semana' ? 'Semana' : 'Mes'}
            </label>
          ))}
        </fieldset>
        <div className="flex items-center justify-between gap-2">
          <Boton variante="secundario" aria-label="Anterior" onClick={() => setAncla(mover(vista, ancla, -1))}>
            <ChevronLeft className="size-4" aria-hidden="true" />
          </Boton>
          <p className="min-w-0 text-center text-sm font-semibold first-letter:uppercase" aria-live="polite">
            {titulo}
          </p>
          <Boton variante="secundario" aria-label="Siguiente" onClick={() => setAncla(mover(vista, ancla, 1))}>
            <ChevronRight className="size-4" aria-hidden="true" />
          </Boton>
        </div>
      </div>

      {calendario.isPending && <p role="status">Cargando el calendario…</p>}
      {calendario.isError && (
        <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
          No pudimos cargar el calendario. Revisa tu conexión.
        </p>
      )}
      {calendario.data && vista === 'lista' && <VistaLista datos={calendario.data} acciones={acciones} />}
      {calendario.data && vista === 'semana' && <VistaSemana datos={calendario.data} acciones={acciones} />}
      {calendario.data && vista === 'mes' && <VistaMes datos={calendario.data} mes={ancla} acciones={acciones} />}

      {solicita && <SolicitarCambio turno={pidiendo} alCerrar={() => setPidiendo(null)} alTerminar={setAviso} />}
      {gestiona && (
        <>
          <GestionTurno
            turno={editando}
            aprueba={(equipos.data ?? []).some((e) => e.id === editando?.equipo.id && e.puedeAprobar)}
            alCerrar={() => setEditando(null)}
            alTerminar={setAviso}
          />
          {creando && <NuevoTurno abierta equipos={gestionables} alCerrar={() => setCreando(false)} alTerminar={setAviso} />}
        </>
      )}
    </div>
  );
}

/**
 * «Tu próximo turno» (HU 2.12): el primero vigente de la persona en los tres meses que también se guardan sin conexión
 * (la misma consulta que la vista en lista). Si no tiene turnos en esos meses, no se muestra.
 */
function ProximoTurno({ personaId }: { personaId: string }) {
  const { desde, hasta } = tresMeses();
  const calendario = useCalendario(desde, hasta, null);
  const proximo = calendario.data?.turnos.find(
    (t) => t.persona.id === personaId && t.fecha >= desde && (t.estado === 'programado' || t.estado === 'confirmado'),
  );
  if (!proximo) return null;
  return (
    <section aria-label="Tu próximo turno" className="flex min-w-0 flex-col gap-2">
      <h2 className="text-base font-semibold">Tu próximo turno</h2>
      <TarjetaTurno turno={proximo} />
    </section>
  );
}

interface PropsVista {
  datos: DatosCalendario;
  acciones: (turno: Turno) => ReactNode;
}

function VistaLista({ datos, acciones }: PropsVista) {
  if (datos.turnos.length === 0) {
    return (
      <p className="rounded-2xl border border-border bg-card px-6 py-10 text-center text-sm text-atlas-texto-suave shadow-tarjeta">
        No hay turnos en estas fechas.
      </p>
    );
  }
  return (
    <ul aria-label="Turnos" className="grid grid-cols-1 gap-3 md:grid-cols-2">
      {datos.turnos.map((t) => (
        <li key={t.id} className="min-w-0">
          <TarjetaTurno turno={t} acciones={acciones(t)} />
        </li>
      ))}
    </ul>
  );
}

function VistaSemana({ datos, acciones }: PropsVista) {
  const dias = Array.from({ length: 7 }, (_, i) => sumarDias(datos.desde, i));
  const festivos = new Map(datos.festivos.map((f) => [f.fecha, f.nombre]));
  return (
    <ol aria-label="Días de la semana" className="flex flex-col gap-2">
      {dias.map((dia) => {
        const turnos = datos.turnos.filter((t) => t.fecha === dia);
        const sabado = diaSemana(dia) === 6;
        return (
          <li key={dia} className={cn('flex min-w-0 flex-col gap-2 rounded-2xl border border-border p-3', sabado ? 'bg-card shadow-tarjeta' : 'bg-transparent')}>
            <p className="text-sm font-semibold first-letter:uppercase">
              {formatearDiaLargo(dia)}
              {festivos.has(dia) && <span className="ml-2 font-normal text-atlas-texto-suave">· {festivos.get(dia)}</span>}
            </p>
            {sabado && turnos.length === 0 && <p className="text-sm text-atlas-texto-suave">Sin turno.</p>}
            {turnos.map((t) => (
              <TarjetaTurno key={t.id} turno={t} mostrarFecha={false} acciones={acciones(t)} />
            ))}
          </li>
        );
      })}
    </ol>
  );
}

function VistaMes({ datos, mes, acciones }: PropsVista & { mes: Dia }) {
  const dias = cuadriculaDelMes(mes);
  const delMes = mes.slice(0, 7);
  const festivos = new Map(datos.festivos.map((f) => [f.fecha, f.nombre]));
  const vigentes = datos.turnos.filter((t) => t.estado !== 'reemplazado' && t.estado !== 'cancelado');
  const hoyDia = hoy();
  const turnosDelMes = datos.turnos.filter((t) => t.fecha.startsWith(delMes));
  return (
    <div className="flex min-w-0 flex-col gap-4">
      <div role="grid" aria-label={`Calendario de ${formatearMes(mes)}`} className="grid min-w-0 grid-cols-7 gap-1 rounded-2xl border border-border bg-card p-2 shadow-tarjeta">
        <div role="row" className="contents">
          {DIAS_CORTOS.map((d) => (
            <span key={d} role="columnheader" className="py-1 text-center text-xs font-semibold text-atlas-texto-suave">
              {d}
            </span>
          ))}
        </div>
        {Array.from({ length: dias.length / 7 }, (_, semana) => (
          <div role="row" key={semana} className="contents">
            {dias.slice(semana * 7, semana * 7 + 7).map((dia) => {
              const delDia = vigentes.filter((t) => t.fecha === dia);
              const turno = delDia[0];
              const fuera = !dia.startsWith(delMes);
              const festivo = festivos.get(dia);
              return (
                <div
                  key={dia}
                  role="gridcell"
                  aria-label={`${formatearDiaLargo(dia)}${festivo ? `, festivo: ${festivo}` : ''}${delDia.length > 0 ? `, de turno: ${delDia.map((t) => `${t.persona.nombre} (${t.equipo.nombre})`).join(', ')}` : ''}`}
                  className={cn(
                    'flex min-h-14 min-w-0 flex-col items-center gap-1 overflow-hidden rounded-lg p-1 text-xs',
                    fuera && 'text-atlas-texto-suave',
                    diaSemana(dia) === 6 && 'bg-atlas-activo',
                    dia === hoyDia && 'ring-2 ring-atlas-tinta',
                  )}
                >
                  <span className={cn('font-medium', festivo && 'text-[var(--badge-danger-fg)]')}>{Number(dia.slice(8, 10))}</span>
                  {turno && (
                    <span className="max-w-full truncate rounded-full bg-degradado-flit px-1.5 text-[0.65rem] font-semibold text-[#0b1220]">
                      {turno.persona.nombre.split(/\s+/)[0]}
                    </span>
                  )}
                  {delDia.length > 1 && <span className="text-[0.65rem] font-semibold">+{delDia.length - 1}</span>}
                </div>
              );
            })}
          </div>
        ))}
      </div>
      {turnosDelMes.length > 0 ? (
        <ul aria-label={`Turnos de ${formatearMes(mes)}`} className="grid grid-cols-1 gap-3 md:grid-cols-2">
          {turnosDelMes.map((t) => (
            <li key={t.id} className="min-w-0">
              <TarjetaTurno turno={t} acciones={acciones(t)} />
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-center text-sm text-atlas-texto-suave">No hay turnos este mes.</p>
      )}
    </div>
  );
}

/** Acciones de quien gestiona el equipo sobre un turno: estado, horario propio y reemplazo. */
function GestionTurno({
  turno,
  aprueba,
  alCerrar,
  alTerminar,
}: {
  turno: Turno | null;
  aprueba: boolean;
  alCerrar: () => void;
  alTerminar: (aviso: Aviso) => void;
}) {
  return (
    <Hoja abierta={turno !== null} alCerrar={alCerrar} titulo={turno ? `Turno del ${formatearDia(turno.fecha)}` : 'Turno'}>
      {turno && <CamposGestion key={turno.id} turno={turno} aprueba={aprueba} alCerrar={alCerrar} alTerminar={alTerminar} />}
    </Hoja>
  );
}

function CamposGestion({
  turno,
  aprueba,
  alCerrar,
  alTerminar,
}: {
  turno: Turno;
  aprueba: boolean;
  alCerrar: () => void;
  alTerminar: (aviso: Aviso) => void;
}) {
  const cliente = useQueryClient();
  const participantes = useParticipantes(turno.equipo.id);
  const [inicio, setInicio] = useState(turno.horaInicio ?? '');
  const [fin, setFin] = useState(turno.horaFin ?? '');
  const [observacion, setObservacion] = useState(turno.observacion ?? '');
  const [reemplazo, setReemplazo] = useState<string | null>(null);
  const [compensado, setCompensado] = useState('');
  const [error, setError] = useState<Aviso>(null);
  const [enviando, setEnviando] = useState(false);
  const pasado = turno.fecha <= hoy();

  async function hacer(trabajo: () => Promise<unknown>, exito: string, porDefecto: string) {
    setEnviando(true);
    setError(null);
    try {
      await trabajo();
      await cliente.invalidateQueries({ queryKey: CLAVE });
      alTerminar({ tono: 'exito', texto: exito });
      alCerrar();
    } catch (e) {
      setError(avisoDeError(e, porDefecto));
    } finally {
      setEnviando(false);
    }
  }

  function guardarHorario(evento: FormEvent) {
    evento.preventDefault();
    void hacer(
      () => cambiarTurno(turno.id, { horaInicio: inicio || null, horaFin: fin || null, observacion: observacion.trim() || null }),
      'Se guardó el horario del turno.',
      'No se pudo guardar el horario.',
    );
  }

  return (
    <div className="flex min-w-0 flex-col gap-5 px-4 pt-2 pb-5 text-sm">
      <p>
        {turno.persona.nombre} · {turno.equipo.nombre} · {formatearDiaLargo(turno.fecha)}
      </p>
      <MostrarAviso aviso={error} />
      <section aria-label="Estado del turno" className="flex flex-wrap gap-2">
        {turno.estado === 'programado' && (
          <Boton disabled={enviando} onClick={() => void hacer(() => cambiarEstadoTurno(turno.id, 'confirmado'), 'Turno confirmado.', 'No se pudo confirmar.')}>
            Confirmar
          </Boton>
        )}
        {turno.estado === 'confirmado' && (
          <Boton
            disabled={enviando || !pasado}
            title={pasado ? undefined : 'Se aplica el día del turno o después'}
            onClick={() => void hacer(() => cambiarEstadoTurno(turno.id, 'aplicado'), 'Turno aplicado.', 'No se pudo aplicar.')}
          >
            Marcar aplicado
          </Boton>
        )}
        <Boton
          variante="secundario"
          disabled={enviando}
          onClick={() => void hacer(() => cambiarEstadoTurno(turno.id, 'cancelado'), 'Turno cancelado.', 'No se pudo cancelar.')}
        >
          Cancelar turno
        </Boton>
      </section>

      <form aria-label="Horario del turno" onSubmit={guardarHorario} className="flex min-w-0 flex-col gap-3">
        <div className="grid grid-cols-2 gap-3">
          <label className="min-w-0 font-medium">
            Inicio
            <input type="time" value={inicio} onChange={(e) => setInicio(e.target.value)} className={claseCampo} />
          </label>
          <label className="min-w-0 font-medium">
            Fin
            <input type="time" value={fin} onChange={(e) => setFin(e.target.value)} className={claseCampo} />
          </label>
        </div>
        <label className="font-medium">
          Observación
          <textarea value={observacion} maxLength={500} rows={2} onChange={(e) => setObservacion(e.target.value)} className={cn(claseCampo, 'py-2')} />
        </label>
        <Boton type="submit" variante="secundario" disabled={enviando} className="self-start">
          Guardar horario
        </Boton>
      </form>

      <form
        aria-label="Reemplazar a la persona"
        onSubmit={(e) => {
          e.preventDefault();
          if (reemplazo) void hacer(() => reemplazarTurno(turno.id, reemplazo), 'Se registró el reemplazo.', 'No se pudo reemplazar.');
        }}
        className="flex min-w-0 flex-col gap-3"
      >
        <Opciones
          leyenda="Reemplazar por"
          nombre={`reemplazo-${turno.id}`}
          valor={reemplazo}
          opciones={(participantes.data ?? []).filter((p) => p.personaId !== turno.persona.id).map((p) => ({ valor: p.personaId, texto: p.nombre }))}
          alCambiar={setReemplazo}
        />
        <Boton type="submit" variante="secundario" disabled={enviando || !reemplazo} className="self-start">
          Reemplazar
        </Boton>
      </form>

      {aprueba && turno.estado !== 'reemplazado' && turno.estado !== 'cancelado' && (
        <form
          aria-label="Compensación"
          onSubmit={(e) => {
            e.preventDefault();
            void hacer(
              () => registrarCompensacion(turno.id, compensado || null, null),
              `Se registró la compensación de ${turno.persona.nombre}.`,
              'No se pudo registrar la compensación.',
            );
          }}
          className="flex min-w-0 flex-col gap-3"
        >
          <label className="font-medium">
            Día compensado (opcional)
            <input type="date" value={compensado} onChange={(e) => setCompensado(e.target.value)} className={claseCampo} />
          </label>
          <Boton type="submit" variante="secundario" disabled={enviando} className="self-start">
            Registrar compensación
          </Boton>
        </form>
      )}
    </div>
  );
}

/** La persona de turno pide el cambio y propone un reemplazo de su equipo; sin conexión, queda en la cola. */
function SolicitarCambio({ turno, alCerrar, alTerminar }: { turno: Turno | null; alCerrar: () => void; alTerminar: (aviso: Aviso) => void }) {
  return (
    <Hoja abierta={turno !== null} alCerrar={alCerrar} titulo={turno ? `Cambiar el turno del ${formatearDia(turno.fecha)}` : 'Cambio de turno'}>
      {turno && <CamposSolicitud key={turno.id} turno={turno} alCerrar={alCerrar} alTerminar={alTerminar} />}
    </Hoja>
  );
}

function CamposSolicitud({ turno, alCerrar, alTerminar }: { turno: Turno; alCerrar: () => void; alTerminar: (aviso: Aviso) => void }) {
  const cliente = useQueryClient();
  const participantes = useParticipantes(turno.equipo.id);
  const [reemplazo, setReemplazo] = useState<string | null>(null);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<Aviso>(null);
  const [enviando, setEnviando] = useState(false);
  const opciones = (participantes.data ?? []).filter((p) => p.activo && p.personaId !== turno.persona.id);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (!reemplazo) return;
    setEnviando(true);
    setError(null);
    try {
      const resultado = await solicitarCambio(turno, reemplazo, motivo.trim() || null);
      if (resultado.estado === 'enviada') await cliente.invalidateQueries({ queryKey: CLAVE });
      alTerminar({ tono: 'exito', texto: textoResultado(resultado, 'Se envió la solicitud: tu reemplazo debe aceptarla y luego se aprueba.') });
      alCerrar();
    } catch (e) {
      setError(avisoDeError(e, 'No se pudo solicitar el cambio.'));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <form aria-label="Solicitud de cambio" onSubmit={(e) => void enviar(e)} className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 text-sm">
      <p>
        {turno.equipo.nombre} · {formatearDiaLargo(turno.fecha)}
      </p>
      <MostrarAviso aviso={error} />
      {participantes.isPending && <p role="status">Cargando el equipo…</p>}
      <Opciones
        leyenda="¿Quién te reemplaza?"
        nombre={`reemplazo-solicitud-${turno.id}`}
        valor={reemplazo}
        opciones={opciones.map((p) => ({ valor: p.personaId, texto: p.nombre }))}
        alCambiar={setReemplazo}
      />
      {participantes.data && opciones.length === 0 && <p className="text-atlas-texto-suave">No hay otras personas activas en la rotación del equipo.</p>}
      <label className="font-medium">
        Motivo (opcional)
        <textarea value={motivo} maxLength={500} rows={2} onChange={(e) => setMotivo(e.target.value)} className={cn(claseCampo, 'py-2')} />
      </label>
      <Boton type="submit" className="min-h-12" disabled={!reemplazo || enviando}>
        Solicitar el cambio
      </Boton>
    </form>
  );
}

function NuevoTurno({
  abierta,
  equipos,
  alCerrar,
  alTerminar,
}: {
  abierta: boolean;
  equipos: Equipo[];
  alCerrar: () => void;
  alTerminar: (aviso: Aviso) => void;
}) {
  const cliente = useQueryClient();
  const [equipo, setEquipo] = useState<string | null>(equipos.length === 1 ? equipos[0]!.id : null);
  const participantes = useParticipantes(abierta ? equipo : null);
  const [fecha, setFecha] = useState<Dia>(() => sabadoDesde(sumarDias(hoy(), 1)));
  const [persona, setPersona] = useState<string | null>(null);
  const [error, setError] = useState<Aviso>(null);
  const esSabado = /^\d{4}-\d{2}-\d{2}$/.test(fecha) && diaSemana(fecha) === 6;
  const elegido = equipos.find((e) => e.id === equipo);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    if (!persona || !esSabado || !equipo) return;
    setError(null);
    try {
      await crearTurno({ equipoId: equipo, fecha, personaId: persona });
      await cliente.invalidateQueries({ queryKey: CLAVE });
      alTerminar({ tono: 'exito', texto: `Se creó el turno del ${formatearDia(fecha)}.` });
      alCerrar();
    } catch (e) {
      setError(avisoDeError(e, 'No se pudo crear el turno.'));
    }
  }

  return (
    <Hoja abierta={abierta} alCerrar={alCerrar} titulo="Nuevo turno">
      <form aria-label="Nuevo turno" onSubmit={(e) => void enviar(e)} className="flex min-w-0 flex-col gap-4 px-4 pt-2 pb-5 text-sm">
        <MostrarAviso aviso={error} />
        <Opciones
          leyenda="Equipo"
          nombre="equipo-nuevo-turno"
          valor={equipo}
          opciones={equipos.map((e) => ({ valor: e.id, texto: e.nombre, ayuda: `${textoCupo(e)} · ${e.horarioEfectivo}` }))}
          alCambiar={(id) => {
            setEquipo(id);
            setPersona(null);
          }}
        />
        <label className="font-medium">
          Sábado
          <input type="date" required value={fecha} onChange={(e) => setFecha(e.target.value)} className={claseCampo} aria-describedby="ayuda-sabado" />
        </label>
        <p id="ayuda-sabado" className="-mt-2 text-xs text-atlas-texto-suave">
          {!esSabado ? 'Elige un sábado.' : elegido ? `Toma el horario del equipo: ${elegido.horarioEfectivo}.` : 'Elige el equipo.'}
        </p>
        <Opciones
          leyenda="Persona de turno"
          nombre="persona-nuevo-turno"
          valor={persona}
          opciones={(participantes.data ?? []).filter((p) => p.activo).map((p) => ({ valor: p.personaId, texto: p.nombre }))}
          alCambiar={setPersona}
        />
        {participantes.data?.length === 0 && <p className="text-atlas-texto-suave">Agrega participantes al equipo en Gestionar.</p>}
        <Boton type="submit" className="min-h-12" disabled={!persona || !esSabado || !equipo}>
          Crear turno
        </Boton>
      </form>
    </Hoja>
  );
}
