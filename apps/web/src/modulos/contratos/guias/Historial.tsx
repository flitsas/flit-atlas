import { lazy, Suspense, useState } from 'react';

import { formatearFechaHora } from '../../../app/ui/formato';
import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import { restaurarRevision, TEXTO_ORIGEN, useCambioGuia, useRevision, useRevisiones } from './api';
import { type Aviso, avisoDeError, Cargando, claseCampo, MostrarAviso } from './componentes';

// jsdiff en su propio chunk: solo se descarga al comparar.
const Comparacion = lazy(() => import('./Comparacion'));

/**
 * Historial de revisiones de una guía (HU 3.7): cada una con autor, fecha y nota; comparar dos (diff línea a línea en el
 * cliente) y restaurar una, que crea una revisión nueva igual a ella.
 */
export function Historial({
  guiaId,
  revisionActual,
  puedeEditar,
  alRestaurar,
}: {
  guiaId: string;
  revisionActual: number;
  puedeEditar: boolean;
  alRestaurar: (aviso: Aviso) => void;
}) {
  const revisiones = useRevisiones(guiaId);
  const [par, setPar] = useState<{ desde: number; hasta: number } | null>(null);
  const [restaurando, setRestaurando] = useState<number | null>(null);

  if (revisiones.isPending) return <Cargando etiqueta="Cargando el historial" />;
  if (revisiones.isError || !revisiones.data) return <p role="alert">No pudimos cargar el historial. Revisa tu conexión.</p>;
  const lista = revisiones.data;

  return (
    <div className="flex min-w-0 flex-col gap-3">
      {par && <ComparacionRevisiones guiaId={guiaId} {...par} alCerrar={() => setPar(null)} />}
      <ol aria-label="Revisiones de la guía" className="flex flex-col gap-3">
        {lista.map((r, i) => (
          <li key={r.numero} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-semibold">Revisión {r.numero}</p>
              <Insignia tono="neutral">{TEXTO_ORIGEN[r.origen]}</Insignia>
              {r.numero === revisionActual && <Insignia tono="info">Última</Insignia>}
              {r.publicada && <Insignia tono="exito">Publicada</Insignia>}
              {r.restauradaDe !== null && <Insignia tono="info">Igual a la {r.restauradaDe}</Insignia>}
            </div>
            <p className="text-sm break-words">{r.nota}</p>
            <p className="text-xs text-atlas-texto-suave">
              {r.autor.nombre} · <time dateTime={r.guardadaEn}>{formatearFechaHora(new Date(r.guardadaEn))}</time> · {r.titulo}
            </p>
            <div className="flex flex-wrap gap-2">
              {lista[i + 1] && (
                <Boton variante="secundario" onClick={() => setPar({ desde: lista[i + 1]!.numero, hasta: r.numero })}>
                  Comparar con la {lista[i + 1]!.numero}
                </Boton>
              )}
              {r.numero !== revisionActual && (
                <Boton variante="secundario" onClick={() => setPar({ desde: r.numero, hasta: revisionActual })}>
                  Comparar con la última
                </Boton>
              )}
              {puedeEditar && r.numero !== revisionActual && (
                <Boton variante="fantasma" onClick={() => setRestaurando(r.numero)}>
                  Restaurar
                </Boton>
              )}
            </div>
            {restaurando === r.numero && (
              <FormularioRestaurar
                guiaId={guiaId}
                numero={r.numero}
                revisionBase={revisionActual}
                alTerminar={(aviso) => {
                  setRestaurando(null);
                  alRestaurar(aviso);
                }}
              />
            )}
          </li>
        ))}
      </ol>
    </div>
  );
}

function FormularioRestaurar({
  guiaId,
  numero,
  revisionBase,
  alTerminar,
}: {
  guiaId: string;
  numero: number;
  revisionBase: number;
  alTerminar: (aviso: Aviso) => void;
}) {
  const [nota, setNota] = useState('');
  const [error, setError] = useState<Aviso>(null);
  const restaurar = useCambioGuia((n: string) => restaurarRevision(guiaId, numero, n, revisionBase));
  return (
    <form
      aria-label={`Restaurar la revisión ${numero}`}
      className="flex flex-col gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        restaurar.mutate(nota, {
          onSuccess: (d) => alTerminar({ tono: 'exito', texto: `Restaurada: la revisión ${d.revision.numero} es igual a la ${numero}.` }),
          onError: (er) => setError(avisoDeError(er, 'No se pudo restaurar la revisión.')),
        });
      }}
    >
      <label className="text-sm font-medium">
        Nota de la restauración
        <textarea required maxLength={500} rows={2} value={nota} onChange={(e) => setNota(e.target.value)} className={`${claseCampo} py-2`} />
      </label>
      <MostrarAviso aviso={error} />
      <Boton type="submit" className="self-start" disabled={!nota.trim() || restaurar.isPending}>
        Restaurar la revisión {numero}
      </Boton>
    </form>
  );
}

function ComparacionRevisiones({ guiaId, desde, hasta, alCerrar }: { guiaId: string; desde: number; hasta: number; alCerrar: () => void }) {
  const antes = useRevision(guiaId, desde);
  const despues = useRevision(guiaId, hasta);
  return (
    <Tarjeta
      titulo={`De la revisión ${desde} a la ${hasta}`}
      accion={
        <Boton variante="fantasma" onClick={alCerrar}>
          Cerrar
        </Boton>
      }
    >
      {(antes.isPending || despues.isPending) && <Cargando etiqueta="Comparando" />}
      {(antes.isError || despues.isError) && <p role="alert">No pudimos comparar las revisiones.</p>}
      {antes.data && despues.data && (
        <Suspense fallback={<Cargando etiqueta="Comparando" />}>
          <Comparacion antes={antes.data} despues={despues.data} />
        </Suspense>
      )}
    </Tarjeta>
  );
}
