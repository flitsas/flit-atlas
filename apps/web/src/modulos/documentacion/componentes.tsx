import { lazy, Suspense } from 'react';
import { Link } from 'react-router';

import { formatearRelativo } from '../../app/ui/formato';
import { Insignia } from '../../app/ui/Tarjeta';
import { type DocumentoResumen, TEXTO_ESTADO, TEXTO_TIPO, TEXTO_VISIBILIDAD } from './api';

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

// unified, remark y el renderizador en su propio chunk: solo se descargan al leer o previsualizar.
const DocumentoDiferido = lazy(() => import('./mdx/Documento'));

export function VistaDocumento({ texto, etiqueta, conIndice }: { texto: string; etiqueta: string; conIndice?: boolean }) {
  return (
    <Suspense fallback={<Cargando etiqueta="Preparando el documento" alto="h-48" />}>
      <DocumentoDiferido texto={texto} etiqueta={etiqueta} conIndice={conIndice} />
    </Suspense>
  );
}

export function TarjetaDocumento({ documento: d, aplicativo }: { documento: DocumentoResumen; aplicativo?: string }) {
  return (
    <Link to={`/documentacion/${d.id}`} className="flex min-h-toque min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3 hover:bg-atlas-activo">
      <span className="font-medium break-words">{d.titulo}</span>
      <span className="text-xs text-atlas-texto-suave">
        {[TEXTO_TIPO[d.tipo] ?? d.tipo, aplicativo, formatearRelativo(new Date(d.actualizadoEn))].filter(Boolean).join(' · ')}
      </span>
      <span className="flex flex-wrap gap-1">
        <Insignia tono="neutral">{TEXTO_VISIBILIDAD[d.visibilidad] ?? d.visibilidad}</Insignia>
        {d.estado !== 'publicado' && <Insignia>{TEXTO_ESTADO[d.estado] ?? d.estado}</Insignia>}
      </span>
    </Link>
  );
}
