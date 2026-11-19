import { GitCommitHorizontal } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { formatearFechaHora } from '../../../app/ui/formato';
import { cn } from '../../../app/ui/Tarjeta';
import { type CambioDiff, useDiff, type VersionResumen } from '../api';
import { Etiqueta, type Tono } from './ui';

const NIVELES: Record<string, { etiqueta: string; tono: Tono }> = {
  error: { etiqueta: 'Error', tono: 'peligro' },
  warning: { etiqueta: 'Advertencia', tono: 'alerta' },
  info: { etiqueta: 'Informativo', tono: 'info' },
};

/**
 * Línea de tiempo de versiones (HU 3.6): fecha, fuente (repo, workflow y commit) y si rompe compatibilidad, más el diff
 * entre dos versiones cualesquiera (`GET /contratos/{id}/diff`, oasdiff en atlas-render). Por defecto compara la
 * versión que se está viendo con la anterior.
 */
export function Versiones({ contratoId, tipo, versiones, actual }: { contratoId: string; tipo: string; versiones: VersionResumen[]; actual: string }) {
  const indice = Math.max(0, versiones.findIndex((v) => v.version === actual));
  const [hasta, setHasta] = useState(versiones[indice]?.version);
  const [desde, setDesde] = useState(versiones[indice + 1]?.version);

  return (
    <div className="grid min-w-0 gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <section aria-labelledby="titulo-linea-tiempo" className="min-w-0">
        <h3 id="titulo-linea-tiempo" className="mb-2 text-base font-semibold">
          Línea de tiempo
        </h3>
        <ol className="relative flex flex-col gap-3 border-l-2 border-border pl-4">
          {versiones.map((v) => (
            <li key={v.id} className="relative min-w-0">
              <span
                aria-hidden="true"
                className={cn('absolute top-1.5 -left-[1.4rem] size-3 rounded-full border-2 border-card', v.tieneBreaking ? 'bg-[var(--badge-danger-fg)]' : 'bg-atlas-tinta')}
              />
              <div className={cn('flex min-w-0 flex-col gap-1 rounded-xl border p-3 text-sm', v.version === actual ? 'border-atlas-tinta bg-atlas-activo' : 'border-border bg-card')}>
                <span className="flex flex-wrap items-center gap-2">
                  <Link to={`/contratos/${contratoId}/v/${encodeURIComponent(v.version)}`} className="font-semibold break-all text-atlas-tinta hover:underline">
                    {v.version}
                  </Link>
                  {v.version === actual && <span className="text-xs">(la que ves)</span>}
                  <Etiqueta tono={v.esEstable ? 'exito' : 'info'}>{v.esEstable ? 'Estable' : 'Prerelease'}</Etiqueta>
                  <Etiqueta tono={v.tieneBreaking ? 'peligro' : 'neutral'}>{v.tieneBreaking ? 'Breaking: sí' : 'Breaking: no'}</Etiqueta>
                  {v.estado !== 'vigente' && <Etiqueta tono="alerta">{v.estado === 'deprecada' ? 'Deprecada' : 'Retirada'}</Etiqueta>}
                </span>
                <time dateTime={v.publicadoEn} className="text-xs text-atlas-texto-suave">
                  {formatearFechaHora(new Date(v.publicadoEn))}
                </time>
                <span className="flex flex-wrap items-center gap-1 text-xs break-all text-atlas-texto-suave">
                  <GitCommitHorizontal className="size-3.5 shrink-0" aria-hidden="true" />
                  {v.repositorio}@{v.rama} · {v.workflow ?? v.workflowDelJob.split('@')[0]} · commit <code>{v.commit.slice(0, 7)}</code>
                </span>
              </div>
            </li>
          ))}
        </ol>
      </section>

      <section aria-labelledby="titulo-diff" className="min-w-0">
        <h3 id="titulo-diff" className="mb-2 text-base font-semibold">
          Diff entre dos versiones
        </h3>
        {versiones.length < 2 ? (
          <p className="text-sm text-atlas-texto-suave">Hace falta más de una versión para comparar.</p>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="grid gap-3 sm:grid-cols-2">
              <ElegirVersion leyenda="Desde (base)" nombre="diff-desde" versiones={versiones} valor={desde} alCambiar={setDesde} />
              <ElegirVersion leyenda="Hasta" nombre="diff-hasta" versiones={versiones} valor={hasta} alCambiar={setHasta} />
            </div>
            <ResultadoDiff contratoId={contratoId} tipo={tipo} desde={desde} hasta={hasta} />
          </div>
        )}
      </section>
    </div>
  );
}

