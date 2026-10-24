import { ChevronRight } from 'lucide-react';
import { useDeferredValue, useMemo, useState } from 'react';

import { Hoja } from '../../../app/ui/Hoja';
import { cn } from '../../../app/ui/Tarjeta';
import type { OperacionOpenApi } from '../spec/openapi';
import { normalizar } from '../spec/referencia';
import { DetalleOperacion } from './DetalleOperacion';
import { Buscador, Chips, Metodo, useEscritorio } from './ui';

const SIN_ETIQUETA = 'Sin etiqueta';

/**
 * Catálogo de operaciones de un OpenAPI (HU 3.6): búsqueda, filtros por método y etiqueta, y la lista agrupada por
 * etiqueta. En escritorio la ficha de la operación va al lado; en el móvil, en una hoja inferior. La operación elegida va
 * en la URL (`?op=`) para compartirla.
 */
export function Operaciones({
  operaciones,
  elegida,
  alElegir,
}: {
  operaciones: OperacionOpenApi[];
  elegida: string | null;
  alElegir: (clave: string | null) => void;
}) {
  const escritorio = useEscritorio();
  const [texto, setTexto] = useState('');
  const [metodo, setMetodo] = useState('');
  const [etiqueta, setEtiqueta] = useState('');
  const buscado = useDeferredValue(normalizar(texto.trim()));

  const metodos = useMemo(() => [...new Set(operaciones.map((o) => o.metodo))], [operaciones]);
  const etiquetas = useMemo(() => [...new Set(operaciones.flatMap((o) => (o.etiquetas.length ? o.etiquetas : [SIN_ETIQUETA])))], [operaciones]);
  const filtradas = useMemo(
    () =>
      operaciones.filter(
        (o) =>
          (!metodo || o.metodo === metodo) &&
          (!etiqueta || (o.etiquetas.length ? o.etiquetas.includes(etiqueta) : etiqueta === SIN_ETIQUETA)) &&
          (!buscado || normalizar([o.ruta, o.resumen, o.operationId, o.descripcion].filter(Boolean).join(' ')).includes(buscado)),
      ),
    [operaciones, metodo, etiqueta, buscado],
  );
  const grupos = useMemo(() => {
    const mapa = new Map<string, OperacionOpenApi[]>();
    for (const o of filtradas) {
      const grupo = o.esWebhook ? 'Webhooks' : (o.etiquetas[0] ?? SIN_ETIQUETA);
      mapa.set(grupo, [...(mapa.get(grupo) ?? []), o]);
    }
    return [...mapa.entries()];
  }, [filtradas]);
  const actual = operaciones.find((o) => o.clave === elegida) ?? null;
  const webhooks = operaciones.filter((o) => o.esWebhook).length;

  return (
    <div className="grid min-w-0 gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
      <div className="flex min-w-0 flex-col gap-3">
        <Buscador etiqueta="Buscar operaciones" valor={texto} alCambiar={setTexto} placeholder="Ruta, resumen u operationId" />
        {metodos.length > 1 && (
          <Chips
            leyenda="Método"
            nombre="filtro-metodo"
            valor={metodo}
            alCambiar={setMetodo}
            opciones={[{ valor: '', etiqueta: 'Todos' }, ...metodos.map((m) => ({ valor: m, etiqueta: m }))]}
          />
        )}
        {etiquetas.length > 1 && (
          <Chips
            leyenda="Etiqueta"
            nombre="filtro-etiqueta"
            valor={etiqueta}
            alCambiar={setEtiqueta}
            opciones={[{ valor: '', etiqueta: 'Todas' }, ...etiquetas.map((e) => ({ valor: e, etiqueta: e }))]}
          />
        )}
        <p role="status" className="text-xs text-atlas-texto-suave">
          {filtradas.length === operaciones.length
            ? `${operaciones.length - webhooks} operaciones${webhooks ? ` y ${webhooks} webhooks` : ''}`
            : `${filtradas.length} de ${operaciones.length}`}
        </p>
        {filtradas.length === 0 && <p className="rounded-xl border border-border bg-card p-4 text-sm text-atlas-texto-suave">Nada coincide con la búsqueda.</p>}
        <nav aria-label="Operaciones" className="flex flex-col gap-3">
          {grupos.map(([grupo, lista]) => (
            <section key={grupo} aria-label={grupo}>
              <h3 className="mb-1 text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">{grupo}</h3>
              <ul className="flex flex-col gap-1">
                {lista.map((o) => (
                  <li key={o.clave}>
                    <button
                      type="button"
                      aria-current={o.clave === elegida ? 'true' : undefined}
                      onClick={() => alElegir(o.clave)}
                      className={cn(
                        'flex min-h-toque w-full min-w-0 items-center gap-2 rounded-xl border px-2.5 py-2 text-left',
                        o.clave === elegida ? 'border-atlas-tinta bg-atlas-activo' : 'border-border bg-card hover:bg-atlas-activo',
                      )}
                    >
                      <Metodo metodo={o.metodo} />
                      <span className="flex min-w-0 flex-1 flex-col">
                        <code className="font-mono text-xs break-all">{o.ruta}</code>
                        {o.resumen && <span className="truncate text-xs text-atlas-texto-suave">{o.resumen}</span>}
                      </span>
                      <ChevronRight className="size-4 shrink-0 text-atlas-texto-suave" aria-hidden="true" />
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </nav>
      </div>

      {escritorio ? (
        <div className="min-w-0">
          <div className="sticky top-4 max-h-[calc(100dvh-6rem)] overflow-y-auto rounded-2xl border border-border bg-card p-5 shadow-tarjeta">
            {actual ? <DetalleOperacion operacion={actual} /> : <p className="text-sm text-atlas-texto-suave">Elige una operación para ver sus parámetros, ejemplos y errores.</p>}
          </div>
        </div>
      ) : (
        <Hoja abierta={!!actual} alCerrar={() => alElegir(null)} titulo={actual ? `${actual.metodo} ${actual.ruta}` : 'Operación'}>
          <div className="px-4 pt-2 pb-6">{actual && <DetalleOperacion operacion={actual} />}</div>
        </Hoja>
      )}
    </div>
  );
}
