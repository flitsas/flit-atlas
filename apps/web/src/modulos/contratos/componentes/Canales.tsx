import { ArrowRight, GitCommitHorizontal } from 'lucide-react';
import { Link } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { formatearFechaHora } from '../../../app/ui/formato';
import { useCanales } from '../api';
import { Etiqueta } from './ui';

/**
 * Pestaña «Canales» de la pantalla del contrato (HU 3.4 y 3.6): qué versión corre en cada ambiente, según el aviso del
 * CD (`GET /contratos/{id}/canales`), y el historial de cambios. Móvil primero: una tarjeta por ambiente apilada (tres
 * columnas desde `sm`) y el historial como lista, sin tablas que obliguen a desplazarse de lado.
 */
export function Canales({ contratoId, actual }: { contratoId: string; actual: string }) {
  const canales = useCanales(contratoId);

  if (canales.isPending) return <div role="status" aria-label="Cargando los canales" className="h-40 animate-pulse rounded-2xl bg-card" />;
  if (canales.isError) {
    return (
      <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
        {canales.error instanceof ErrorApi && canales.error.estado === 404
          ? 'Este contrato ya no existe.'
          : 'No pudimos cargar los canales. Revisa tu conexión e inténtalo de nuevo.'}
      </p>
    );
  }
  const { ambientes, canales: actuales, historial } = canales.data;
  const enlace = (version: string) => `/contratos/${contratoId}/v/${encodeURIComponent(version)}`;

  return (
    <div className="flex min-w-0 flex-col gap-6">
      <section aria-labelledby="titulo-por-ambiente" className="min-w-0">
        <h3 id="titulo-por-ambiente" className="mb-2 text-base font-semibold">
          Versión por ambiente
        </h3>
        <ul aria-label="Versión por ambiente" className="grid min-w-0 gap-3 sm:grid-cols-3">
          {ambientes.map((ambiente) => {
            const canal = actuales.find((c) => c.ambiente === ambiente);
            return (
              <li key={ambiente} aria-label={ambiente} className="flex min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3 text-sm">
                <span className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">{ambiente}</span>
                {canal ? (
                  <>
                    <span className="flex flex-wrap items-center gap-2">
                      <Link to={enlace(canal.version)} className="font-semibold break-all text-atlas-tinta hover:underline">
                        {canal.version}
                      </Link>
                      <Etiqueta tono={canal.esEstable ? 'exito' : 'info'}>{canal.esEstable ? 'Estable' : 'Prerelease'}</Etiqueta>
                      {canal.version === actual && <span className="text-xs">(la que ves)</span>}
                    </span>
                    <time dateTime={canal.desplegadoEn} className="text-xs text-atlas-texto-suave">
                      Desplegada el {formatearFechaHora(new Date(canal.desplegadoEn))}
                    </time>
                    <span className="flex flex-wrap items-center gap-1 text-xs break-all text-atlas-texto-suave">
                      <GitCommitHorizontal className="size-3.5 shrink-0" aria-hidden="true" />
                      {canal.desplegadoPor} · commit <code>{canal.commit.slice(0, 7)}</code>
                    </span>
                  </>
                ) : (
                  <span className="text-atlas-texto-suave">Sin versión desplegada todavía.</span>
                )}
              </li>
            );
          })}
        </ul>
      </section>

      <section aria-labelledby="titulo-historial-canales" className="min-w-0">
        <h3 id="titulo-historial-canales" className="mb-2 text-base font-semibold">
          Historial
        </h3>
        {historial.length === 0 ? (
          <p className="text-sm text-atlas-texto-suave">El CD todavía no ha avisado ningún despliegue de este contrato.</p>
        ) : (
          <ol aria-label="Historial de canales" className="flex flex-col gap-2">
            {historial.map((h, i) => (
              <li key={`${h.ambiente}-${h.desplegadoEn}-${i}`} className="flex min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3 text-sm">
                <span className="flex flex-wrap items-center gap-2">
                  <Etiqueta>{h.ambiente}</Etiqueta>
                  {h.versionAnterior && (
                    <>
                      <span className="break-all">{h.versionAnterior}</span>
                      <ArrowRight className="size-4 shrink-0" aria-hidden="true" />
                      <span className="sr-only">pasó a</span>
                    </>
                  )}
                  <Link to={enlace(h.version)} className="font-semibold break-all text-atlas-tinta hover:underline">
                    {h.version}
                  </Link>
                </span>
                <time dateTime={h.desplegadoEn} className="text-xs text-atlas-texto-suave">
                  {formatearFechaHora(new Date(h.desplegadoEn))}
                </time>
                <span className="text-xs break-all text-atlas-texto-suave">
                  {h.repositorio}@{h.rama} · commit <code>{h.commit.slice(0, 7)}</code>
                </span>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}
