import { Check, Download, Minus, Plus } from 'lucide-react';
import { useDeferredValue, useMemo, useState } from 'react';

import { Boton, cn } from '../../../app/ui/Tarjeta';
import { aCsv, descargar } from '../spec/csv';
import { ETIQUETAS_OBLIGATORIEDAD, type FilaMatriz, type MatrizObligatoriedad as Matriz, normalizar, type Obligatoriedad } from '../spec/referencia';
import { Buscador, Chips, Etiqueta, type Tono } from './ui';

const TONO: Record<Obligatoriedad, Tono> = { obligatorio: 'peligro', opcional: 'info', no_aplica: 'neutral' };
const ICONO = { obligatorio: Check, opcional: Plus, no_aplica: Minus } as const;

/** Celda de la matriz: ícono, color y texto (el color nunca va solo, WCAG 1.4.1). */
export function Nivel({ nivel }: { nivel: Obligatoriedad }) {
  const Icono = ICONO[nivel];
  return (
    <Etiqueta tono={TONO[nivel]}>
      <Icono className="size-3.5" aria-hidden="true" />
      {ETIQUETAS_OBLIGATORIEDAD[nivel]}
    </Etiqueta>
  );
}

/**
 * Matriz de obligatoriedad (HU 3.9) a partir de `x-flit-obligatoriedad`: campos × tipos de trámite, con filtro por tipo
 * y búsqueda de campo. En escritorio es una tabla; en el móvil (o con un tipo elegido) es una lista por tipo de trámite,
 * agrupada en obligatorios, opcionales y no aplica. Se exporta a CSV tal como se ve.
 */
