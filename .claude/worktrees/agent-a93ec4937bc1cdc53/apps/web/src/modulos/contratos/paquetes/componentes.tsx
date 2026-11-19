import type { ReactNode } from 'react';

import { ErrorApi } from '../../../app/api';
import { cn } from '../../../app/ui/Tarjeta';
import { Etiqueta } from '../componentes/ui';
import type { Aviso } from '../guias/componentes';
import { type PaqueteResumen, TEXTO_ESTADO } from './api';

/** Piezas de la pantalla de paquetes (HU 4.1): casillas y opciones de 44 px, avisos de error y las insignias del paquete. */
const MENSAJES: Record<string, string> = {
  revision_desactualizada: 'Alguien guardó otra revisión mientras trabajabas: recarga el paquete y vuelve a intentarlo.',
  sin_cambios: 'No hay cambios frente a la última revisión.',
  ya_publicada: 'Esa revisión ya es la vigente.',
  ya_vigente: 'Esa revisión ya es la vigente.',
  es_la_ultima: 'Es la última revisión: publícala en vez de fijarla.',
  archivado: 'El paquete está archivado: ya no admite revisiones ni publicaciones.',
  sin_permiso: 'Solo el líder técnico o el responsable de los aplicativos de todos sus contratos arman este paquete.',
};

export function avisoDeError(error: unknown, porDefecto: string): Aviso {
  if (error instanceof ErrorApi) {
    if (error.codigo === 'no_publicable') {
      const detalles = Array.isArray(error.cuerpo?.detalles) ? (error.cuerpo.detalles as string[]) : [];
      return { tono: 'error', texto: detalles.length > 0 ? `No se puede publicar: ${detalles.join(' · ')}` : error.mensajePara(porDefecto) };
    }
    const conocido = error.codigo ? MENSAJES[error.codigo] : undefined;
    if (conocido) return { tono: 'error', texto: conocido };
    if (error.estado === 403) return { tono: 'error', texto: MENSAJES.sin_permiso! };
    return { tono: 'error', texto: error.mensajePara(porDefecto) };
  }
  return { tono: 'error', texto: porDefecto };
}

/** Casilla de verificación con toda la fila como objetivo táctil (≥ 44 px). */
export function Casilla({ marcada, alCambiar, children, deshabilitada = false }: { marcada: boolean; alCambiar: () => void; children: ReactNode; deshabilitada?: boolean }) {
  return (
    <label
      className={cn(
        'flex min-h-toque min-w-0 items-start gap-3 rounded-xl border p-3 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-[var(--flit-brand-ink)]',
        marcada ? 'border-atlas-tinta bg-atlas-activo' : 'border-border bg-card',
        deshabilitada ? 'cursor-not-allowed opacity-60' : 'cursor-pointer',
      )}
    >
      <input type="checkbox" checked={marcada} disabled={deshabilitada} onChange={alCambiar} className="mt-0.5 size-5 shrink-0 accent-[var(--flit-brand-ink)]" />
      <span className="min-w-0 flex-1 break-words">{children}</span>
    </label>
  );
}

/** Opción única con forma de chips de 44 px (radios: un `<select>` viola la CSP en WebKit). */
export function Opciones<T extends string>({
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
            key={o.valor || 'todas'}
            className={cn(
              'inline-flex min-h-toque max-w-full cursor-pointer items-center gap-1.5 rounded-xl border px-3 text-sm break-all has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-[var(--flit-brand-ink)]',
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

export function InsigniasPaquete({ paquete: p }: { paquete: PaqueteResumen }) {
  return (
    <span className="flex flex-wrap gap-1.5">
      <Etiqueta tono={p.estado === 'publicado' ? 'exito' : p.estado === 'archivado' ? 'neutral' : 'info'}>{TEXTO_ESTADO[p.estado]}</Etiqueta>
      <Etiqueta>Revisión {p.revisionActual}</Etiqueta>
      {p.revisionPublicada !== null && <Etiqueta tono="exito">Vigente: la {p.revisionPublicada}</Etiqueta>}
      {p.estado !== 'archivado' && p.cambiosSinPublicar && <Etiqueta tono="alerta">Cambios sin publicar</Etiqueta>}
      <Etiqueta>
        {p.contratos.length} {p.contratos.length === 1 ? 'contrato' : 'contratos'} · {p.operaciones} {p.operaciones === 1 ? 'operación' : 'operaciones'}
      </Etiqueta>
    </span>
  );
}
