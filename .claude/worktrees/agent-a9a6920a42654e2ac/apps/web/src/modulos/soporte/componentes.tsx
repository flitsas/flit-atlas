import { CloudOff, Mail, Phone, TriangleAlert } from 'lucide-react';
import { type ReactNode, useState } from 'react';
import { Link } from 'react-router';

import { ErrorApi } from '../../app/api';
import { iniciales } from '../../app/ui/formato';
import { Boton, cn, Insignia } from '../../app/ui/Tarjeta';
import { type AccionEncolada, descartar, enviarPendientes } from '../../pwa/cola';
import { useEnLinea } from '../../pwa/conexion';
import { type EstadoTurno, horario, TEXTO_ESTADO, TEXTO_ORIGEN, type Turno } from './api';
import { formatearDiaLargo } from './fechas';

/** Piezas de interfaz del módulo de soporte (un módulo no importa a otro: estas son propias, ATLAS-ADR-0026). */
export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

export type Aviso = { tono: 'exito' | 'error'; texto: string } | null;

export function avisoDeError(error: unknown, porDefecto: string): Aviso {
  if (error instanceof ErrorApi) {
    if (error.estado === 403) return { tono: 'error', texto: error.mensajePara('No tienes permiso para esta acción.') };
    return { tono: 'error', texto: error.mensajePara(porDefecto) };
  }
  return { tono: 'error', texto: porDefecto };
}

export function MostrarAviso({ aviso }: { aviso: Aviso }) {
  if (!aviso) return null;
  return (
    <div
      role={aviso.tono === 'error' ? 'alert' : 'status'}
      className={cn(
        'rounded-xl border p-3 text-sm',
        aviso.tono === 'error'
          ? 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]'
          : 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
      )}
    >
      {aviso.texto}
    </div>
  );
}

/** Aviso para quien gestiona el soporte (p. ej. sábados sin personas suficientes en la vista previa). */
export function AvisoPendiente({ children, enlace }: { children: ReactNode; enlace?: { ruta: string; texto: string } }) {
  return (
    <div
      role="note"
      className="flex items-start gap-2 rounded-xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] p-3 text-sm text-foreground"
    >
      <TriangleAlert className="mt-0.5 size-4 shrink-0 text-[var(--badge-warning-fg)]" aria-hidden="true" />
      <p className="min-w-0">
        {children}{' '}
        {enlace && (
          <Link to={enlace.ruta} className="font-semibold underline">
            {enlace.texto}
          </Link>
        )}
      </p>
    </div>
  );
}

export function InsigniaEstado({ estado }: { estado: EstadoTurno }) {
  const tono = estado === 'aplicado' || estado === 'confirmado' ? 'exito' : estado === 'programado' ? 'info' : 'neutral';
  return <Insignia tono={tono}>{TEXTO_ESTADO[estado]}</Insignia>;
}

export function Avatar({ nombre, apagado = false }: { nombre: string; apagado?: boolean }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'grid size-10 shrink-0 place-items-center rounded-full text-sm font-semibold',
        apagado ? 'bg-atlas-activo text-atlas-texto-suave' : 'bg-degradado-flit text-[#0b1220]',
      )}
    >
      {iniciales(nombre)}
    </span>
  );
}

/** Tarjeta de un turno: persona, horario, estado, festivo y contacto (el teléfono solo llega con permiso). */
export function TarjetaTurno({ turno, acciones, mostrarFecha = true }: { turno: Turno; acciones?: ReactNode; mostrarFecha?: boolean }) {
  const vigente = turno.estado !== 'reemplazado' && turno.estado !== 'cancelado';
  const textoHorario = horario(turno);
  return (
    <article
      aria-label={`Turno del ${formatearDiaLargo(turno.fecha)}: ${turno.persona.nombre}`}
      className={cn('flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta', !vigente && 'opacity-75')}
    >
      {mostrarFecha && (
        <header className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-semibold first-letter:uppercase">{formatearDiaLargo(turno.fecha)}</h3>
          <div className="flex flex-wrap gap-1.5">
            {turno.festivo && <Insignia tono="neutral">Festivo: {turno.festivo}</Insignia>}
            <InsigniaEstado estado={turno.estado} />
          </div>
        </header>
      )}
      <div className="flex min-w-0 items-start gap-3">
        <Avatar nombre={turno.persona.nombre} apagado={!vigente} />
        <div className="min-w-0 flex-1">
          <p className={cn('font-medium break-words', !vigente && 'line-through')}>{turno.persona.nombre}</p>
          <p className="text-xs font-medium text-atlas-tinta">{turno.equipo.nombre}</p>
          {turno.persona.cargo && <p className="text-xs text-atlas-texto-suave">{turno.persona.cargo}</p>}
          <p className="mt-1 text-sm">{textoHorario ?? <span className="text-atlas-texto-suave">Horario por definir</span>}</p>
          {!mostrarFecha && (
            <div className="mt-1 flex flex-wrap gap-1.5">
              <InsigniaEstado estado={turno.estado} />
            </div>
          )}
          <p className="mt-1 text-xs text-atlas-texto-suave">
            Origen: {TEXTO_ORIGEN[turno.origen]}
            {turno.turnoReemplazadoId && ' · en reemplazo'}
            {turno.requiereRevision && ' · por revisar'}
          </p>
          {turno.observacion && <p className="mt-1 text-xs break-words whitespace-pre-line text-atlas-texto-suave">{turno.observacion}</p>}
        </div>
      </div>
      {vigente && (turno.persona.correo || turno.persona.telefono) && (
        <div className="flex flex-wrap gap-2">
          {turno.persona.correo && (
            <a
              href={`mailto:${turno.persona.correo}`}
              className="inline-flex min-h-toque max-w-full items-center gap-2 rounded-xl border border-border px-3 text-sm hover:bg-atlas-activo"
            >
              <Mail className="size-4 shrink-0" aria-hidden="true" />
              <span className="truncate">{turno.persona.correo}</span>
            </a>
          )}
          {turno.persona.telefono && (
            <a
              href={`tel:${turno.persona.telefono}`}
              className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border px-3 text-sm hover:bg-atlas-activo"
            >
              <Phone className="size-4 shrink-0" aria-hidden="true" />
              {turno.persona.telefono}
            </a>
          )}
        </div>
      )}
      {acciones}
    </article>
  );
}

