import type { ReactNode } from 'react';
import { useId } from 'react';

import { ErrorApi } from '../../app/api';
import { cn } from '../../app/ui/Tarjeta';
import type { AplicativoResumen } from './api';
import { CRITICIDADES, ESTADOS, porVerificar, TIPOS } from './textos';

export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

type Tono = 'neutral' | 'info' | 'exito' | 'alerta' | 'peligro';

const TONOS: Record<Tono, string> = {
  neutral: 'border-[var(--badge-neutral-border)] bg-[var(--badge-neutral-bg)] text-[var(--badge-neutral-fg)]',
  info: 'border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)]',
  exito: 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
  alerta: 'border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)]',
  peligro: 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]',
};

/** Insignia con los tonos que el catálogo necesita (estado, criticidad, «por verificar»). */
export function Marca({ children, tono = 'neutral' }: { children: ReactNode; tono?: Tono }) {
  return (
    <span className={cn('inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium whitespace-nowrap', TONOS[tono])}>
      {children}
    </span>
  );
}

const TONO_ESTADO: Record<AplicativoResumen['estado'], Tono> = { planeado: 'info', activo: 'exito', en_retiro: 'alerta', retirado: 'neutral' };
const TONO_CRITICIDAD: Record<AplicativoResumen['criticidad'], Tono> = { alta: 'peligro', media: 'neutral', baja: 'neutral' };

/** Las insignias de un aplicativo: tipo, estado, criticidad, datos personales y «por verificar». */
export function Insignias({ aplicativo }: { aplicativo: Pick<AplicativoResumen, 'tipo' | 'estado' | 'criticidad' | 'manejaDatosPersonales' | 'descripcion'> }) {
  return (
    <span className="flex flex-wrap gap-1.5">
      <Marca>{TIPOS[aplicativo.tipo]}</Marca>
      <Marca tono={TONO_ESTADO[aplicativo.estado]}>{ESTADOS[aplicativo.estado]}</Marca>
      <Marca tono={TONO_CRITICIDAD[aplicativo.criticidad]}>Criticidad {CRITICIDADES[aplicativo.criticidad].toLowerCase()}</Marca>
      {aplicativo.manejaDatosPersonales && <Marca tono="info">Datos personales</Marca>}
      {porVerificar(aplicativo.descripcion) && <Marca tono="alerta">Por verificar</Marca>}
    </span>
  );
}

/**
 * Opciones de una sola elección como radios con forma de botón (como la matriz de permisos): en WebKit un `<select>`
 * dispara una violación de la CSP (docs/como-crear-un-modulo.md §2.3).
 */
export function Opciones<T extends string>({
  leyenda,
  valor,
  opciones,
  alCambiar,
  className,
  deshabilitado,
}: {
  leyenda: string;
  valor: T | '';
  opciones: { valor: T | ''; etiqueta: string }[];
  alCambiar: (valor: T | '') => void;
  className?: string;
  deshabilitado?: boolean;
}) {
  const nombre = useId();
  return (
    <fieldset className={cn('min-w-0', className)} disabled={deshabilitado}>
      <legend className="mb-1 text-sm font-medium">{leyenda}</legend>
      <div className="flex flex-wrap gap-2">
        {opciones.map((o) => (
          <label
            key={o.valor || 'todos'}
            className={cn(
              'inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm',
              o.valor === valor ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
              deshabilitado && 'cursor-not-allowed opacity-60',
            )}
          >
            <input
              type="radio"
              name={nombre}
              value={o.valor}
              checked={o.valor === valor}
              onChange={() => alCambiar(o.valor)}
              className="size-4 accent-[var(--flit-brand-ink)]"
            />
            {o.etiqueta}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

export type Aviso = { tono: 'exito' | 'error'; texto: string } | null;

export function avisoDeError(error: unknown, porDefecto: string): Aviso {
  if (error instanceof ErrorApi) {
    if (error.estado === 403) return { tono: 'error', texto: error.mensajePara('No tienes permiso para este cambio.') };
    return { tono: 'error', texto: error.mensajePara(porDefecto) };
  }
  return { tono: 'error', texto: porDefecto };
}

export function MostrarAviso({ aviso }: { aviso: Aviso }) {
  if (!aviso) return null;
  return (
    <div
      role={aviso.tono === 'error' ? 'alert' : 'status'}
      className={cn('rounded-xl border p-3 text-sm', aviso.tono === 'error' ? TONOS.peligro : TONOS.exito)}
    >
      {aviso.texto}
    </div>
  );
}

/** Texto con la marca «[por verificar]» resaltada (el resto, tal cual). */
export function TextoConMarca({ texto }: { texto: string }) {
  const partes = texto.split(/(\[por verificar\])/);
  return (
    <>
      {partes.map((parte, i) =>
        parte === '[por verificar]' ? (
          <mark key={i} className="rounded bg-[var(--badge-warning-bg)] px-1 text-[var(--badge-warning-fg)]">
            {parte}
          </mark>
        ) : (
          <span key={i}>{parte}</span>
        ),
      )}
    </>
  );
}
