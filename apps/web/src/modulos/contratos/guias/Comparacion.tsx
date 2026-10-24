import { diffLines } from 'diff';
import { useMemo } from 'react';

import { cn } from '../../../app/ui/Tarjeta';
import { type RevisionDetalle, TEXTO_VISIBILIDAD, textoVersiones } from './api';

type Linea = { tipo: 'igual' | 'agregada' | 'quitada'; texto: string };

/** Diff línea a línea de dos textos con jsdiff (`diffLines`). Exportado para las pruebas. */
export function compararLineas(antes: string, despues: string): Linea[] {
  const lineas: Linea[] = [];
  for (const parte of diffLines(antes, despues)) {
    const tipo = parte.added ? 'agregada' : parte.removed ? 'quitada' : 'igual';
    const textos = parte.value.replace(/\n$/, '').split('\n');
    for (const texto of textos) lineas.push({ tipo, texto });
  }
  return lineas;
}

const SIGNO = { igual: ' ', agregada: '+', quitada: '−' } as const;
const LECTOR = { igual: '', agregada: 'Agregada: ', quitada: 'Quitada: ' } as const;

/**
 * Comparación de dos revisiones de una guía (HU 3.7, ATLAS-ADR-0040): el cuerpo línea a línea con jsdiff, en el cliente
 * y en su propio chunk, y los demás campos que cambiaron. Las revisiones son inmutables: los dos textos llegan de la API
 * tal como se guardaron.
 */
export default function Comparacion({ antes, despues }: { antes: RevisionDetalle; despues: RevisionDetalle }) {
  const lineas = useMemo(() => compararLineas(antes.contenido, despues.contenido), [antes.contenido, despues.contenido]);
  const agregadas = lineas.filter((l) => l.tipo === 'agregada').length;
  const quitadas = lineas.filter((l) => l.tipo === 'quitada').length;
  const campos = [
    { nombre: 'Título', a: antes.titulo, b: despues.titulo },
    { nombre: 'Visibilidad', a: TEXTO_VISIBILIDAD[antes.visibilidad], b: TEXTO_VISIBILIDAD[despues.visibilidad] },
    { nombre: 'Versiones', a: textoVersiones(antes), b: textoVersiones(despues) },
    { nombre: 'Operación', a: antes.operacion ?? 'Todo el contrato', b: despues.operacion ?? 'Todo el contrato' },
  ].filter((c) => c.a !== c.b);

  return (
    <div className="flex min-w-0 flex-col gap-3">
      {antes.huella === despues.huella ? (
        <p role="status" className="text-sm">
          Son iguales: mismo título, cuerpo, versiones, operación y visibilidad.
        </p>
      ) : (
        <p role="status" className="text-sm text-atlas-texto-suave">
          {agregadas === 1 ? '1 línea agregada' : `${agregadas} líneas agregadas`} y {quitadas === 1 ? '1 quitada' : `${quitadas} quitadas`}
          {campos.length > 0 && ` · cambió ${campos.map((c) => c.nombre.toLowerCase()).join(', ')}`}.
        </p>
      )}
      {campos.length > 0 && (
        <dl className="grid gap-2 text-sm">
          {campos.map((c) => (
            <div key={c.nombre} className="rounded-xl border border-border p-2">
              <dt className="font-medium">{c.nombre}</dt>
              <dd className="break-words">
                <del className="text-[var(--badge-danger-fg)]">{c.a}</del> → <ins className="text-[var(--badge-success-fg)] no-underline">{c.b}</ins>
              </dd>
            </div>
          ))}
        </dl>
      )}
      <ol aria-label="Cambios línea a línea" className="overflow-x-auto rounded-xl border border-border font-mono text-xs">
        {lineas.map((l, i) => (
          <li
            // El orden de las líneas es estable para un par de revisiones.
            key={i}
            className={cn(
              'flex min-w-max gap-2 px-2 py-0.5 whitespace-pre',
              l.tipo === 'agregada' && 'border-l-4 border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-foreground',
              l.tipo === 'quitada' && 'border-l-4 border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-foreground',
            )}
          >
            <span aria-hidden="true" className="w-3 shrink-0 select-none">
              {SIGNO[l.tipo]}
            </span>
            <span>
              {LECTOR[l.tipo] && <span className="sr-only">{LECTOR[l.tipo]}</span>}
              {l.texto || ' '}
            </span>
          </li>
        ))}
      </ol>
    </div>
  );
}
