import { Download } from 'lucide-react';
import { useDeferredValue, useMemo, useState } from 'react';

import { aCsv, descargar } from '../spec/csv';
import { type FilaTabla, normalizar, type TablaReferencia } from '../spec/referencia';
import { Buscador, TablaResponsiva } from './ui';

/**
 * Tablas de referencia (HU 3.9): los esquemas reutilizables con `enum` y `x-enum-descriptions` (tipo de documento, tipo
 * de trámite…) como tablas de código y descripción, con búsqueda en todas a la vez y exportación a CSV de cada una.
 */
export function TablasReferencia({ tablas, nombreArchivo }: { tablas: TablaReferencia[]; nombreArchivo: string }) {
  const [texto, setTexto] = useState('');
  const buscado = useDeferredValue(normalizar(texto.trim()));
  const visibles = useMemo(
    () =>
      tablas
        .map((t) => {
          if (!buscado || normalizar(`${t.titulo} ${t.id}`).includes(buscado)) return t;
          return { ...t, filas: t.filas.filter((f) => normalizar(`${f.codigo} ${f.descripcion ?? ''}`).includes(buscado)) };
        })
        .filter((t) => t.filas.length > 0),
    [tablas, buscado],
  );

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <Buscador etiqueta="Buscar en las tablas de referencia" valor={texto} alCambiar={setTexto} placeholder="Tabla, código o descripción" />
      <p role="status" className="text-xs text-atlas-texto-suave">
        {visibles.length === tablas.length ? `${tablas.length} tablas` : `${visibles.length} de ${tablas.length} tablas`}
      </p>
      <ul className="flex flex-col gap-2">
        {visibles.map((t) => (
          <li key={t.id}>
            <details className="group rounded-2xl border border-border bg-card shadow-tarjeta" open={!!buscado || undefined}>
              <summary className="flex min-h-toque cursor-pointer flex-wrap items-center gap-x-2 px-4 py-2">
                <span className="font-medium">{t.titulo}</span>
                <code className="font-mono text-xs text-atlas-texto-suave">{t.id}</code>
                <span className="text-xs text-atlas-texto-suave">{t.filas.length === 1 ? '1 valor' : `${t.filas.length} valores`}</span>
              </summary>
              <div className="flex flex-col gap-3 border-t border-border p-4">
                {t.descripcion && <p className="text-sm text-atlas-texto-suave">{t.descripcion}</p>}
                <TablaResponsiva<FilaTabla>
                  titulo={t.titulo}
                  filas={t.filas}
                  clave={(f) => f.codigo}
                  columnas={[
                    { id: 'codigo', titulo: 'Código', principal: true, className: 'w-40', celda: (f) => <code className="font-mono text-xs break-all">{f.codigo}</code> },
                    { id: 'descripcion', titulo: 'Descripción', celda: (f) => f.descripcion ?? '—' },
                  ]}
                />
                <div>
                  <button
                    type="button"
                    onClick={() => descargar(`${nombreArchivo}-${t.id}.csv`, aCsv(['Código', 'Descripción'], t.filas.map((f) => [f.codigo, f.descripcion])))}
                    className="inline-flex min-h-toque items-center gap-2 rounded-xl border border-border px-3 text-sm font-medium hover:bg-atlas-activo"
                  >
                    <Download className="size-4" aria-hidden="true" />
                    Exportar CSV<span className="sr-only"> de {t.titulo}</span>
                  </button>
                </div>
              </div>
            </details>
          </li>
        ))}
      </ul>
      {visibles.length === 0 && <p className="rounded-xl border border-border bg-card p-4 text-sm text-atlas-texto-suave">Nada coincide con la búsqueda.</p>}
    </div>
  );
}
