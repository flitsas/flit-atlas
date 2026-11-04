import { lazy, Suspense } from 'react';

import { ErrorApi } from '../../../app/api';
import { cn } from '../../../app/ui/Tarjeta';

/** Piezas de interfaz de las guías (un módulo no importa a otro: estas son propias, ATLAS-ADR-0026). */
export const claseCampo =
  'mt-1 min-h-12 w-full min-w-0 max-w-full rounded-xl border border-border bg-card px-3 text-base outline-none focus-visible:border-atlas-tinta';

export type Aviso = { tono: 'exito' | 'error'; texto: string } | null;

const MENSAJES: Record<string, string> = {
  revision_desactualizada: 'Alguien guardó otra revisión mientras trabajabas: recarga la guía y vuelve a intentarlo.',
  sin_cambios: 'No hay cambios frente a la última revisión.',
  aprobacion_requerida: 'Una guía compartible con clientes solo la aprueba el responsable principal o secundario del aplicativo.',
  ya_publicada: 'Esta revisión ya está publicada.',
};

export function avisoDeError(error: unknown, porDefecto: string): Aviso {
  if (error instanceof ErrorApi) {
    const conocido = error.codigo ? MENSAJES[error.codigo] : undefined;
    if (conocido) return { tono: 'error', texto: conocido };
    if (error.estado === 403) return { tono: 'error', texto: error.mensajePara('No tienes permiso para esta acción.') };
    return { tono: 'error', texto: error.mensajePara(porDefecto) };
  }
  return { tono: 'error', texto: porDefecto };
}

export function MostrarAviso({ aviso }: { aviso: Aviso }) {
  if (!aviso) return null;
  return (
    <div
      role={aviso.tono === 'error' ? 'alert' : 'status'}
      className={cn(
        'rounded-xl border p-3 text-sm',
        aviso.tono === 'error'
          ? 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]'
          : 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
      )}
    >
      {aviso.texto}
    </div>
  );
}

export function Cargando({ etiqueta, alto = 'h-24' }: { etiqueta: string; alto?: string }) {
  return <div role="status" aria-label={etiqueta} className={cn('animate-pulse rounded-xl bg-atlas-activo', alto)} />;
}

// marked + DOMPurify en su propio chunk: solo se descargan al ver o previsualizar una guía.
const MarkdownDiferido = lazy(() => import('./Markdown'));

export function VistaMarkdown({ texto, etiqueta }: { texto: string; etiqueta: string }) {
  return (
    <Suspense fallback={<Cargando etiqueta="Preparando la vista" />}>
      <MarkdownDiferido texto={texto} etiqueta={etiqueta} />
    </Suspense>
  );
}
