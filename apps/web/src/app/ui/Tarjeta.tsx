import type { ReactNode } from 'react';

import type { NombreIcono } from '../modulos/contrato';
import { Icono } from './Icono';

export const cn = (...clases: Array<string | false | null | undefined>): string => clases.filter(Boolean).join(' ');

/** Superficie base del shell: tarjeta blanca (o del tema oscuro) con borde de marca y sombra suave. */
export function Tarjeta({
  titulo,
  icono,
  accion,
  children,
  className,
  etiqueta,
}: {
  titulo?: string;
  icono?: NombreIcono;
  accion?: ReactNode;
  children: ReactNode;
  className?: string;
  /** Nombre accesible de la región cuando no hay título visible. */
  etiqueta?: string;
}) {
  return (
    <section
      aria-label={titulo ? undefined : etiqueta}
      aria-labelledby={titulo ? idTitulo(titulo) : undefined}
      className={cn('rounded-2xl border border-border bg-card p-4 text-card-foreground shadow-tarjeta sm:p-5', className)}
    >
      {titulo && (
        <header className="mb-3 flex items-center justify-between gap-3">
          <h2 id={idTitulo(titulo)} className="flex items-center gap-2 text-base font-semibold">
            {icono && (
              <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-atlas-activo text-atlas-tinta">
                <Icono nombre={icono} className="size-4.5" />
              </span>
            )}
            {titulo}
          </h2>
          {accion}
        </header>
      )}
      {children}
    </section>
  );
}

const idTitulo = (titulo: string) =>
  `t-${titulo
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')}`;

export function Insignia({ children, tono = 'info' }: { children: ReactNode; tono?: 'info' | 'neutral' | 'exito' }) {
  const tonos = {
    info: 'border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)]',
    neutral: 'border-[var(--badge-neutral-border)] bg-[var(--badge-neutral-bg)] text-[var(--badge-neutral-fg)]',
    exito: 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
  };
  return (
    <span className={cn('inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium', tonos[tono])}>
      {children}
    </span>
  );
}

/** Botón con objetivo táctil de 44 px (§11). */
export function Boton({
  variante = 'primario',
  className,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { variante?: 'primario' | 'secundario' | 'fantasma' }) {
  const variantes = {
    primario: 'bg-flit-primary text-white hover:bg-[#1f3a6b] dark:bg-flit-brand dark:text-[#0b1220] dark:hover:bg-[#6f93ff]',
    secundario: 'border border-border bg-card text-foreground hover:bg-atlas-activo',
    fantasma: 'text-foreground hover:bg-atlas-activo',
  };
  return (
    <button
      type="button"
      className={cn(
        'inline-flex min-h-toque items-center justify-center gap-2 rounded-xl px-4 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60',
        variantes[variante],
        className,
      )}
      {...props}
    />
  );
}
