import { ArrowRight, Bell, BellRing } from 'lucide-react';
import { Suspense, useEffect, useState } from 'react';
import { Link } from 'react-router';

import { estadoPush, type EstadoPush } from '../../pwa/push';
import { diferido } from '../modulos/diferidos';
import type { WidgetResuelto } from '../modulos/resolver';
import { useBandeja } from '../notificaciones';
import { useSesion } from '../sesion';
import { formatearDia, formatearRelativo, nombreRol, primerNombre } from '../ui/formato';
import { Icono } from '../ui/Icono';
import { cn, Insignia, Tarjeta } from '../ui/Tarjeta';

/**
 * Inicio por rol (§8.9): saludo, accesos a los módulos visibles y los widgets que cada módulo aporta, filtrados por
 * bandera y por permiso. Cada widget se descarga aparte.
 */
export default function Inicio() {
  const { yo, navegacion } = useSesion();

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <section aria-labelledby="saludo" className="relative overflow-hidden rounded-3xl bg-portada p-6 text-white shadow-tarjeta sm:p-8">
        <p className="text-sm text-white/80 first-letter:uppercase">{formatearDia(new Date())}</p>
        <h1 id="saludo" className="mt-1 text-2xl font-semibold sm:text-3xl">
          Hola, {primerNombre(yo.nombre)}
        </h1>
        <p className="mt-2 max-w-2xl text-white/85">
          El conocimiento de FLIT en un solo lugar: aplicativos, responsables, soporte, contratos y documentación.
        </p>
        <ul aria-label="Tus roles" className="mt-4 flex flex-wrap gap-2">
          {(yo.roles.length > 0 ? yo.roles.map(nombreRol) : ['Sin roles todavía']).map((rol) => (
            <li key={rol} className="rounded-full bg-white/14 px-3 py-1 text-xs font-medium">
              {rol}
            </li>
          ))}
        </ul>
      </section>

      {navegacion.modulos.length > 0 && (
        <section aria-labelledby="accesos">
          <h2 id="accesos" className="mb-3 text-base font-semibold">
            Accesos rápidos
          </h2>
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
            {navegacion.modulos.map((modulo) => (
              <li key={modulo.id}>
                <Link
                  to={modulo.rutas[0]?.path ?? '/'}
                  className="flex h-full min-h-28 flex-col gap-2 rounded-2xl border border-border bg-card p-4 shadow-tarjeta transition-colors hover:border-atlas-tinta"
                >
                  <span className="grid size-10 place-items-center rounded-xl bg-degradado-flit text-[#0b1220]">
                    <Icono nombre={modulo.icono} className="size-5" />
                  </span>
                  <span className="text-sm leading-tight font-semibold">{modulo.navegacion[0]?.etiqueta ?? modulo.nombre}</span>
                  {modulo.estado === 'proximamente' && <span className="mt-auto text-xs text-atlas-texto-suave">Próximamente</span>}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <NotificacionesRecientes />
        <ActivarNotificaciones />
        {navegacion.widgets.map((widget) => (
          <WidgetDiferido key={widget.id} widget={widget} />
        ))}
      </div>
    </div>
  );
}

function WidgetDiferido({ widget }: { widget: WidgetResuelto }) {
  const Contenido = diferido(widget.cargar);
  return (
    <Tarjeta titulo={widget.titulo} icono={widget.modulo?.icono} className={cn(widget.ancho === 'doble' && 'md:col-span-2')}>
      <Suspense fallback={<div className="h-20 animate-pulse rounded-xl bg-atlas-activo" />}>
        <Contenido />
      </Suspense>
    </Tarjeta>
  );
}

function NotificacionesRecientes() {
  const bandeja = useBandeja();
  const recientes = bandeja.data?.elementos.slice(0, 3) ?? [];
  return (
    <Tarjeta
      titulo="Notificaciones"
      icono="notificaciones"
      accion={
        bandeja.data && bandeja.data.noLeidas > 0 ? <Insignia>{bandeja.data.noLeidas} sin leer</Insignia> : undefined
      }
    >
      {recientes.length === 0 ? (
        <p className="flex items-center gap-2 text-sm text-atlas-texto-suave">
          <Bell className="size-4" aria-hidden="true" />
          {bandeja.isPending ? 'Cargando…' : 'Estás al día. No tienes notificaciones.'}
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {recientes.map((n) => (
            <li key={n.id} className="flex items-start gap-2 text-sm">
              <span
                aria-hidden="true"
                className={cn('mt-1.5 size-2 shrink-0 rounded-full', n.leidaEn ? 'bg-border' : 'bg-atlas-tinta')}
              />
              <span className="min-w-0 flex-1">
                <span className="block truncate font-medium">{n.titulo}</span>
                <span className="block text-xs text-atlas-texto-suave">{formatearRelativo(new Date(n.creadaEn))}</span>
              </span>
            </li>
          ))}
        </ul>
      )}
      <Link
        to="/notificaciones"
        className="mt-3 inline-flex min-h-toque items-center gap-1 rounded-lg px-2 text-sm font-medium text-atlas-tinta hover:bg-atlas-activo"
      >
        Ver todas
        <ArrowRight className="size-4" aria-hidden="true" />
      </Link>
    </Tarjeta>
  );
}

function ActivarNotificaciones() {
  const [estado, setEstado] = useState<EstadoPush | null>(null);
  useEffect(() => {
    void estadoPush().then(setEstado, () => setEstado('no_soportado'));
  }, []);
  if (estado === null || estado === 'activo' || estado === 'no_soportado') return null;

  const textos: Partial<Record<EstadoPush, string>> = {
    inactivo: 'Recibe en este dispositivo los recordatorios de soporte y los avisos de Atlas.',
    ios_sin_instalar: 'En iPhone y iPad, instala Atlas en tu pantalla de inicio para recibir notificaciones.',
    bloqueado: 'Bloqueaste las notificaciones de Atlas en este navegador. Puedes habilitarlas en sus ajustes.',
  };
  return (
    <Tarjeta titulo="Activa las notificaciones" icono="notificaciones">
      <p className="text-sm text-atlas-texto-suave">{textos[estado] ?? textos.inactivo}</p>
      <Link
        to="/ajustes#notificaciones-push"
        className="mt-3 inline-flex min-h-toque items-center gap-2 rounded-xl bg-flit-primary px-4 text-sm font-medium text-white dark:bg-flit-brand dark:text-[#0b1220]"
      >
        <BellRing className="size-4" aria-hidden="true" />
        Configurar
      </Link>
    </Tarjeta>
  );
}
