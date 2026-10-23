import { ChevronRight, Plus, Search, SlidersHorizontal, X } from 'lucide-react';
import { useDeferredValue, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';

import { useSesion } from '../../../app/sesion';
import { Icono } from '../../../app/ui/Icono';
import { Boton } from '../../../app/ui/Tarjeta';
import { type AplicativoResumen, type Criticidad, type Estado, type FiltroCatalogo, type Grupo, type Tipo, useAplicativos, useCambiosCatalogo } from '../api';
import { type Aviso, Insignias, MostrarAviso, Opciones } from '../componentes';
import { FormularioAplicativo } from '../Formulario';
import { moduloCatalogo } from '../modulo';
import { CRITICIDADES, ESTADOS, GRUPOS, opciones, TIPOS } from '../textos';

const FILTROS = ['grupo', 'estado', 'criticidad', 'tipo'] as const;

/**
 * Catálogo de aplicativos (HU 2.1): búsqueda y filtros por grupo, estado, criticidad y tipo, guardados en la URL para
 * compartirlos. Los resultados van en tarjetas, agrupados por grupo y en el orden de la jerarquía; en el móvil los
 * filtros se pliegan para no tapar la lista.
 */
export default function Lista() {
  const { acceso } = useSesion();
  const navegar = useNavigate();
  const [parametros, setParametros] = useSearchParams();
  const [texto, setTexto] = useState(parametros.get('texto') ?? '');
  const textoDiferido = useDeferredValue(texto.trim());
  const [creando, setCreando] = useState(false);
  const [aviso, setAviso] = useState<Aviso>(null);
  const { crear } = useCambiosCatalogo();

  const filtro: FiltroCatalogo = {
    texto: textoDiferido || undefined,
    grupo: (parametros.get('grupo') as Grupo | null) ?? undefined,
    estado: (parametros.get('estado') as Estado | null) ?? undefined,
    criticidad: (parametros.get('criticidad') as Criticidad | null) ?? undefined,
    tipo: (parametros.get('tipo') as Tipo | null) ?? undefined,
  };
  const lista = useAplicativos(filtro);
  const activos = FILTROS.filter((f) => parametros.get(f));
  const puedeCrear = acceso.permisos?.includes('atlas.catalogo.gestionar') ?? false;

  function cambiar(clave: (typeof FILTROS)[number] | 'texto', valor: string) {
    setParametros(
      (actual) => {
        const siguiente = new URLSearchParams(actual);
        if (valor) siguiente.set(clave, valor);
        else siguiente.delete(clave);
        return siguiente;
      },
      { replace: true },
    );
  }

  const grupos = useMemo(() => {
    const mapa = new Map<Grupo, AplicativoResumen[]>();
    for (const a of lista.data?.elementos ?? []) mapa.set(a.grupo, [...(mapa.get(a.grupo) ?? []), a]);
    return [...mapa.entries()];
  }, [lista.data]);

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-4">
      <header className="flex flex-wrap items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloCatalogo.icono} className="size-6" />
        </span>
        <div className="min-w-0 flex-1">
          <h1 className="text-2xl font-semibold">Catálogo de aplicativos</h1>
          <p className="max-w-3xl text-sm text-atlas-texto-suave">{moduloCatalogo.descripcion}</p>
        </div>
        {puedeCrear && (
          <Boton onClick={() => setCreando(true)}>
            <Plus className="size-4" aria-hidden="true" />
            Nuevo producto
          </Boton>
        )}
      </header>
      <MostrarAviso aviso={aviso} />

      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-4 size-5 -translate-y-1/2 text-atlas-texto-suave" aria-hidden="true" />
        <input
          type="search"
          aria-label="Buscar en el catálogo"
          placeholder="Busca por nombre, repo, descripción o etiqueta"
          enterKeyHint="search"
          maxLength={200}
          value={texto}
          onChange={(e) => {
            setTexto(e.target.value);
            cambiar('texto', e.target.value.trim());
          }}
          className="min-h-12 w-full rounded-xl border border-border bg-card pr-4 pl-12 text-base outline-none placeholder:text-atlas-texto-suave focus-visible:border-atlas-tinta"
        />
      </div>

      <details className="group rounded-2xl border border-border bg-card shadow-tarjeta" open={activos.length > 0 || undefined}>
        <summary className="flex min-h-toque cursor-pointer list-none items-center gap-2 px-4 text-sm font-medium">
          <SlidersHorizontal className="size-4" aria-hidden="true" />
          Filtros{activos.length > 0 && ` (${activos.length})`}
          <ChevronRight className="ml-auto size-4 transition-transform group-open:rotate-90" aria-hidden="true" />
        </summary>
        <div className="flex flex-col gap-4 border-t border-border px-4 pt-3 pb-4">
          <Opciones leyenda="Grupo" valor={filtro.grupo ?? ''} opciones={[{ valor: '', etiqueta: 'Todos' }, ...opciones(GRUPOS)]} alCambiar={(v) => cambiar('grupo', v)} />
          <Opciones leyenda="Estado" valor={filtro.estado ?? ''} opciones={[{ valor: '', etiqueta: 'Todos' }, ...opciones(ESTADOS)]} alCambiar={(v) => cambiar('estado', v)} />
          <Opciones
            leyenda="Criticidad"
            valor={filtro.criticidad ?? ''}
            opciones={[{ valor: '', etiqueta: 'Todas' }, ...opciones(CRITICIDADES)]}
            alCambiar={(v) => cambiar('criticidad', v)}
          />
          <Opciones leyenda="Tipo" valor={filtro.tipo ?? ''} opciones={[{ valor: '', etiqueta: 'Todos' }, ...opciones(TIPOS)]} alCambiar={(v) => cambiar('tipo', v)} />
          {activos.length > 0 && (
            <Boton variante="fantasma" className="self-start" onClick={() => setParametros(texto.trim() ? { texto: texto.trim() } : {}, { replace: true })}>
              <X className="size-4" aria-hidden="true" />
              Quitar filtros
            </Boton>
          )}
        </div>
      </details>

      {lista.isPending && <div role="status" aria-label="Cargando el catálogo" className="h-40 animate-pulse rounded-2xl bg-atlas-activo" />}
      {lista.isError && (
        <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
          No pudimos cargar el catálogo. Revisa tu conexión e inténtalo de nuevo.
        </p>
      )}
      {lista.data && (
        <p role="status" className="text-sm text-atlas-texto-suave">
          {lista.data.total === 1 ? '1 aplicativo' : `${lista.data.total} aplicativos`}
          {lista.data.total > lista.data.elementos.length && ` (se muestran ${lista.data.elementos.length}; afina la búsqueda)`}
        </p>
      )}
      {lista.data?.total === 0 && (
        <p className="rounded-2xl border border-border bg-card p-6 text-center text-sm text-atlas-texto-suave">
          Nada coincide con la búsqueda y los filtros. Prueba con menos palabras o quita filtros.
        </p>
      )}
      {grupos.map(([grupo, aplicativos]) => (
        <section key={grupo} aria-labelledby={`grupo-${grupo}`} className="flex flex-col gap-2">
          <h2 id={`grupo-${grupo}`} className="px-1 text-sm font-semibold tracking-wide text-atlas-texto-suave uppercase">
            {GRUPOS[grupo]} <span className="font-normal normal-case">· {aplicativos.length}</span>
          </h2>
          <ul className="flex flex-col gap-2">
            {aplicativos.map((a) => (
              <li key={a.id}>
                <TarjetaAplicativo aplicativo={a} />
              </li>
            ))}
          </ul>
        </section>
      ))}

      <FormularioAplicativo
        edicion={creando ? { modo: 'crear', padre: null } : null}
        puedeGestionar={puedeCrear}
        alCerrar={() => setCreando(false)}
        alGuardar={async (solicitud) => {
          const ficha = await crear.mutateAsync(solicitud);
          setCreando(false);
          setAviso({ tono: 'exito', texto: `Se creó ${ficha.nombre}.` });
          void navegar(`/catalogo/${ficha.slug}`);
          return null;
        }}
      />
    </div>
  );
}

