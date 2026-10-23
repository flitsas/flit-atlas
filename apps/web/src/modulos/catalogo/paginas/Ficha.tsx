import { ArrowLeft, ExternalLink, Hourglass, Pencil, Plus, Trash2 } from 'lucide-react';
import { type ComponentType, type KeyboardEvent, lazy, type LazyExoticComponent, Suspense, useId, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import type { AplicativoDeFicha, CargadorPestana } from '../../../app/modulos/contrato';
import { type PestanaResuelta, resolverPestanasFicha } from '../../../app/modulos/resolver';
import { NoEncontrada } from '../../../app/paginas/Estados';
import { useSesion } from '../../../app/sesion';
import { formatearFecha } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, cn, Tarjeta } from '../../../app/ui/Tarjeta';
import { type FichaAplicativo, useCambiosCatalogo, useFicha } from '../api';
import { type Aviso, avisoDeError, Insignias, MostrarAviso, TextoConMarca } from '../componentes';
import { ESPACIOS_FICHA, type EspacioFicha, ID_RESUMEN } from '../espacios';
import { type Edicion, FormularioAplicativo, tiposHijo } from '../Formulario';
import { GRUPOS, TIPOS } from '../textos';

/** Un `React.lazy` por cargador de pestaña, creado una sola vez (como `diferido` del shell para páginas y widgets). */
const cache = new Map<CargadorPestana, LazyExoticComponent<ComponentType<{ aplicativo: AplicativoDeFicha }>>>();
function pestanaDiferida(cargar: CargadorPestana) {
  let componente = cache.get(cargar);
  if (!componente) {
    componente = lazy(cargar);
    cache.set(cargar, componente);
  }
  return componente;
}

type Pestana =
  | { id: string; titulo: string; tipo: 'resumen' }
  | { id: string; titulo: string; tipo: 'modulo'; pestana: PestanaResuelta }
  | { id: string; titulo: string; tipo: 'proximamente'; espacio: EspacioFicha };

/**
 * Ficha del aplicativo (HU 2.1, ATLAS-ADR-0028): la página a la que enlazan todos los módulos. «Resumen» es del
 * catálogo; los demás espacios los llenan los módulos con `pestanasFicha` en su manifiesto (sin que el catálogo los
 * importe). La pestaña activa va en la URL (`?pestana=`), para compartirla.
 */
export default function Ficha() {
  const { slug = '' } = useParams();
  const ficha = useFicha(slug);

  if (ficha.isPending) return <div role="status" aria-label="Cargando la ficha" className="mx-auto h-60 max-w-5xl animate-pulse rounded-3xl bg-card" />;
  if (ficha.isError) {
    if (ficha.error instanceof ErrorApi && ficha.error.estado === 404) return <NoEncontrada />;
    return (
      <p role="alert" className="mx-auto max-w-5xl rounded-xl border border-border bg-card p-4 text-sm">
        No pudimos cargar la ficha. Revisa tu conexión e inténtalo de nuevo.
      </p>
    );
  }
  return <Contenido ficha={ficha.data} />;
}

