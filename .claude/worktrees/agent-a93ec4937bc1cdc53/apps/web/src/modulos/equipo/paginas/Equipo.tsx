import { Mail, Phone, RefreshCw } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { Link } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { tienePermiso } from '../../../app/modulos/resolver';
import { useSesion } from '../../../app/sesion';
import { formatearFechaHora, iniciales } from '../../../app/ui/formato';
import { Icono } from '../../../app/ui/Icono';
import { Avatar } from '../../../app/ui/Marca';
import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  BUSQUEDA_MINIMA,
  type EstadoSincronizacion,
  type PersonaDirectorio,
  useDirectorio,
  useSincronizacion,
  useSincronizarAhora,
} from '../api';
import { moduloEquipo } from '../modulo';

const RETRASO_BUSQUEDA_MS = 300;

const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

function useRetrasado(valor: string, ms: number): string {
  const [retrasado, setRetrasado] = useState(valor);
  useEffect(() => {
    const espera = window.setTimeout(() => setRetrasado(valor), ms);
    return () => window.clearTimeout(espera);
  }, [valor, ms]);
  return retrasado;
}

/**
 * Equipo (HU 2.2, ATLAS-ADR-0030): el directorio interno con nombre, cargo, área y foto que llegan de Microsoft 365 cada
 * día; el teléfono solo con `atlas.equipo.ver_contacto`. Quien tiene `atlas.identidad.administrar` ve la última
 * sincronización y puede pedir una sin esperar al día siguiente. Los responsables por producto llegan con la HU 2.3.
 */
export default function Equipo() {
  const { acceso } = useSesion();
  const idAyuda = useId();
  const [texto, setTexto] = useState('');
  const [inactivas, setInactivas] = useState(false);
  const consulta = useRetrasado(texto, RETRASO_BUSQUEDA_MS);
  const directorio = useDirectorio(consulta, inactivas);
  const administra = tienePermiso('atlas.identidad.administrar', acceso.permisos);
  const corta = consulta.trim().length > 0 && consulta.trim().length < BUSQUEDA_MINIMA;

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-4">
      <header className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloEquipo.icono} className="size-6" />
        </span>
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">Equipo</h1>
          <p className="text-sm text-atlas-texto-suave">
            Las personas de FLIT con su cargo, área y foto de Microsoft 365, actualizadas cada día.{' '}
            <Link to="/equipo/responsables" className="font-medium text-atlas-tinta underline-offset-2 hover:underline">
              Ver los responsables de cada producto y módulo
            </Link>
          </p>
        </div>
      </header>

      {administra && <Sincronizacion />}

      <form role="search" aria-label="Buscar en el equipo" className="flex flex-wrap items-end gap-3" onSubmit={(e) => e.preventDefault()}>
        <label className="min-w-0 flex-1 basis-64 text-sm font-medium">
          Nombre, correo, cargo o área
          <input
            type="search"
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            autoComplete="off"
            spellCheck={false}
            enterKeyHint="search"
            maxLength={100}
            aria-describedby={idAyuda}
            className={claseCampo}
          />
          <span id={idAyuda} className="mt-1 block text-xs font-normal text-atlas-texto-suave">
            Al menos {BUSQUEDA_MINIMA} letras; no importan las mayúsculas ni las tildes.
          </span>
        </label>
        <label className="flex min-h-toque items-center gap-2 text-sm">
          <input type="checkbox" checked={inactivas} onChange={(e) => setInactivas(e.target.checked)} className="size-5" />
          Incluir inactivas
        </label>
      </form>

      <div aria-live="polite">
        {corta && <p className="text-sm text-atlas-texto-suave">Escribe al menos {BUSQUEDA_MINIMA} letras.</p>}
        {!corta && directorio.isPending && <p className="text-sm text-atlas-texto-suave">Cargando el equipo…</p>}
        {directorio.isError && (
          <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
            No pudimos cargar el equipo. Revisa tu conexión.
          </p>
        )}
        {!corta && directorio.data?.personas.length === 0 && (
          <p className="rounded-2xl border border-border bg-card px-6 py-10 text-center text-sm text-atlas-texto-suave shadow-tarjeta">
            {consulta.trim() ? `Nadie coincide con «${consulta.trim()}».` : 'Todavía no hay personas sincronizadas.'}
          </p>
        )}
        {!corta && directorio.data && directorio.data.personas.length > 0 && (
          <ul aria-label="Personas del equipo" className="grid gap-3 sm:grid-cols-2">
            {directorio.data.personas.map((p) => (
              <TarjetaPersona key={p.id} persona={p} />
            ))}
          </ul>
        )}
        {!corta && directorio.data?.hayMas && (
          <p className="mt-2 text-xs text-atlas-texto-suave">Hay más personas: escribe más del nombre para afinar.</p>
        )}
      </div>
    </div>
  );
}

