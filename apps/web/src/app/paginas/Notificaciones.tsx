import { BellOff, CheckCheck, Settings } from 'lucide-react';
import { Link, useNavigate } from 'react-router';

import type { Notificacion } from '../api';
import { useBandeja, useMarcarLeidas } from '../notificaciones';
import { formatearRelativo } from '../ui/formato';
import { Boton, cn } from '../ui/Tarjeta';

/** Centro de notificaciones (§8.9): todo lo que llega por push también queda aquí. */
export default function Notificaciones() {
  const bandeja = useBandeja();
  const { una, todas } = useMarcarLeidas();
  const navegar = useNavigate();
  const elementos = bandeja.data?.elementos ?? [];

  function abrir(notificacion: Notificacion) {
    if (!notificacion.leidaEn) una.mutate(notificacion.id);
    if (notificacion.enlace?.startsWith('/') && !notificacion.enlace.startsWith('//')) void navegar(notificacion.enlace);
  }

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Notificaciones</h1>
          <p className="text-sm text-atlas-texto-suave">
            {bandeja.data ? `${bandeja.data.noLeidas} sin leer` : 'Avisos de Atlas y de tus módulos'}
          </p>
        </div>
        <div className="flex gap-2">
          <Link
            to="/ajustes#notificaciones-push"
            className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-3 text-sm font-medium hover:bg-atlas-activo"
          >
            <Settings className="size-4" aria-hidden="true" />
            Preferencias
          </Link>
          <Boton
            variante="secundario"
            disabled={!bandeja.data || bandeja.data.noLeidas === 0 || todas.isPending}
            onClick={() => todas.mutate()}
          >
            <CheckCheck className="size-4" aria-hidden="true" />
            Marcar todas como leídas
          </Boton>
        </div>
      </div>

      {bandeja.isError && (
        <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
          No pudimos cargar tus notificaciones. Revisa tu conexión.
        </p>
      )}

      {bandeja.data && elementos.length === 0 && (
        <div className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-card px-6 py-12 text-center shadow-tarjeta">
          <BellOff className="size-8 text-atlas-texto-suave" aria-hidden="true" />
          <p className="font-medium">Estás al día</p>
          <p className="max-w-sm text-sm text-atlas-texto-suave">
            Aquí verás los recordatorios de soporte, los cambios en contratos y los avisos de Atlas.
          </p>
        </div>
      )}

      {elementos.length > 0 && (
        <ul className="flex flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-tarjeta">
          {elementos.map((n) => (
            <li key={n.id} className="border-b border-border last:border-b-0">
              <button
                type="button"
                onClick={() => abrir(n)}
                className="flex min-h-toque w-full items-start gap-3 px-4 py-3 text-left hover:bg-atlas-activo"
              >
                <span
                  className={cn('mt-1.5 size-2.5 shrink-0 rounded-full', n.leidaEn ? 'bg-transparent' : 'bg-atlas-tinta')}
                  aria-hidden="true"
                />
                <span className="min-w-0 flex-1">
                  <span className={cn('block text-sm', !n.leidaEn && 'font-semibold')}>
                    {n.titulo}
                    {!n.leidaEn && <span className="sr-only"> (sin leer)</span>}
                  </span>
                  <span className="mt-0.5 block text-sm text-atlas-texto-suave">{n.cuerpo}</span>
                </span>
                <time dateTime={n.creadaEn} className="shrink-0 text-xs text-atlas-texto-suave">
                  {formatearRelativo(new Date(n.creadaEn))}
                </time>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
