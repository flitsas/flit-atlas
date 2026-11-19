import { Check, Copy, Search } from 'lucide-react';
import { type KeyboardEvent, type ReactNode, useEffect, useId, useRef, useState } from 'react';

import { cn } from '../../../app/ui/Tarjeta';

/**
 * Piezas de la pantalla del contrato (HU 3.6 y 3.9) sobre los componentes del shell (`src/app/ui`): etiquetas con los
 * tonos de la marca, pestañas accesibles, buscador, filtros en forma de chips, tabla que en el móvil se vuelve tarjetas
 * (sin scroll horizontal a 360 px) y el botón «Copiar». Todo con clases de Tailwind: la CSP no permite estilos en línea.
 */
export type Tono = 'info' | 'neutral' | 'exito' | 'alerta' | 'peligro';

const TONOS: Record<Tono, string> = {
  info: 'border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)]',
  neutral: 'border-[var(--badge-neutral-border)] bg-[var(--badge-neutral-bg)] text-[var(--badge-neutral-fg)]',
  exito: 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
  // Naranja y rojo de la marca, más oscuros en el tema claro para llegar a 4,5:1 sobre el fondo tintado de Atlas.
  alerta: 'border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] text-[#8a3c00] dark:text-[var(--badge-warning-fg)]',
  peligro: 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[#9a2d07] dark:text-[var(--badge-danger-fg)]',
};

export function Etiqueta({ children, tono = 'neutral', className }: { children: ReactNode; tono?: Tono; className?: string }) {
  return <span className={cn('inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium', TONOS[tono], className)}>{children}</span>;
}

const TONO_METODO: Record<string, Tono> = { GET: 'info', POST: 'exito', PUT: 'alerta', PATCH: 'alerta', DELETE: 'peligro' };

/** Método HTTP (o acción de AsyncAPI) con color y texto: el color nunca va solo. */
export function Metodo({ metodo }: { metodo: string }) {
  return (
    <Etiqueta tono={TONO_METODO[metodo.toUpperCase()] ?? 'neutral'} className="min-w-14 justify-center font-mono font-semibold uppercase">
      {metodo}
    </Etiqueta>
  );
}

export interface Pestana {
  id: string;
  titulo: string;
  /** Contador o marca pequeña junto al título. */
  marca?: ReactNode;
}

/** Pestañas con el patrón de ARIA (flechas, Inicio y Fin). En el móvil se acomodan en varias líneas. */
export function Pestanas({
  etiqueta,
  pestanas,
  activa,
  alElegir,
  children,
  compactas = false,
}: {
  etiqueta: string;
  pestanas: Pestana[];
  activa: string;
  alElegir: (id: string) => void;
  children: ReactNode;
  compactas?: boolean;
}) {
  const id = useId();
  const botones = useRef(new Map<string, HTMLButtonElement>());

  function alPresionar(evento: KeyboardEvent<HTMLDivElement>) {
    const indice = pestanas.findIndex((p) => p.id === activa);
    const mover: Record<string, number> = { ArrowRight: indice + 1, ArrowLeft: indice - 1, Home: 0, End: pestanas.length - 1 };
    if (!(evento.key in mover)) return;
    evento.preventDefault();
    const siguiente = pestanas[(mover[evento.key]! + pestanas.length) % pestanas.length]!;
    alElegir(siguiente.id);
    botones.current.get(siguiente.id)?.focus();
  }

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <div role="tablist" aria-label={etiqueta} onKeyDown={alPresionar} className="flex flex-wrap gap-2">
        {pestanas.map((p) => (
          <button
            key={p.id}
            ref={(el) => {
              if (el) botones.current.set(p.id, el);
              else botones.current.delete(p.id);
            }}
            type="button"
            role="tab"
            id={`${id}-${p.id}`}
            aria-selected={p.id === activa}
            aria-controls={`${id}-panel`}
            tabIndex={p.id === activa ? 0 : -1}
            onClick={() => alElegir(p.id)}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-xl border text-sm',
              compactas ? 'min-h-10 px-2.5' : 'min-h-toque px-3',
              p.id === activa ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card hover:bg-atlas-activo',
            )}
          >
            {p.titulo}
            {p.marca}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`${id}-panel`} aria-labelledby={`${id}-${activa}`} className="min-w-0">
        {children}
      </div>
    </div>
  );
}

export function Buscador({ etiqueta, valor, alCambiar, placeholder }: { etiqueta: string; valor: string; alCambiar: (v: string) => void; placeholder: string }) {
  return (
    <div className="relative min-w-0">
      <Search className="pointer-events-none absolute top-1/2 left-3 size-4.5 -translate-y-1/2 text-atlas-texto-suave" aria-hidden="true" />
      <input
        type="search"
        aria-label={etiqueta}
        placeholder={placeholder}
        enterKeyHint="search"
        maxLength={200}
        value={valor}
        onChange={(e) => alCambiar(e.target.value)}
        className="min-h-toque w-full rounded-xl border border-border bg-card pr-3 pl-10 text-base outline-none placeholder:text-atlas-texto-suave focus-visible:border-atlas-tinta sm:text-sm"
      />
    </div>
  );
}