function Contenido({ ficha }: { ficha: FichaAplicativo }) {
  const { modulos, acceso } = useSesion();
  const navegar = useNavigate();
  const [parametros, setParametros] = useSearchParams();
  const [edicion, setEdicion] = useState<Edicion | null>(null);
  const [eliminando, setEliminando] = useState(false);
  const [aviso, setAviso] = useState<Aviso>(null);
  const { crear, actualizar, eliminar } = useCambiosCatalogo();

  const pestanas = useMemo<Pestana[]>(() => {
    const { visibles, sinPermiso } = resolverPestanasFicha(modulos, acceso, ficha.tipo);
    const porId = new Map(visibles.map((p) => [p.id, p]));
    const espacios = new Set(ESPACIOS_FICHA.map((e) => e.id));
    return [
      { id: ID_RESUMEN, titulo: 'Resumen', tipo: 'resumen' },
      ...ESPACIOS_FICHA.filter((e) => !sinPermiso.has(e.id)).map<Pestana>((espacio) => {
        const pestana = porId.get(espacio.id);
        return pestana ? { id: espacio.id, titulo: pestana.titulo, tipo: 'modulo', pestana } : { id: espacio.id, titulo: espacio.titulo, tipo: 'proximamente', espacio };
      }),
      ...visibles
        .filter((p) => !espacios.has(p.id) && p.id !== ID_RESUMEN)
        .map<Pestana>((pestana) => ({ id: pestana.id, titulo: pestana.titulo, tipo: 'modulo', pestana })),
    ];
  }, [modulos, acceso, ficha.tipo]);

  const activa = pestanas.find((p) => p.id === parametros.get('pestana')) ?? pestanas[0]!;
  const hijosPosibles = tiposHijo(ficha);

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-4">
      <nav aria-label="Ruta" className="flex min-w-0 flex-wrap items-center gap-x-1 gap-y-1 text-sm text-atlas-texto-suave">
        <Link to="/catalogo" className="inline-flex min-h-toque items-center gap-1 rounded-lg pr-2 font-medium text-atlas-tinta hover:underline">
          <ArrowLeft className="size-4" aria-hidden="true" />
          Catálogo
        </Link>
        {ficha.ancestros.map((a) => (
          <span key={a.id} className="inline-flex min-w-0 items-center gap-1">
            <span aria-hidden="true">›</span>
            <Link to={`/catalogo/${a.slug}`} className="inline-flex min-h-toque max-w-48 items-center truncate hover:underline">
              {a.nombre}
            </Link>
          </span>
        ))}
      </nav>

      <header className="flex flex-col gap-3 rounded-3xl bg-portada p-5 text-white shadow-tarjeta sm:p-6">
        <p className="text-sm text-white/80">
          {TIPOS[ficha.tipo]} · {GRUPOS[ficha.grupo]}
        </p>
        <h1 className="text-2xl font-semibold break-words sm:text-3xl">{ficha.nombre}</h1>
        <div className="rounded-xl bg-white/95 p-2 dark:bg-card">
          <Insignias aplicativo={ficha} />
        </div>
        {(ficha.permisos.editar || ficha.permisos.gestionar) && (
          <div className="flex flex-wrap gap-2">
            {ficha.permisos.editar && (
              <Boton variante="secundario" onClick={() => setEdicion({ modo: 'editar', ficha })}>
                <Pencil className="size-4" aria-hidden="true" />
                Editar
              </Boton>
            )}
            {ficha.permisos.gestionar && hijosPosibles.length > 0 && (
              <Boton variante="secundario" onClick={() => setEdicion({ modo: 'crear', padre: ficha })}>
                <Plus className="size-4" aria-hidden="true" />
                {hijosPosibles.includes('modulo') ? 'Nuevo módulo o servicio' : 'Nuevo servicio'}
              </Boton>
            )}
            {ficha.permisos.gestionar && ficha.hijos.length === 0 && (
              <Boton variante="secundario" onClick={() => setEliminando(true)}>
                <Trash2 className="size-4" aria-hidden="true" />
                Eliminar
              </Boton>
            )}
          </div>
        )}
      </header>
      <MostrarAviso aviso={aviso} />

      <Pestanas
        pestanas={pestanas}
        activa={activa.id}
        alElegir={(id) => setParametros(id === ID_RESUMEN ? {} : { pestana: id }, { replace: true })}
      >
        {activa.tipo === 'resumen' && <Resumen ficha={ficha} />}
        {activa.tipo === 'proximamente' && <Proximamente espacio={activa.espacio} />}
        {activa.tipo === 'modulo' && <PestanaDeModulo pestana={activa.pestana} ficha={ficha} />}
      </Pestanas>

      <FormularioAplicativo
        edicion={edicion}
        puedeGestionar={ficha.permisos.gestionar}
        alCerrar={() => setEdicion(null)}
        alGuardar={async (solicitud) => {
          if (edicion?.modo === 'editar') {
            const nueva = await actualizar.mutateAsync({ id: ficha.id, solicitud });
            setEdicion(null);
            setAviso({ tono: 'exito', texto: 'Cambios guardados.' });
            if (nueva.slug !== ficha.slug) void navegar(`/catalogo/${nueva.slug}`, { replace: true });
            return null;
          }
          const nueva = await crear.mutateAsync(solicitud);
          setEdicion(null);
          void navegar(`/catalogo/${nueva.slug}`);
          return null;
        }}
      />
      <Hoja abierta={eliminando} alCerrar={() => setEliminando(false)} titulo="Eliminar aplicativo">
        <div className="flex flex-col gap-4 px-4 pt-2 pb-5 text-sm">
          <p>
            ¿Eliminar <strong>{ficha.nombre}</strong> del catálogo? La semilla no lo vuelve a crear. Si solo dejó de usarse, márcalo como
            retirado.
          </p>
          <div className="flex flex-wrap justify-end gap-2">
            <Boton variante="secundario" onClick={() => setEliminando(false)}>
              Cancelar
            </Boton>
            <Boton
              onClick={() => {
                setEliminando(false);
                eliminar.mutateAsync(ficha.id).then(
                  () => void navegar(ficha.ancestros.length > 0 ? `/catalogo/${ficha.ancestros.at(-1)!.slug}` : '/catalogo', { replace: true }),
                  (error: unknown) => setAviso(avisoDeError(error, 'No se pudo eliminar.')),
                );
              }}
            >
              Eliminar
            </Boton>
          </div>
        </div>
      </Hoja>
    </div>
  );
}

