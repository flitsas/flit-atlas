import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { VistaDiagrama } from '../../../app/diagramas/VistaDiagrama';
import { Boton, Insignia } from '../../../app/ui/Tarjeta';
import { formatearFechaHora } from '../../../app/ui/formato';
import {
  mensajeDeError,
  restaurarVersion,
  TEXTO_ORIGEN,
  TEXTO_TIPO,
  TEXTO_VISIBILIDAD,
  useCambioDiagrama,
  useComentar,
  useComentarios,
  useDiagrama,
  useVersion,
  useVersiones,
} from '../api';
import { Aviso, Cargando, claseCampo } from '../componentes';

function Comentarios({ id, version }: { id: string; version: number }) {
  const comentarios = useComentarios(id);
  const comentar = useComentar(id);
  const [texto, setTexto] = useState('');
  return (
    <section aria-labelledby="titulo-comentarios" className="flex min-w-0 flex-col gap-2">
      <h2 id="titulo-comentarios" className="text-lg font-semibold">
        Comentarios
      </h2>
      {comentarios.isError && <p role="alert">No pudimos cargar los comentarios.</p>}
      {comentarios.data?.length === 0 && <p className="text-sm text-atlas-texto-suave">Todavía no hay comentarios.</p>}
      <ul className="flex flex-col gap-2">
        {comentarios.data?.map((c) => (
          <li key={c.id} className="rounded-xl border border-border bg-card p-3 text-sm">
            <p className="break-words whitespace-pre-wrap">{c.texto}</p>
            <p className="mt-1 text-xs text-atlas-texto-suave">
              {c.persona.nombre} · versión {c.versionNumero} · {formatearFechaHora(new Date(c.escritoEn))}
            </p>
          </li>
        ))}
      </ul>
      <form
        aria-label="Escribir un comentario"
        className="flex flex-col gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (texto.trim()) comentar.mutate({ versionNumero: version, texto }, { onSuccess: () => setTexto('') });
        }}
      >
        <label className="text-sm font-medium">
          Comentario sobre la versión {version}
          <textarea required maxLength={2000} rows={3} value={texto} onChange={(e) => setTexto(e.target.value)} className={`${claseCampo} py-2`} />
        </label>
        {comentar.isError && <Aviso tono="error">{mensajeDeError(comentar.error)}</Aviso>}
        <div>
          <Boton type="submit" variante="secundario" disabled={comentar.isPending || !texto.trim()}>
            Comentar
          </Boton>
        </div>
      </form>
    </section>
  );
}

function Versiones({ id, ultima, puedeEditar, elegida, alElegir }: { id: string; ultima: number; puedeEditar: boolean; elegida: number | null; alElegir: (n: number | null) => void }) {
  const versiones = useVersiones(id);
  const cambio = useCambioDiagrama();
  const [nota, setNota] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [restaurando, setRestaurando] = useState(false);
  return (
    <section aria-labelledby="titulo-versiones" className="flex min-w-0 flex-col gap-2">
      <h2 id="titulo-versiones" className="text-lg font-semibold">
        Versiones
      </h2>
      {versiones.isError && <p role="alert">No pudimos cargar las versiones.</p>}
      <ul className="flex flex-col gap-2">
        {versiones.data?.map((v) => (
          <li key={v.numero} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-card p-3 text-sm">
            <div className="min-w-0">
              <p className="font-medium break-words">
                Versión {v.numero} · {TEXTO_ORIGEN[v.origen] ?? v.origen}
                {v.restauradaDe ? ` de la ${v.restauradaDe}` : ''}
              </p>
              <p className="break-words text-atlas-texto-suave">{v.nota}</p>
              <p className="text-xs text-atlas-texto-suave">
                {v.autor.nombre} · {formatearFechaHora(new Date(v.guardadaEn))}
              </p>
            </div>
            <Boton variante="secundario" aria-pressed={(elegida ?? ultima) === v.numero} onClick={() => alElegir(v.numero === ultima ? null : v.numero)}>
              Ver la {v.numero}
            </Boton>
          </li>
        ))}
      </ul>
      {puedeEditar && elegida !== null && (
        <form
          aria-label={`Restaurar la versión ${elegida}`}
          className="flex flex-col gap-2 rounded-xl border border-border p-3"
          onSubmit={(e) => {
            e.preventDefault();
            setRestaurando(true);
            setError(null);
            restaurarVersion(id, elegida, nota, ultima)
              .then(async () => {
                setNota('');
                alElegir(null);
                await cambio();
              })
              .catch((motivo: unknown) => setError(mensajeDeError(motivo)))
              .finally(() => setRestaurando(false));
          }}
        >
          <p className="text-sm">Restaurar crea una versión nueva igual a la {elegida}; la historia no cambia.</p>
          <label className="text-sm font-medium">
            Nota del cambio
            <input required maxLength={500} value={nota} onChange={(e) => setNota(e.target.value)} className={claseCampo} />
          </label>
          {error && <Aviso tono="error">{error}</Aviso>}
          <div>
            <Boton type="submit" disabled={restaurando || !nota.trim()}>
              Restaurar la versión {elegida}
            </Boton>
          </div>
        </form>
      )}
    </section>
  );
}