/** Filtro de opción única con forma de chips (radios: un `<select>` viola la CSP en WebKit). */
export function Chips<T extends string>({
  leyenda,
  nombre,
  opciones,
  valor,
  alCambiar,
}: {
  leyenda: string;
  nombre: string;
  opciones: Array<{ valor: T; etiqueta: ReactNode }>;
  valor: T;
  alCambiar: (v: T) => void;
}) {
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-xs font-medium text-atlas-texto-suave">{leyenda}</legend>
      <div className="flex flex-wrap gap-1.5">
        {opciones.map((o) => (
          <label
            key={o.valor || 'todos'}
            className={cn(
              'inline-flex min-h-10 cursor-pointer items-center gap-1.5 rounded-lg border px-2.5 text-sm has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-[var(--flit-brand-ink)]',
              o.valor === valor ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
            )}
          >
            <input type="radio" name={nombre} value={o.valor} checked={o.valor === valor} onChange={() => alCambiar(o.valor)} className="sr-only" />
            {o.etiqueta}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

export interface Columna<F> {
  id: string;
  titulo: string;
  celda: (fila: F) => ReactNode;
  /** En la tarjeta del móvil: el título de la tarjeta (va arriba y sin rótulo). */
  principal?: boolean;
  className?: string;
}

/**
 * Tabla en escritorio y lista de tarjetas en el móvil (§11: las tablas se convierten en tarjetas): a 360 px no hay
 * scroll horizontal. `md` es el corte, como el resto del shell.
 */
export function TablaResponsiva<F>({
  titulo,
  columnas,
  filas,
  clave,
  vacio = 'Sin datos.',
}: {
  titulo: string;
  columnas: Columna<F>[];
  filas: F[];
  clave: (fila: F, i: number) => string;
  vacio?: string;
}) {
  if (filas.length === 0) return <p className="text-sm text-atlas-texto-suave">{vacio}</p>;
  const principal = columnas.find((c) => c.principal) ?? columnas[0]!;
  return (
    <>
      <table className="hidden w-full border-collapse text-left text-sm md:table">
        <caption className="sr-only">{titulo}</caption>
        <thead>
          <tr className="border-b border-border text-xs text-atlas-texto-suave">
            {columnas.map((c) => (
              <th key={c.id} scope="col" className={cn('px-2 py-2 font-medium', c.className)}>
                {c.titulo}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {filas.map((f, i) => (
            <tr key={clave(f, i)} className="border-b border-border align-top last:border-0">
              {columnas.map((c) => (
                <td key={c.id} className={cn('px-2 py-2 break-words', c.className)}>
                  {c.celda(f)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <ul aria-label={titulo} className="flex flex-col gap-2 md:hidden">
        {filas.map((f, i) => (
          <li key={clave(f, i)} className="rounded-xl border border-border bg-card p-3 text-sm">
            <div className="font-medium break-words">{principal.celda(f)}</div>
            <dl className="mt-1 flex flex-col gap-1">
              {columnas
                .filter((c) => c !== principal)
                .map((c) => (
                  <div key={c.id} className="flex min-w-0 flex-wrap gap-x-2">
                    <dt className="text-xs text-atlas-texto-suave">{c.titulo}</dt>
                    <dd className="min-w-0 break-words">{c.celda(f)}</dd>
                  </div>
                ))}
            </dl>
          </li>
        ))}
      </ul>
    </>
  );
}

/** «Copiar» con aviso para lectores de pantalla. Sin portapapeles (http, permisos), no se muestra. */
export function BotonCopiar({ texto, etiqueta = 'Copiar' }: { texto: string; etiqueta?: string }) {
  const [copiado, setCopiado] = useState(false);
  useEffect(() => {
    if (!copiado) return;
    const t = setTimeout(() => setCopiado(false), 2000);
    return () => clearTimeout(t);
  }, [copiado]);
  if (typeof navigator === 'undefined' || !navigator.clipboard) return null;
  return (
    <button
      type="button"
      onClick={() => void navigator.clipboard.writeText(texto).then(() => setCopiado(true), () => undefined)}
      className="inline-flex min-h-10 items-center gap-1.5 rounded-lg border border-border bg-card px-2.5 text-xs font-medium hover:bg-atlas-activo"
    >
      {copiado ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}
      <span aria-live="polite">{copiado ? 'Copiado' : etiqueta}</span>
    </button>
  );
}

/** Bloque de código con su título y «Copiar»; envuelve las líneas largas (nada de scroll horizontal en el móvil). */
export function Codigo({ titulo, codigo, acciones }: { titulo?: string; codigo: string; acciones?: ReactNode }) {
  return (
    <figure className="min-w-0 overflow-hidden rounded-xl border border-border bg-atlas-fondo">
      <figcaption className="flex items-center justify-between gap-2 border-b border-border px-3 py-1.5 text-xs text-atlas-texto-suave">
        <span>{titulo}</span>
        <span className="flex items-center gap-2">
          {acciones}
          <BotonCopiar texto={codigo} />
        </span>
      </figcaption>
      <pre className="max-h-96 overflow-y-auto p-3 font-mono text-xs leading-relaxed break-words whitespace-pre-wrap">
        <code>{codigo}</code>
      </pre>
    </figure>
  );
}

/** `true` en pantallas de escritorio (≥ 1024 px); se actualiza si cambia el tamaño. */
export function useEscritorio(): boolean {
  const consulta = '(min-width: 1024px)';
  const [es, setEs] = useState(() => typeof window.matchMedia === 'function' && window.matchMedia(consulta).matches);
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const mq = window.matchMedia(consulta);
    const alCambiar = () => setEs(mq.matches);
    mq.addEventListener('change', alCambiar);
    return () => mq.removeEventListener('change', alCambiar);
  }, []);
  return es;
}