/** Una fila del catálogo: el nombre enlaza a la ficha; la ruta en la jerarquía va debajo, como contexto. */
function TarjetaAplicativo({ aplicativo: a }: { aplicativo: AplicativoResumen }) {
  return (
    <Link
      to={`/catalogo/${a.slug}`}
      className="flex min-w-0 flex-col gap-2 rounded-2xl border border-border bg-card p-4 shadow-tarjeta transition-colors hover:border-atlas-tinta"
    >
      <span className="flex min-w-0 items-start gap-2">
        <span className="min-w-0 flex-1">
          {a.ancestros.length > 0 && (
            <span className="block truncate text-xs text-atlas-texto-suave">{a.ancestros.map((p) => p.nombre).join(' › ')}</span>
          )}
          <span className="block truncate font-semibold">{a.nombre}</span>
        </span>
        {a.hijos > 0 && (
          <span className="shrink-0 text-xs text-atlas-texto-suave">
            {a.hijos} {a.hijos === 1 ? 'hijo' : 'hijos'}
          </span>
        )}
        <ChevronRight className="size-4 shrink-0 self-center text-atlas-texto-suave" aria-hidden="true" />
      </span>
      <Insignias aplicativo={a} />
      {a.descripcion && <span className="line-clamp-2 text-sm text-atlas-texto-suave">{a.descripcion}</span>}
      {a.tecnologias.length > 0 && <span className="truncate text-xs text-atlas-texto-suave">{a.tecnologias.join(' · ')}</span>}
    </Link>
  );
}