/** Lista de radios (un `<select>` viola la CSP en WebKit); con muchas versiones se desplaza dentro de su caja. */
function ElegirVersion({
  leyenda,
  nombre,
  versiones,
  valor,
  alCambiar,
}: {
  leyenda: string;
  nombre: string;
  versiones: VersionResumen[];
  valor: string | undefined;
  alCambiar: (v: string) => void;
}) {
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-xs font-medium text-atlas-texto-suave">{leyenda}</legend>
      <div className="flex max-h-48 flex-col gap-1 overflow-y-auto rounded-xl border border-border bg-card p-1">
        {versiones.map((v) => (
          <label
            key={v.id}
            className={cn(
              'flex min-h-10 cursor-pointer items-center gap-2 rounded-lg px-2 text-sm break-all has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-[var(--flit-brand-ink)]',
              v.version === valor ? 'bg-atlas-activo font-medium' : 'hover:bg-atlas-activo',
            )}
          >
            <input type="radio" name={nombre} value={v.version} checked={v.version === valor} onChange={() => alCambiar(v.version)} className="size-4 shrink-0 accent-[var(--flit-brand-ink)]" />
            {v.version}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function ResultadoDiff({ contratoId, tipo, desde, hasta }: { contratoId: string; tipo: string; desde?: string; hasta?: string }) {
  const diff = useDiff(contratoId, tipo === 'openapi' ? desde : undefined, hasta);
  if (tipo !== 'openapi') {
    return <p className="rounded-xl border border-border bg-card p-4 text-sm text-atlas-texto-suave">El diff de contratos {tipo === 'asyncapi' ? 'AsyncAPI' : 'proto'} todavía no está disponible; por ahora solo OpenAPI.</p>;
  }
  if (!desde || !hasta || desde === hasta) return <p className="text-sm text-atlas-texto-suave">Elige dos versiones distintas.</p>;
  if (diff.isPending) return <div role="status" aria-label="Calculando el diff" className="h-32 animate-pulse rounded-xl bg-atlas-activo" />;
  if (diff.isError) {
    const error = diff.error instanceof ErrorApi ? diff.error : null;
    return (
      <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
        {error?.estado === 422 || error?.estado === 503 ? error.mensajePara('No se pudo calcular el diff.') : 'No pudimos calcular el diff. Inténtalo de nuevo.'}
      </p>
    );
  }
  const d = diff.data;
  const rompen = d.cambios.filter((c) => c.breaking);
  const otros = d.cambios.filter((c) => !c.breaking);
  return (
    <div className="flex flex-col gap-3">
      <p role="status" className="text-sm">
        {d.sinDiferencias
          ? `Sin diferencias entre ${d.desde} y ${d.hasta}.`
          : `${d.resumen.total} ${d.resumen.total === 1 ? 'cambio' : 'cambios'} de ${d.desde} a ${d.hasta}; ${rompen.length === 0 ? 'ninguno rompe compatibilidad' : `${rompen.length} rompe${rompen.length === 1 ? '' : 'n'} compatibilidad`}.`}
      </p>
      {rompen.length > 0 && <ListaCambios titulo="Rompen compatibilidad" cambios={rompen} />}
      {otros.length > 0 && <ListaCambios titulo="Otros cambios" cambios={otros} />}
      <p className="text-xs text-atlas-texto-suave">Calculado por {d.motor}.</p>
    </div>
  );
}

function ListaCambios({ titulo, cambios }: { titulo: string; cambios: CambioDiff[] }) {
  return (
    <section aria-label={titulo} className="flex flex-col gap-1.5">
      <h4 className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">
        {titulo} ({cambios.length})
      </h4>
      <ul className="flex flex-col gap-1.5">
        {cambios.map((c, i) => {
          const nivel = NIVELES[c.nivel] ?? { etiqueta: c.nivel, tono: 'neutral' as Tono };
          return (
            <li key={`${c.regla}-${i}`} className="flex min-w-0 flex-col gap-1 rounded-xl border border-border bg-card p-3 text-sm">
              <span className="flex flex-wrap items-center gap-2">
                <Etiqueta tono={nivel.tono}>{nivel.etiqueta}</Etiqueta>
                {c.metodo && c.ruta && (
                  <code className="font-mono text-xs break-all">
                    {c.metodo} {c.ruta}
                  </code>
                )}
              </span>
              <span className="break-words">{c.texto}</span>
              <code className="text-xs break-all text-atlas-texto-suave">{c.regla}</code>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
