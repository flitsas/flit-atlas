import { diffLines } from 'diff';
import { ArrowLeft } from 'lucide-react';
import { useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';

import { cn } from '../../../app/ui/Tarjeta';
import { type ResumenVersion, useVersion, useVersiones } from '../api';
import { Cargando } from '../componentes';

type Linea = { tipo: 'igual' | 'agregada' | 'quitada'; texto: string };

/** Diff línea a línea con jsdiff, en el cliente. Exportado para las pruebas. */
export function compararLineas(antes: string, despues: string): Linea[] {
  return diffLines(antes, despues).flatMap((parte) => {
    const tipo: Linea['tipo'] = parte.added ? 'agregada' : parte.removed ? 'quitada' : 'igual';
    return parte.value
      .replace(/\n$/, '')
      .split('\n')
      .map((texto) => ({ tipo, texto }));
  });
}

const SIGNO = { igual: ' ', agregada: '+', quitada: '−' } as const;
const LECTOR = { igual: '', agregada: 'Agregada: ', quitada: 'Quitada: ' } as const;

function Elegir({ titulo, nombre, versiones, valor, alCambiar }: { titulo: string; nombre: string; versiones: ResumenVersion[]; valor: number; alCambiar: (n: number) => void }) {
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-sm font-medium">{titulo}</legend>
      <div className="flex flex-wrap gap-2">
        {versiones.map((v) => (
          <label
            key={v.numero}
            className={cn(
              'inline-flex min-h-toque cursor-pointer items-center rounded-xl border px-3 text-sm',
              valor === v.numero ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
            )}
          >
            <input type="radio" name={nombre} className="sr-only" checked={valor === v.numero} onChange={() => alCambiar(v.numero)} />
            {v.numero}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/** Comparación de dos versiones inmutables de un documento (HU 5.4). */
export default function Comparar() {
  const { id = '' } = useParams();
  const [parametros, setParametros] = useSearchParams();
  const versiones = useVersiones(id);
  const lista = versiones.data ?? [];
  const b = Number(parametros.get('b')) || lista[0]?.numero || null;
  const a = Number(parametros.get('a')) || lista[1]?.numero || lista[0]?.numero || null;
  const antes = useVersion(id, a);
  const despues = useVersion(id, b);
  const lineas = useMemo(
    () => (antes.data && despues.data ? compararLineas(antes.data.contenido, despues.data.contenido) : []),
    [antes.data, despues.data],
  );
  const elegir = (clave: 'a' | 'b', valor: number) =>
    setParametros(
      (p) => {
        p.set(clave, String(valor));
        return p;
      },
      { replace: true },
    );

  return (
    <div className="mx-auto flex max-w-5xl min-w-0 flex-col gap-4">
      <Link to={`/documentacion/${encodeURIComponent(id)}`} className="inline-flex min-h-toque items-center gap-2 self-start text-sm underline">
        <ArrowLeft className="size-4" aria-hidden="true" /> Documento
      </Link>
      <h1 className="text-2xl font-semibold">Comparar versiones</h1>
      {versiones.isPending && <Cargando etiqueta="Cargando las versiones" />}
      {versiones.isError && <p role="alert">No pudimos cargar las versiones.</p>}
      {a !== null && b !== null && (
        <div className="grid gap-3 sm:grid-cols-2">
          <Elegir titulo="Versión anterior" nombre="comparar-a" versiones={lista} valor={a} alCambiar={(n) => elegir('a', n)} />
          <Elegir titulo="Versión nueva" nombre="comparar-b" versiones={lista} valor={b} alCambiar={(n) => elegir('b', n)} />
        </div>
      )}
      {(antes.isError || despues.isError) && <p role="alert">No pudimos cargar una de las versiones.</p>}
      {antes.data && despues.data && (
        <>
          <p role="status" className="text-sm text-atlas-texto-suave">
            {antes.data.version.sha256 === despues.data.version.sha256
              ? 'Las dos versiones son iguales.'
              : resumen(lineas)}
          </p>
          <ol tabIndex={0} aria-label="Cambios línea a línea" className="overflow-x-auto rounded-xl border border-border font-mono text-xs">
            {lineas.map((l, i) => (
              <li
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
        </>
      )}
    </div>
  );
}

function resumen(lineas: Linea[]): string {
  const agregadas = lineas.filter((l) => l.tipo === 'agregada').length;
  const quitadas = lineas.filter((l) => l.tipo === 'quitada').length;
  return `${agregadas === 1 ? '1 línea agregada' : `${agregadas} líneas agregadas`} y ${quitadas === 1 ? '1 quitada' : `${quitadas} quitadas`}.`;
}