/** Filtro por equipo con «Todos»: botones de opción que se envuelven (sin scroll horizontal a 360 px). */
export function FiltroEquipo({
  equipos,
  valor,
  alCambiar,
  conTodos = true,
}: {
  equipos: { id: string; nombre: string }[];
  valor: string | null;
  alCambiar: (equipo: string | null) => void;
  conTodos?: boolean;
}) {
  const opciones = [...(conTodos ? [{ id: null as string | null, nombre: 'Todos' }] : []), ...equipos];
  return (
    <fieldset className="flex min-w-0 flex-wrap gap-2" aria-label="Equipo">
      {opciones.map((o) => (
        <label
          key={o.id ?? 'todos'}
          className={cn(
            'flex min-h-toque cursor-pointer items-center rounded-full border px-4 text-sm font-medium has-[:focus-visible]:ring-2',
            valor === o.id ? 'border-transparent bg-flit-primary text-white dark:bg-flit-brand dark:text-[#0b1220]' : 'border-border bg-card hover:bg-atlas-activo',
          )}
        >
          <input type="radio" name="equipo" checked={valor === o.id} onChange={() => alCambiar(o.id)} className="sr-only" />
          {o.nombre}
        </label>
      ))}
    </fieldset>
  );
}

/** Grupo de opciones (radios) en lugar de `<select>`: en WebKit un `<select>` choca con la CSP (docs/como-crear-un-modulo.md). */
export function Opciones<T extends string>({
  leyenda,
  nombre,
  valor,
  opciones,
  alCambiar,
}: {
  leyenda: string;
  nombre: string;
  valor: T | null;
  opciones: { valor: T; texto: string; ayuda?: string }[];
  alCambiar: (valor: T) => void;
}) {
  return (
    <fieldset className="flex min-w-0 flex-col gap-1">
      <legend className="mb-1 text-sm font-medium">{leyenda}</legend>
      {opciones.map((o) => (
        <label key={o.valor} className="flex min-h-toque cursor-pointer items-start gap-3 rounded-xl px-1 py-2 text-sm hover:bg-atlas-activo">
          <input
            type="radio"
            name={nombre}
            value={o.valor}
            checked={valor === o.valor}
            onChange={() => alCambiar(o.valor)}
            className="mt-0.5 size-5 shrink-0"
          />
          <span className="min-w-0">
            <span className="block font-medium">{o.texto}</span>
            {o.ayuda && <span className="text-atlas-texto-suave">{o.ayuda}</span>}
          </span>
        </label>
      ))}
    </fieldset>
  );
}

/**
 * Acciones de soporte hechas sin conexión y pendientes de enviar (ATLAS-ADR-0032): se envían solas al reconectar y se
 * pueden reintentar a mano (donde no hay Background Sync). Las que el servidor rechazó muestran su motivo y se descartan.
 */
export function PendientesDeEnviar({ acciones }: { acciones: readonly AccionEncolada[] }) {
  const enLinea = useEnLinea();
  const [enviando, setEnviando] = useState(false);
  if (acciones.length === 0) return null;
  return (
    <section
      aria-label="Pendientes de enviar"
      className="flex min-w-0 flex-col gap-2 rounded-xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] p-3 text-sm text-foreground"
    >
      <p className="flex items-center gap-2 font-semibold">
        <CloudOff className="size-4 shrink-0" aria-hidden="true" />
        {acciones.length === 1 ? '1 acción pendiente de enviar' : `${acciones.length} acciones pendientes de enviar`}
      </p>
      <ul className="flex min-w-0 flex-col gap-1">
        {acciones.map((a) => (
          <li key={a.llave} className="flex min-w-0 flex-wrap items-center justify-between gap-2">
            <span className="min-w-0 break-words">
              {a.descripcion}
              {a.error ? <span className="block text-xs">No se pudo: {a.error}</span> : <span className="block text-xs">Pendiente de enviar</span>}
            </span>
            {a.error && (
              <Boton variante="secundario" onClick={() => void descartar(a.llave)}>
                Descartar
              </Boton>
            )}
          </li>
        ))}
      </ul>
      {acciones.some((a) => !a.error) && (
        <Boton
          variante="secundario"
          className="self-start"
          disabled={!enLinea || enviando}
          onClick={() => {
            setEnviando(true);
            void enviarPendientes().finally(() => setEnviando(false));
          }}
        >
          {enLinea ? 'Reintentar ahora' : 'Sin conexión: se envía al reconectar'}
        </Boton>
      )}
    </section>
  );
}