const ESTADOS: Record<PersonaDirectorio['estado'], string | null> = { activa: null, inactiva: 'Inactiva', suspendida: 'Suspendida' };

function TarjetaPersona({ persona }: { persona: PersonaDirectorio }) {
  const estado = ESTADOS[persona.estado];
  const detalle = [persona.cargo, persona.area].filter(Boolean).join(' · ');
  return (
    <li className="flex min-w-0 items-start gap-3 rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
      <Avatar iniciales={iniciales(persona.nombre)} foto={persona.foto} className="size-12" />
      <div className="flex min-w-0 flex-col gap-1">
        <p className="font-semibold break-words">{persona.nombre}</p>
        {detalle && <p className="text-sm break-words text-atlas-texto-suave">{detalle}</p>}
        {estado && (
          <span>
            <Insignia tono="neutral">{estado}</Insignia>
          </span>
        )}
        <a href={`mailto:${persona.correo}`} className="inline-flex min-h-toque items-center gap-2 text-sm break-all text-atlas-tinta underline-offset-2 hover:underline">
          <Mail className="size-4 shrink-0" aria-hidden="true" />
          {persona.correo}
        </a>
        {persona.telefono && (
          <a href={`tel:${persona.telefono.replace(/[^\d+]/g, '')}`} className="inline-flex min-h-toque items-center gap-2 text-sm text-atlas-tinta underline-offset-2 hover:underline">
            <Phone className="size-4 shrink-0" aria-hidden="true" />
            {persona.telefono}
          </a>
        )}
      </div>
    </li>
  );
}

function resumen(ultima: EstadoSincronizacion): string {
  const partes = [
    `${ultima.creadas} nuevas`,
    `${ultima.actualizadas} actualizadas`,
    `${ultima.deshabilitadas} deshabilitadas`,
    `${ultima.reactivadas} reactivadas`,
  ];
  return partes.join(' · ');
}

/** Última corrida, próxima y el botón «Sincronizar ahora» (solo con `atlas.identidad.administrar`). */
function Sincronizacion() {
  const [pedida, setPedida] = useState<string | null>(null);
  const estado = useSincronizacion(true, pedida !== null);
  const sincronizar = useSincronizarAhora();
  const ultima = estado.data?.ultima ?? null;
  // La corrida pedida terminó cuando la última cambia de id (o aparece la primera).
  const esperando = pedida !== null && (ultima?.id ?? '') === pedida;
  useEffect(() => {
    if (pedida !== null && (ultima?.id ?? '') !== pedida) setPedida(null);
  }, [pedida, ultima?.id]);

  const error =
    sincronizar.error instanceof ErrorApi
      ? sincronizar.error.mensajePara('No se pudo pedir la sincronización.')
      : sincronizar.error
        ? 'No se pudo pedir la sincronización.'
        : null;

  return (
    <Tarjeta
      titulo="Sincronización con Microsoft 365"
      accion={
        estado.data?.disponible && (
          <Boton
            variante="secundario"
            disabled={esperando || sincronizar.isPending}
            onClick={() =>
              sincronizar.mutate(undefined, { onSuccess: () => setPedida(ultima?.id ?? '') })
            }
          >
            <RefreshCw className={esperando ? 'size-4 animate-spin' : 'size-4'} aria-hidden="true" />
            {esperando ? 'Sincronizando…' : 'Sincronizar ahora'}
          </Boton>
        )
      }
    >
      {estado.data && !estado.data.disponible && (
        <p className="text-sm text-atlas-texto-suave">Este ambiente no tiene Entra ID configurado: no hay directorio que sincronizar.</p>
      )}
      {estado.data?.disponible && (
        <div className="flex flex-col gap-1 text-sm" aria-live="polite">
          {ultima ? (
            <p>
              Última: {formatearFechaHora(new Date(ultima.inicio))} ({ultima.disparo === 'manual' ? 'pedida' : 'programada'}) ·{' '}
              {ultima.resultado === 'exitosa' ? resumen(ultima) : 'falló; se reintenta sola'}
            </p>
          ) : (
            <p>Todavía no ha corrido.</p>
          )}
          {ultima && ultima.retenidas > 0 && (
            <p role="alert">
              El freno de seguridad retuvo {ultima.retenidas} deshabilitaciones: revisa la configuración de Entra antes de la próxima.
            </p>
          )}
          <p className="text-atlas-texto-suave">
            {estado.data.programada && estado.data.proxima
              ? `Próxima: ${formatearFechaHora(new Date(estado.data.proxima))} (cada día a las ${estado.data.hora}, hora de Bogotá).`
              : 'El trabajo diario está apagado en este ambiente.'}
          </p>
        </div>
      )}
      {error && (
        <p role="alert" className="mt-2 text-sm">
          {error}
        </p>
      )}
    </Tarjeta>
  );
}