/**
 * Pestañas con el patrón de ARIA (flechas, Inicio y Fin). En el móvil se acomodan en varias líneas en lugar de
 * desplazarse de lado: nada de scroll horizontal a 360 px.
 */
function Pestanas({
  pestanas,
  activa,
  alElegir,
  children,
}: {
  pestanas: Pestana[];
  activa: string;
  alElegir: (id: string) => void;
  children: React.ReactNode;
}) {
  const id = useId();
  const botones = useRef(new Map<string, HTMLButtonElement>());

  function alPresionar(evento: KeyboardEvent<HTMLDivElement>) {
    const indice = pestanas.findIndex((p) => p.id === activa);
    const mover: Record<string, number> = { ArrowRight: indice + 1, ArrowLeft: indice - 1, Home: 0, End: pestanas.length - 1 };
    if (!(evento.key in mover)) return;
    evento.preventDefault();
    const siguiente = pestanas[(mover[evento.key]! + pestanas.length) % pestanas.length]!;
    alElegir(siguiente.id);
    botones.current.get(siguiente.id)?.focus();
  }

  return (
    <div className="flex flex-col gap-3">
      <div role="tablist" aria-label="Secciones de la ficha" onKeyDown={alPresionar} className="flex flex-wrap gap-2">
        {pestanas.map((p) => (
          <button
            key={p.id}
            ref={(el) => {
              if (el) botones.current.set(p.id, el);
              else botones.current.delete(p.id);
            }}
            type="button"
            role="tab"
            id={`${id}-${p.id}`}
            aria-selected={p.id === activa}
            aria-controls={`${id}-panel`}
            tabIndex={p.id === activa ? 0 : -1}
            onClick={() => alElegir(p.id)}
            className={cn(
              'inline-flex min-h-toque items-center gap-1.5 rounded-xl border px-3 text-sm',
              p.id === activa ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card hover:bg-atlas-activo',
            )}
          >
            {p.titulo}
            {p.tipo === 'proximamente' && <Hourglass className="size-3.5 text-atlas-texto-suave" aria-label="próximamente" />}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`${id}-panel`} aria-labelledby={`${id}-${activa}`} className="min-w-0">
        {children}
      </div>
    </div>
  );
}

function Dato({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-atlas-texto-suave">{titulo}</dt>
      <dd className="mt-0.5 text-sm break-words">{children}</dd>
    </div>
  );
}

function Resumen({ ficha }: { ficha: FichaAplicativo }) {
  const enlaceRepo = ficha.repo ? `https://github.com/${ficha.repo}${ficha.rutaEnRepo ? `/tree/HEAD/${ficha.rutaEnRepo}` : ''}` : null;
  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <Tarjeta titulo="Descripción" className="lg:col-span-2">
        {ficha.descripcion ? (
          <p className="text-sm whitespace-pre-line">
            <TextoConMarca texto={ficha.descripcion} />
          </p>
        ) : (
          <p className="text-sm text-atlas-texto-suave">Sin descripción todavía.</p>
        )}
        <dl className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Dato titulo="Repo">
            {enlaceRepo ? (
              <a href={enlaceRepo} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-atlas-tinta hover:underline">
                {ficha.repo}
                {ficha.rutaEnRepo && `/${ficha.rutaEnRepo}`}
                <ExternalLink className="size-3.5" aria-label="(se abre en otra pestaña)" />
              </a>
            ) : (
              '—'
            )}
          </Dato>
          <Dato titulo="Tecnología">{ficha.tecnologias.length > 0 ? ficha.tecnologias.join(' · ') : '—'}</Dato>
          <Dato titulo="Ambientes">{ficha.ambientes.length > 0 ? ficha.ambientes.join(' · ') : '—'}</Dato>
          <Dato titulo="Datos personales">{ficha.manejaDatosPersonales ? 'Sí, trata datos de personas' : 'No'}</Dato>
          <Dato titulo="Etiquetas">{ficha.etiquetas.length > 0 ? ficha.etiquetas.join(', ') : '—'}</Dato>
          <Dato titulo="Actualizado">{formatearFecha(new Date(ficha.actualizadoEn))}</Dato>
        </dl>
      </Tarjeta>
      <Tarjeta titulo={ficha.tipo === 'producto' ? 'Módulos y servicios' : ficha.tipo === 'modulo' ? 'Servicios' : 'Componentes'}>
        {ficha.hijos.length === 0 ? (
          <p className="text-sm text-atlas-texto-suave">{ficha.tipo === 'servicio' ? 'Un servicio no tiene hijos en el catálogo.' : 'Todavía no tiene hijos.'}</p>
        ) : (
          <ul className="flex flex-col gap-1">
            {ficha.hijos.map((h) => (
              <li key={h.id}>
                <Link to={`/catalogo/${h.slug}`} className="flex min-h-toque min-w-0 items-center gap-2 rounded-lg px-2 text-sm hover:bg-atlas-activo">
                  <span className="min-w-0 flex-1 truncate font-medium">{h.nombre}</span>
                  <span className="shrink-0 text-xs text-atlas-texto-suave">{TIPOS[h.tipo]}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Tarjeta>
    </div>
  );
}

function Proximamente({ espacio }: { espacio: EspacioFicha }) {
  return (
    <Tarjeta titulo={espacio.titulo}>
      <p className="text-sm text-atlas-texto-suave">{espacio.descripcion}</p>
      <p className="mt-3 inline-flex items-center gap-2 text-sm">
        <Hourglass className="size-4 text-atlas-tinta" aria-hidden="true" />
        Llega con el módulo {espacio.modulo} (fase {espacio.fase}).
      </p>
    </Tarjeta>
  );
}

function PestanaDeModulo({ pestana, ficha }: { pestana: PestanaResuelta; ficha: FichaAplicativo }) {
  const Contenido = pestanaDiferida(pestana.cargar);
  const aplicativo: AplicativoDeFicha = { id: ficha.id, slug: ficha.slug, nombre: ficha.nombre, tipo: ficha.tipo, grupo: ficha.grupo };
  return (
    <Suspense fallback={<div role="status" aria-label={`Cargando ${pestana.titulo}`} className="h-32 animate-pulse rounded-2xl bg-card" />}>
      <Contenido aplicativo={aplicativo} />
    </Suspense>
  );
}