/** Un diagrama con su historial y sus comentarios (HU 6.6 y 6.7). */
export default function DetalleDiagrama() {
  const { id = '' } = useParams();
  const detalle = useDiagrama(id);
  const [elegida, setElegida] = useState<number | null>(null);
  const version = useVersion(id, elegida);

  if (detalle.isPending) return <Cargando etiqueta="Cargando el diagrama" alto="h-64" />;
  if (detalle.isError) {
    return (
      <p role="alert">
        {detalle.error instanceof ErrorApi && detalle.error.estado === 404 ? 'Este diagrama no existe o no tienes acceso.' : 'No pudimos cargar el diagrama. Revisa tu conexión.'}
      </p>
    );
  }
  const { diagrama: d, version: ultima, referencias, puedeEditar } = detalle.data;
  const mostrada = elegida === null ? ultima : version.data;

  return (
    <article aria-labelledby="titulo-diagrama" className="flex min-w-0 flex-col gap-4">
      <Link to="/diagramas" className="inline-flex min-h-toque w-fit items-center gap-1 text-sm underline">
        <ArrowLeft aria-hidden className="size-4" /> Diagramas
      </Link>
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h1 id="titulo-diagrama" className="text-2xl font-semibold break-words">
            {mostrada?.version.titulo ?? d.titulo}
          </h1>
          <p className="flex flex-wrap items-center gap-2 text-sm text-atlas-texto-suave">
            <span>{TEXTO_TIPO[d.tipo] ?? d.tipo}</span>
            <Insignia tono="neutral">{TEXTO_VISIBILIDAD[mostrada?.version.visibilidad ?? d.visibilidad]}</Insignia>
            <code className="break-all text-xs">{`<Diagrama id="${d.codigo}" />`}</code>
          </p>
        </div>
        {puedeEditar && (
          <Link to={`/diagramas/${d.id}/editar`} className="inline-flex min-h-toque items-center rounded-xl bg-atlas-tinta px-4 font-medium text-white">
            Editar
          </Link>
        )}
      </header>

      {elegida !== null && version.isPending && <Cargando etiqueta="Cargando la versión" alto="h-64" />}
      {elegida !== null && version.isError && <p role="alert">No pudimos cargar esa versión.</p>}
      {mostrada ? (
        <VistaDiagrama key={`${d.id}-${mostrada.version.numero}`} tipo={d.tipo} fuente={mostrada.fuente} titulo={mostrada.version.titulo} />
      ) : (
        elegida === null && <p className="text-sm text-atlas-texto-suave">No tienes acceso a ninguna versión de este diagrama.</p>
      )}

      {referencias.length > 0 && (
        <section aria-labelledby="titulo-referencias">
          <h2 id="titulo-referencias" className="text-lg font-semibold">
            Aplicativos que menciona
          </h2>
          <ul className="flex flex-wrap gap-2">
            {referencias.map((r) => (
              <li key={r.aplicativoId}>
                <Link to={`/catalogo/${encodeURIComponent(r.slug)}`} className="inline-flex min-h-toque items-center underline">
                  {r.nombre}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="grid min-w-0 gap-6 lg:grid-cols-2">
        {ultima && <Versiones id={d.id} ultima={d.versionActual} puedeEditar={puedeEditar} elegida={elegida} alElegir={setElegida} />}
        {ultima && <Comentarios id={d.id} version={mostrada?.version.numero ?? ultima.version.numero} />}
      </div>
    </article>
  );
}
