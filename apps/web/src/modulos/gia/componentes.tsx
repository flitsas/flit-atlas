import { useId, useState } from 'react';
import { NavLink } from 'react-router';

import { ErrorApi } from '../../app/api';
import { cn } from '../../app/ui/Tarjeta';
import { detalleError, usePersonas, type PersonaGia } from './api';

export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

export const claseBoton = 'inline-flex min-h-toque items-center justify-center rounded-xl bg-atlas-tinta px-4 font-medium text-white disabled:opacity-60';

export const claseBotonSuave = 'inline-flex min-h-toque items-center justify-center rounded-xl border border-border bg-card px-4 font-medium disabled:opacity-60';

export function Cargando({ etiqueta, alto = 'h-24' }: { etiqueta: string; alto?: string }) {
  return <div role="status" aria-label={etiqueta} className={`animate-pulse rounded-xl bg-atlas-activo ${alto}`} />;
}

export function Aviso({ tono, children }: { tono: 'error' | 'exito' | 'info'; children: React.ReactNode }) {
  const clases = {
    // El texto no usa --badge-danger-fg: sobre este fondo da contraste 4,08, por debajo de AA.
    error: 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-foreground',
    exito: 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
    info: 'border-border bg-atlas-activo text-foreground',
  };
  return (
    <div role={tono === 'error' ? 'alert' : 'status'} className={cn('rounded-xl border p-3 text-sm', clases[tono])}>
      {children}
    </div>
  );
}

/** El error de una escritura con su mensaje y, si los hay, los campos o requisitos que faltan. */
export function ErrorEnvio({ error, porDefecto }: { error: unknown; porDefecto: string }) {
  if (!error) return null;
  const mensaje = error instanceof ErrorApi ? error.mensajePara(porDefecto) : porDefecto;
  const detalle = detalleError(error);
  return (
    <Aviso tono="error">
      <p>{mensaje}</p>
      {detalle.length > 0 && (
        <ul className="mt-1 list-disc pl-5">
          {detalle.map((d) => (
            <li key={d}>{d}</li>
          ))}
        </ul>
      )}
    </Aviso>
  );
}

const SECCIONES = [
  { ruta: '/gobierno-ia', texto: 'Tablero', fin: true },
  { ruta: '/gobierno-ia/casos', texto: 'Casos' },
  { ruta: '/gobierno-ia/politicas', texto: 'Políticas' },
  { ruta: '/gobierno-ia/herramientas', texto: 'Herramientas' },
  { ruta: '/gobierno-ia/incidentes', texto: 'Incidentes' },
  { ruta: '/gobierno-ia/roles', texto: 'Roles' },
];

/** Encabezado común: título y navegación entre las secciones del módulo. */
export function Pagina({ titulo, descripcion, accion, children }: { titulo: string; descripcion?: string; accion?: React.ReactNode; children: React.ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="flex min-w-0 flex-col gap-4">
      <nav aria-label="Secciones de gobierno de IA" className="flex flex-wrap gap-2">
        {SECCIONES.map((s) => (
          <NavLink
            key={s.ruta}
            to={s.ruta}
            end={s.fin}
            className={({ isActive }) => cn('inline-flex min-h-toque items-center rounded-xl border px-3 text-sm', isActive ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card')}
          >
            {s.texto}
          </NavLink>
        ))}
      </nav>
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <h1 id={id} className="text-2xl font-semibold">
            {titulo}
          </h1>
          {descripcion && <p className="text-sm text-atlas-texto-suave">{descripcion}</p>}
        </div>
        {accion}
      </header>
      {children}
    </section>
  );
}

/** Radios y no `<select>`: en WebKit, crear un `<select>` reporta una violación de `style-src-attr` (docs/como-crear-un-modulo.md). */
export function Opciones({ titulo, valor, opciones, alCambiar }: { titulo: string; valor: string | null; opciones: { valor: string; texto: string }[]; alCambiar: (v: string) => void }) {
  const id = useId();
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-sm font-medium">{titulo}</legend>
      <div className="flex flex-wrap gap-2">
        {opciones.map((o) => (
          <label key={o.valor} className={cn('inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm', valor === o.valor ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card')}>
            <input type="radio" name={id} value={o.valor} checked={valor === o.valor} onChange={() => alCambiar(o.valor)} className="size-4 accent-[var(--flit-brand-ink)]" />
            {o.texto}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/** Varias opciones a la vez, con casillas. */
export function Multiples({ titulo, valores, opciones, alCambiar }: { titulo: string; valores: string[]; opciones: { valor: string; texto: string }[]; alCambiar: (v: string[]) => void }) {
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-sm font-medium">{titulo}</legend>
      <div className="flex flex-wrap gap-2">
        {opciones.map((o) => {
          const activa = valores.includes(o.valor);
          return (
            <label key={o.valor} className={cn('inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm', activa ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card')}>
              <input
                type="checkbox"
                checked={activa}
                onChange={() => alCambiar(activa ? valores.filter((v) => v !== o.valor) : [...valores, o.valor])}
                className="size-4 accent-[var(--flit-brand-ink)]"
              />
              {o.texto}
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}

/** Buscador de empleados activos. Solo pide nombres; nunca datos de contacto. */
export function ElegirPersona({ titulo, elegida, alElegir }: { titulo: string; elegida: PersonaGia | null; alElegir: (p: PersonaGia | null) => void }) {
  const [texto, setTexto] = useState('');
  const termino = texto.trim();
  const personas = usePersonas(termino, termino.length >= 2);
  return (
    <div className="min-w-0">
      <label className="text-sm font-medium">
        {titulo}
        <input type="search" value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Escribe al menos 2 letras" className={claseCampo} />
      </label>
      {elegida && (
        <p className="mt-1 flex flex-wrap items-center gap-2 text-sm">
          Elegida: <strong>{elegida.nombre}</strong>
          <button type="button" onClick={() => alElegir(null)} className="min-h-toque underline">
            Quitar
          </button>
        </p>
      )}
      {termino.length >= 2 && (
        <ul aria-label={`Resultados de ${titulo}`} className="mt-1 flex flex-col gap-1">
          {personas.data?.map((p) => (
            <li key={p.id}>
              <button type="button" onClick={() => { alElegir(p); setTexto(''); }} className="min-h-toque w-full rounded-xl border border-border bg-card px-3 text-left text-sm">
                {p.nombre}
              </button>
            </li>
          ))}
          {personas.data?.length === 0 && <li className="text-sm text-atlas-texto-suave">No hay empleados con ese nombre.</li>}
        </ul>
      )}
    </div>
  );
}

/** Convierte texto separado por comas en una lista, sin vacíos. */
export const lista = (texto: string): string[] => texto.split(',').map((t) => t.trim()).filter(Boolean);

export const vacioANulo = (texto: string): string | null => (texto.trim() === '' ? null : texto.trim());
