import { useId } from 'react';
import { Link } from 'react-router';

import { cn, Insignia } from '../../app/ui/Tarjeta';
import { formatearRelativo } from '../../app/ui/formato';
import { type DiagramaResumen, TEXTO_TIPO, TEXTO_VISIBILIDAD } from './api';

export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

export function Cargando({ etiqueta, alto = 'h-24' }: { etiqueta: string; alto?: string }) {
  return <div role="status" aria-label={etiqueta} className={`animate-pulse rounded-xl bg-atlas-activo ${alto}`} />;
}

export function Aviso({ tono, children }: { tono: 'error' | 'exito'; children: React.ReactNode }) {
  return (
    <div
      role={tono === 'error' ? 'alert' : 'status'}
      className={
        tono === 'error'
          ? 'rounded-xl border border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] p-3 text-sm text-[var(--badge-danger-fg)]'
          : 'rounded-xl border border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] p-3 text-sm text-[var(--badge-success-fg)]'
      }
    >
      {children}
    </div>
  );
}

const claseOpcion = (activa: boolean) =>
  cn('inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm', activa ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card');

/** Radios y no `<select>`: en WebKit, crear un `<select>` reporta una violación de `style-src-attr` (docs/como-crear-un-modulo.md). */
export function Opciones<T extends string>({
  titulo,
  valor,
  opciones,
  textos,
  alCambiar,
}: {
  titulo: string;
  valor: T | null;
  opciones: readonly T[];
  textos: Record<T, string>;
  alCambiar: (v: T) => void;
}) {
  const id = useId();
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-sm font-medium">{titulo}</legend>
      <div className="flex max-h-56 flex-wrap gap-2 overflow-y-auto">
        {opciones.map((o) => (
          <label key={o} className={claseOpcion(valor === o)}>
            <input type="radio" name={id} value={o} checked={valor === o} onChange={() => alCambiar(o)} className="size-4 accent-[var(--flit-brand-ink)]" />
            {textos[o]}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

export function TarjetaDiagrama({ diagrama: d }: { diagrama: DiagramaResumen }) {
  return (
    <Link to={`/diagramas/${d.id}`} className="flex min-h-toque min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3 hover:bg-atlas-activo">
      <span className="font-medium break-words">{d.titulo}</span>
      <span className="text-xs text-atlas-texto-suave">
        {[TEXTO_TIPO[d.tipo] ?? d.tipo, `versión ${d.versionActual}`, formatearRelativo(new Date(d.actualizadoEn))].join(' · ')}
      </span>
      <span className="flex flex-wrap gap-1">
        <Insignia tono="neutral">{TEXTO_VISIBILIDAD[d.visibilidad] ?? d.visibilidad}</Insignia>
      </span>
    </Link>
  );
}
