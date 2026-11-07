import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { Hoja } from '../../../app/ui/Hoja';
import { Insignia } from '../../../app/ui/Tarjeta';
import { formatearRelativo } from '../../../app/ui/formato';
import {
  CANALES,
  type Canal,
  elementosDeVista,
  ORDEN_CATEGORIAS,
  TEXTO_CANAL,
  TEXTO_CATEGORIA,
  useFicha,
  useModelo,
  useVigente,
  type Vista,
} from '../api';
import { VisorLikeC4 } from '../VisorLikeC4';

const claseOpcion = (activa: boolean) =>
  `inline-flex min-h-toque items-center rounded-xl border px-3 text-sm ${activa ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card'}`;

function FichaElemento({ publicacion, elemento, alCerrar }: { publicacion: string; elemento: string | null; alCerrar: () => void }) {
  const ficha = useFicha(publicacion, elemento);
  const d = ficha.data;
  return (
    <Hoja abierta={elemento !== null} alCerrar={alCerrar} titulo={d?.titulo ?? 'Elemento'}>
      {ficha.isPending && <div role="status" aria-label="Cargando la ficha" className="m-4 h-24 animate-pulse rounded-xl bg-atlas-activo" />}
      {ficha.isError && <p role="alert" className="p-4">Este elemento no tiene ficha o no puedes verla.</p>}
      {d && (
        <div className="flex min-w-0 flex-col gap-3 px-4 pb-4 text-sm">
          <p className="break-words text-xs text-atlas-texto-suave">
            {d.kind} · {d.id}
          </p>
          {d.descripcion && <p className="break-words">{d.descripcion}</p>}
          {d.tecnologia && <p>Tecnología: {d.tecnologia}</p>}
          {d.etiquetas.length > 0 && (
            <ul aria-label="Etiquetas" className="flex flex-wrap gap-1">
              {d.etiquetas.map((e) => (
                <li key={e}>
                  <Insignia tono="neutral">{e}</Insignia>
                </li>
              ))}
            </ul>
          )}
          {d.aplicativo && (
            <p>
              Ficha del catálogo:{' '}
              <Link className="font-medium underline" to={d.aplicativo.ruta}>
                {d.aplicativo.nombre}
              </Link>
            </p>
          )}
          {d.contratos.length > 0 && (
            <div>
              <h3 className="font-medium">Contratos</h3>
              <ul className="list-disc pl-5">
                {d.contratos.map((c) => (
                  <li key={c.ruta} className="break-words">
                    {c.rutaAtlas ? (
                      <Link className="underline" to={c.rutaAtlas}>
                        {c.nombre}
                        {c.ultimaVersion ? ` (${c.ultimaVersion})` : ''}
                      </Link>
                    ) : (
                      <>
                        {c.ruta} <span className="text-atlas-texto-suave">(sin publicar en Atlas o sin permiso)</span>
                      </>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {d.adr.length > 0 && (
            <div>
              <h3 className="font-medium">ADR</h3>
              <ul className="list-disc pl-5">
                {d.adr.map((a) => (
                  <li key={a.codigo}>
                    <Link className="underline" to={a.rutaAtlas}>
                      {a.codigo}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {d.enlaces.length > 0 && (
            <div>
              <h3 className="font-medium">Enlaces</h3>
              <ul className="list-disc pl-5">
                {d.enlaces.map((e) => (
                  <li key={e.url} className="break-words">
                    <a className="underline" href={e.url} target="_blank" rel="noreferrer noopener">
                      {e.titulo ?? e.url}
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {d.driftAbierto !== null && d.driftAbierto > 0 && (
            <p>
              <Link className="font-medium underline" to="/arquitectura/drift">
                {d.driftAbierto === 1 ? '1 hallazgo de drift abierto' : `${d.driftAbierto} hallazgos de drift abiertos`}
              </Link>
            </p>
          )}
          {Object.keys(d.metadatos).length > 0 && (
            <details>
              <summary className="min-h-toque cursor-pointer font-medium">Metadatos</summary>
              <dl className="mt-1 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
                {Object.entries(d.metadatos).map(([k, v]) => (
                  <div key={k} className="contents">
                    <dt className="text-atlas-texto-suave">{k}</dt>
                    <dd className="min-w-0 break-words">{v.join(', ')}</dd>
                  </div>
                ))}
              </dl>
            </details>
          )}
        </div>
      )}
    </Hoja>
  );
}

/** Vistas de un proyecto (HU 6.3): canal, fecha («volver en el tiempo», plan 01 §9.2), vista por categoría, lienzo y lista de elementos. */
export default function VistasArquitectura() {
  const { proyecto = '', vista: vistaRuta } = useParams();
  const [parametros, setParametros] = useSearchParams();
  const navegar = useNavigate();
  const canal = (CANALES as readonly string[]).includes(parametros.get('ambiente') ?? '') ? (parametros.get('ambiente') as Canal) : 'pdn';
  const en = parametros.get('en');
  const [elemento, setElemento] = useState<string | null>(null);

  const detalle = useVigente(proyecto, canal, en);
  const publicacion = detalle.data?.publicacion;
  const modelo = useModelo(publicacion?.id);
  const todas = useMemo(() => detalle.data?.vistas ?? [], [detalle.data]);
  // LikeC4 agrega una vista `index` automática a cada modelo: solo se ofrece si no hay otra.
  const vistas = useMemo(() => (todas.some((v) => v.id !== 'index') ? todas.filter((v) => v.id !== 'index') : todas), [todas]);
  const vista: Vista | undefined = todas.find((v) => v.id === vistaRuta) ?? vistas.find((v) => v.categoria === 'landscape') ?? vistas[0];
  const consulta = parametros.toString();

  const irAVista = useCallback(
    (id: string) => navegar(`/arquitectura/proyectos/${encodeURIComponent(proyecto)}/vistas/${encodeURIComponent(id)}${consulta ? `?${consulta}` : ''}`),
    [navegar, proyecto, consulta],
  );
  const cambiar = (clave: string, valor: string | null) =>
    setParametros((p) => {
      if (valor) p.set(clave, valor);
      else p.delete(clave);
      return p;
    });
  const tocar = useCallback((id: string) => setElemento(id), []);
  const elementos = useMemo(() => (vista ? elementosDeVista(modelo.data, vista.id) : []), [modelo.data, vista]);
  const noHay = detalle.error instanceof ErrorApi && detalle.error.estado === 404;

  return (
    <section aria-labelledby="titulo-vistas" className="flex min-w-0 flex-col gap-4">
      <header className="min-w-0">
        <Link to="/arquitectura" className="text-sm underline">
          Arquitectura
        </Link>
        <h1 id="titulo-vistas" className="truncate text-2xl font-semibold">
          {proyecto}
        </h1>
        {publicacion && (
          <p className="text-sm text-atlas-texto-suave">
            {publicacion.version ?? publicacion.commitSha.slice(0, 7)} · rama {publicacion.rama} · {formatearRelativo(new Date(publicacion.publicadoEn))}
            {publicacion.estado === 'borrador' ? ' · borrador' : ''}
            {publicacion.visibilidad === 'restringido' ? ' · restringido' : ''}
          </p>
        )}
      </header>

      <div className="flex min-w-0 flex-wrap items-end gap-3">
        <div role="group" aria-label="Ambiente" className="flex flex-wrap gap-1">
          {CANALES.map((c) => (
            <button key={c} type="button" aria-pressed={c === canal} onClick={() => cambiar('ambiente', c)} className={claseOpcion(c === canal)}>
              {TEXTO_CANAL[c]}
            </button>
          ))}
        </div>
        <label className="min-w-0 text-sm font-medium">
          Ver cómo estaba el
          <input
            type="datetime-local"
            value={en ? new Date(en).toISOString().slice(0, 16) : ''}
            onChange={(e) => cambiar('en', e.target.value ? new Date(`${e.target.value}Z`).toISOString() : null)}
            className="mt-1 block min-h-toque max-w-full rounded-xl border border-border bg-card px-3"
          />
          <span className="block text-xs font-normal text-atlas-texto-suave">Hora UTC</span>
        </label>
      </div>

      {detalle.isPending && <div role="status" aria-label="Cargando las vistas" className="h-24 animate-pulse rounded-xl bg-atlas-activo" />}
      {noHay && <p className="text-sm text-atlas-texto-suave">No hay un modelo publicado en {TEXTO_CANAL[canal]} para esta fecha, o no puedes verlo.</p>}
      {detalle.isError && !noHay && <p role="alert">No pudimos cargar las vistas. Revisa tu conexión.</p>}

      {!noHay && vistas.length > 0 && (
        <nav aria-label="Vistas" className="flex min-w-0 flex-col gap-2">
          {ORDEN_CATEGORIAS.map((categoria) => {
            const deCategoria = vistas.filter((v) => v.categoria === categoria);
            return deCategoria.length === 0 ? null : (
              <div key={categoria} className="min-w-0">
                <h2 className="text-xs font-medium uppercase text-atlas-texto-suave">{TEXTO_CATEGORIA[categoria]}</h2>
                <div className="mt-1 flex flex-wrap gap-1">
                  {deCategoria.map((v) => (
                    <button key={v.id} type="button" aria-current={v.id === vista?.id ? 'page' : undefined} onClick={() => irAVista(v.id)} className={claseOpcion(v.id === vista?.id)}>
                      {v.titulo}
                    </button>
                  ))}
                </div>
              </div>
            );
          })}
        </nav>
      )}

      {!noHay && vista && modelo.data && publicacion && (
        <VisorLikeC4 clave={publicacion.id} modelo={modelo.data} vista={vista.id} titulo={vista.titulo} alTocarElemento={tocar} alNavegar={irAVista} />
      )}
      {modelo.isError && <p role="alert">No pudimos cargar el modelo. Revisa tu conexión.</p>}

      {!noHay && elementos.length > 0 && (
        <details className="min-w-0 rounded-xl border border-border bg-card p-3">
          <summary className="min-h-toque cursor-pointer font-medium">Elementos de esta vista ({elementos.length})</summary>
          <ul className="mt-2 grid gap-1 sm:grid-cols-2 lg:grid-cols-3">
            {elementos.map((e) => (
              <li key={e.id} className="min-w-0">
                <button type="button" onClick={() => tocar(e.id)} className="min-h-toque w-full truncate rounded-xl px-2 text-left text-sm hover:bg-atlas-activo">
                  {e.titulo}
                </button>
              </li>
            ))}
          </ul>
        </details>
      )}

      {publicacion && <FichaElemento publicacion={publicacion.id} elemento={elemento} alCerrar={() => setElemento(null)} />}
    </section>
  );
}