export function MatrizObligatoriedad({ matriz, nombreArchivo }: { matriz: Matriz; nombreArchivo: string }) {
  const [tipo, setTipo] = useState('');
  const [texto, setTexto] = useState('');
  const buscado = useDeferredValue(normalizar(texto.trim()));
  const filas = useMemo(
    () => matriz.filas.filter((f) => !buscado || normalizar(`${f.campo} ${f.detalle ?? ''} ${f.condicion ?? ''}`).includes(buscado)),
    [matriz.filas, buscado],
  );
  const tiposVisibles = tipo ? matriz.tipos.filter((t) => t.clave === tipo) : matriz.tipos;
  const titulo = matriz.clase === 'documentos' ? 'Documento' : 'Campo';
  // En el móvil, sin tipo elegido, se muestra el primero: una lista por tipo de trámite es lo legible a 360 px.
  const tipoMovil = tipo || matriz.tipos[0]!.clave;

  function exportar() {
    const encabezados = [titulo, ...(matriz.clase === 'documentos' ? ['Nombre'] : []), ...tiposVisibles.map((t) => t.nombre), 'Condición'];
    const datos = filas.map((f) => [
      f.campo,
      ...(matriz.clase === 'documentos' ? [f.detalle] : []),
      ...tiposVisibles.map((t) => ETIQUETAS_OBLIGATORIEDAD[f.valores[t.clave] ?? 'no_aplica']),
      f.condicion,
    ]);
    descargar(`${nombreArchivo}-obligatoriedad-${matriz.esquema}.csv`, aCsv(encabezados, datos));
  }

  return (
    <section aria-label={`Matriz de obligatoriedad de ${matriz.esquema}`} className="flex min-w-0 flex-col gap-3">
      <p className="text-sm text-atlas-texto-suave">
        {matriz.clase === 'documentos' ? 'Documentos adjuntos' : 'Campos'} de <code className="font-mono">{matriz.esquema}</code> según el tipo de trámite
        {matriz.propiedad && (
          <>
            {' '}
            (<code className="font-mono">{matriz.propiedad}</code>)
          </>
        )}
        .
      </p>
      <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <div className="min-w-0 md:w-80">
          <Buscador etiqueta={`Buscar ${titulo.toLowerCase()}`} valor={texto} alCambiar={setTexto} placeholder={matriz.clase === 'documentos' ? 'Nombre del documento' : 'Nombre del campo'} />
        </div>
        <Boton variante="secundario" onClick={exportar}>
          <Download className="size-4" aria-hidden="true" />
          Exportar CSV
        </Boton>
      </div>
      <Chips
        leyenda="Tipo de trámite"
        nombre={`tipo-tramite-${matriz.esquema}`}
        valor={tipo}
        alCambiar={setTipo}
        opciones={[
          { valor: '', etiqueta: 'Todos' },
          ...matriz.tipos.map((t) => ({ valor: t.clave, etiqueta: t.valor ? `${t.nombre} (${t.valor})` : t.nombre })),
        ]}
      />
      <p role="status" className="text-xs text-atlas-texto-suave">
        {filas.length === matriz.filas.length ? `${filas.length} ${matriz.clase === 'documentos' ? 'documentos' : 'campos'}` : `${filas.length} de ${matriz.filas.length}`}
      </p>

      {/* Escritorio: la tabla completa (o la columna del tipo elegido). */}
      <table className="hidden w-full border-collapse text-left text-sm md:table">
        <caption className="sr-only">Matriz de obligatoriedad de {matriz.esquema}</caption>
        <thead>
          <tr className="border-b border-border text-xs text-atlas-texto-suave">
            <th scope="col" className="w-56 px-2 py-2 font-medium">
              {titulo}
            </th>
            {tiposVisibles.map((t) => (
              <th key={t.clave} scope="col" className="px-2 py-2 font-medium">
                {t.nombre}
              </th>
            ))}
            <th scope="col" className="w-64 px-2 py-2 font-medium">
              Condición
            </th>
          </tr>
        </thead>
        <tbody>
          {filas.map((f) => (
            <tr key={f.campo} className="border-b border-border align-top last:border-0">
              <th scope="row" className="px-2 py-2 text-left font-normal">
                <code className="font-mono text-xs break-all">{f.campo}</code>
                {f.detalle && <span className="block text-xs text-atlas-texto-suave">{f.detalle}</span>}
              </th>
              {tiposVisibles.map((t) => (
                <td key={t.clave} className="px-2 py-2">
                  <Nivel nivel={f.valores[t.clave] ?? 'no_aplica'} />
                </td>
              ))}
              <td className="px-2 py-2 text-xs break-words text-atlas-texto-suave">{f.condicion ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Móvil: por tipo de trámite, como lista. */}
      <div className="md:hidden">
        <PorTipo filas={filas} tipo={tipoMovil} nombreTipo={matriz.tipos.find((t) => t.clave === tipoMovil)!.nombre} />
      </div>
    </section>
  );
}

function PorTipo({ filas, tipo, nombreTipo }: { filas: FilaMatriz[]; tipo: string; nombreTipo: string }) {
  const grupos: Obligatoriedad[] = ['obligatorio', 'opcional', 'no_aplica'];
  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-sm font-semibold">{nombreTipo}</h3>
      {grupos.map((g) => {
        const lista = filas.filter((f) => (f.valores[tipo] ?? 'no_aplica') === g);
        if (lista.length === 0) return null;
        const contenido = (
          <ul className="flex flex-col gap-1.5">
            {lista.map((f) => (
              <li key={f.campo} className="rounded-xl border border-border bg-card p-3 text-sm">
                <span className="flex min-w-0 flex-wrap items-center justify-between gap-2">
                  <code className="min-w-0 font-mono text-xs break-all">{f.campo}</code>
                  <Nivel nivel={g} />
                </span>
                {f.detalle && <span className="mt-1 block text-xs text-atlas-texto-suave">{f.detalle}</span>}
                {f.condicion && <span className="mt-1 block text-xs">{f.condicion}</span>}
              </li>
            ))}
          </ul>
        );
        // «No aplica» va plegado: es lo que menos interesa a quien integra.
        return g === 'no_aplica' ? (
          <details key={g} className="rounded-xl border border-border">
            <summary className={cn('flex min-h-toque cursor-pointer items-center px-3 text-sm font-medium')}>
              {ETIQUETAS_OBLIGATORIEDAD[g]} ({lista.length})
            </summary>
            <div className="p-2">{contenido}</div>
          </details>
        ) : (
          <section key={g} aria-label={`${ETIQUETAS_OBLIGATORIEDAD[g]} en ${nombreTipo}`} className="flex flex-col gap-1.5">
            <h4 className="text-xs font-semibold tracking-wide text-atlas-texto-suave uppercase">
              {ETIQUETAS_OBLIGATORIEDAD[g]} ({lista.length})
            </h4>
            {contenido}
          </section>
        );
      })}
    </div>
  );
}
