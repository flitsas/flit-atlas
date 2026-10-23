import { ArrowLeft, ArrowRight, Check, Hourglass } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';

import type { ModuloAtlas } from '../modulos/contrato';
import { Icono } from './Icono';
import { Insignia } from './Tarjeta';

/** Fase del plan en que llega el módulo (01-prompts-por-fase: F2 catálogo y equipo, F3 contratos, …). */
export type Fase = 'F2' | 'F3' | 'F4' | 'F5' | 'F6' | 'F7' | 'F8';

/**
 * Página de un módulo registrado cuya fase aún no llega. La navegación queda completa desde el primer día y la
 * persona sabe qué viene y cuándo, en lugar de encontrar un vacío.
 */
export function PaginaProximamente({
  modulo,
  fase,
  capacidades,
}: {
  modulo: Pick<ModuloAtlas, 'nombre' | 'descripcion' | 'icono'>;
  fase: Fase;
  capacidades: string[];
}) {
  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6">
      <section
        aria-labelledby="titulo-proximamente"
        className="relative overflow-hidden rounded-3xl bg-portada p-6 text-white shadow-tarjeta sm:p-10"
      >
        <span className="grid size-14 place-items-center rounded-2xl bg-degradado-flit shadow-lg">
          <Icono nombre={modulo.icono} className="size-7" />
        </span>
        <p className="mt-6 inline-flex items-center gap-2 rounded-full bg-white/12 px-3 py-1 text-sm font-medium">
          <Hourglass className="size-4" aria-hidden="true" />
          Próximamente · fase {fase}
        </p>
        <h1 id="titulo-proximamente" className="mt-3 text-2xl font-semibold sm:text-3xl">
          {modulo.nombre}
        </h1>
        <p className="mt-2 max-w-xl text-white/85">{modulo.descripcion}</p>
      </section>

      <section aria-labelledby="titulo-capacidades" className="rounded-2xl border border-border bg-card p-5 shadow-tarjeta">
        <h2 id="titulo-capacidades" className="text-base font-semibold">
          Lo que vas a poder hacer
        </h2>
        <ul className="mt-4 grid gap-3 sm:grid-cols-2">
          {capacidades.map((capacidad) => (
            <li key={capacidad} className="flex gap-3 text-sm">
              <span className="mt-0.5 grid size-5 shrink-0 place-items-center rounded-full bg-atlas-activo text-atlas-tinta">
                <Check className="size-3.5" aria-hidden="true" />
              </span>
              {capacidad}
            </li>
          ))}
        </ul>
        <p className="mt-5 text-sm text-atlas-texto-suave">
          Este módulo ya está registrado en Atlas y se activa con su bandera cuando su fase se publique. Te avisaremos en
          el centro de notificaciones.
        </p>
      </section>

      <div className="flex flex-wrap gap-3">
        <Link
          to="/"
          className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border bg-card px-4 text-sm font-medium hover:bg-atlas-activo"
        >
          <ArrowLeft className="size-4" aria-hidden="true" />
          Volver al inicio
        </Link>
        <Link
          to="/buscar"
          className="inline-flex min-h-toque items-center gap-2 rounded-xl px-4 text-sm font-medium text-foreground underline-offset-4 hover:underline"
        >
          Explorar Atlas
          <ArrowRight className="size-4" aria-hidden="true" />
        </Link>
      </div>
    </div>
  );
}

/** Contenido de un widget de Inicio de un módulo que aún no llega. */
export function WidgetProximamente({
  children,
  ruta,
  sobre,
  fase,
  destacado,
}: {
  children: ReactNode;
  ruta: string;
  /** Nombre del módulo, para que cada «Ver más» tenga un nombre accesible distinto. */
  sobre: string;
  fase: Fase;
  destacado?: ReactNode;
}) {
  return (
    <div className="flex h-full flex-col gap-3">
      {destacado}
      <p className="text-sm text-atlas-texto-suave">{children}</p>
      <div className="mt-auto flex items-center justify-between gap-2">
        <Insignia tono="neutral">Fase {fase}</Insignia>
        <Link
          to={ruta}
          aria-label={`Ver más sobre ${sobre}`}
          className="inline-flex min-h-toque items-center gap-1 rounded-lg px-2 text-sm font-medium text-atlas-tinta hover:bg-atlas-activo"
        >
          Ver más
          <ArrowRight className="size-4" aria-hidden="true" />
        </Link>
      </div>
    </div>
  );
}
