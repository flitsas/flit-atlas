import { ArrowLeft, ChevronRight, Plus } from 'lucide-react';
import { lazy, Suspense, useState } from 'react';

import { BotonCompartir } from '../../../app/ui/Compartir';
import { formatearFechaHora } from '../../../app/ui/formato';
import { Boton, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import {
  type CamposGuia,
  camposDe,
  crearGuia,
  type GuiaDetalle,
  type GuiaResumen,
  guardarGuia,
  publicarGuia,
  TEXTO_VISIBILIDAD,
  textoVersiones,
  useCambioGuia,
  useGuia,
  useGuias,
} from './api';
import { type Aviso, avisoDeError, Cargando, MostrarAviso, VistaMarkdown } from './componentes';
import { Historial } from './Historial';

// El editor (con su vista previa) en su propio chunk: quien solo lee no lo descarga.
const Editor = lazy(() => import('./Editor'));

const NUEVA: CamposGuia = { titulo: '', contenido: '', operacion: '', aplicaDesde: '', aplicaHasta: '', visibilidad: 'interna', nota: '' };

type Modo = { tipo: 'lista' } | { tipo: 'nueva' } | { tipo: 'ver' | 'editar' | 'historial'; guiaId: string };

/**
 * Guías de integración de un contrato (HU 3.7, plan 00 §8.9 «Guía», ATLAS-ADR-0040): lista, lectura con el Markdown
 * sanitizado, editor con vista previa, historial de revisiones, comparación y restauración, y publicación (aprobación del
 * responsable si es compartible). La pantalla del contrato (HU 3.6) la monta en su pestaña «Guías» con import perezoso;
 * `/contratos/:contratoId/guias/:guiaId` la abre en una guía. Móvil primero: una sola columna y botones de 44 px.
 *
 * @param version Si se da, solo las guías que aplican a esa versión del contrato.
 * @param guiaId Guía abierta al cargar (enlace directo).
 */
export default function GuiasDelContrato({ contratoId, version, guiaId }: { contratoId: string; version?: string; guiaId?: string }) {
  const [modo, setModo] = useState<Modo>(guiaId ? { tipo: 'ver', guiaId } : { tipo: 'lista' });
  const [aviso, setAviso] = useState<Aviso>(null);
  const lista = useGuias(contratoId, version);
  const ir = (siguiente: Modo, nuevoAviso: Aviso = null) => {
    setAviso(nuevoAviso);
    setModo(siguiente);
  };

  if (modo.tipo === 'nueva') {
    return <NuevaGuia contratoId={contratoId} alTerminar={(d) => ir(d ? { tipo: 'ver', guiaId: d.guia.id } : { tipo: 'lista' }, d && { tono: 'exito', texto: 'Guía creada en borrador (revisión 1).' })} />;
  }
  if (modo.tipo !== 'lista') {
    return <Guia contratoId={contratoId} modo={modo} aviso={aviso} ir={ir} />;
  }

  return (
    <section aria-label="Guías del contrato" className="flex min-w-0 flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-lg font-semibold">Guías</h2>
        {lista.data?.puedeEditar && (
          <Boton onClick={() => ir({ tipo: 'nueva' })}>
            <Plus className="size-4" aria-hidden="true" /> Nueva guía
          </Boton>
        )}
      </div>
      <MostrarAviso aviso={aviso} />
      {lista.isPending && <Cargando etiqueta="Cargando las guías" />}
      {lista.isError && <p role="alert">No pudimos cargar las guías. Revisa tu conexión e inténtalo de nuevo.</p>}
      {lista.data?.elementos.length === 0 && (
        <p className="rounded-2xl border border-border bg-card p-6 text-center text-sm text-atlas-texto-suave">
          {version ? `Ninguna guía aplica a la versión ${version}.` : 'Este contrato todavía no tiene guías.'}
          {lista.data.puedeEditar && ' Crea la primera con «Nueva guía».'}
        </p>
      )}
      {lista.data && lista.data.elementos.length > 0 && (
        <ul className="flex flex-col gap-2">
          {lista.data.elementos.map((g) => (
            <li key={g.id}>
              <button
                type="button"
                onClick={() => ir({ tipo: 'ver', guiaId: g.id })}
                className="flex min-h-toque w-full items-start gap-2 rounded-2xl border border-border bg-card p-4 text-left shadow-tarjeta hover:bg-atlas-activo"
              >
                <span className="min-w-0 flex-1">
                  <span className="block font-semibold break-words">{g.titulo}</span>
                  <InsigniasGuia guia={g} />
                </span>
                <ChevronRight className="size-4 shrink-0 self-center text-atlas-texto-suave" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function InsigniasGuia({ guia: g }: { guia: GuiaResumen }) {
  return (
    <span className="mt-2 flex flex-wrap gap-1.5">
      <Insignia tono={g.estado === 'publicada' ? 'exito' : 'neutral'}>{g.estado === 'publicada' ? 'Publicada' : 'Borrador'}</Insignia>
      <Insignia tono={g.visibilidad === 'compartible' ? 'info' : 'neutral'}>{TEXTO_VISIBILIDAD[g.visibilidad]}</Insignia>
      <Insignia tono="neutral">{textoVersiones(g)}</Insignia>
      {g.operacion && <Insignia tono="neutral">{g.operacion}</Insignia>}
      <Insignia tono="neutral">Revisión {g.revisionActual}</Insignia>
    </span>
  );
}

function NuevaGuia({ contratoId, alTerminar }: { contratoId: string; alTerminar: (detalle: GuiaDetalle | null) => void }) {
  const [aviso, setAviso] = useState<Aviso>(null);
  const crear = useCambioGuia((campos: CamposGuia) => crearGuia(contratoId, campos));
  return (
    <Tarjeta titulo="Nueva guía">
      <Suspense fallback={<Cargando etiqueta="Abriendo el editor" alto="h-72" />}>
        <Editor
          inicial={NUEVA}
          titulo="Nueva guía"
          guardando={crear.isPending}
          aviso={aviso}
          alCancelar={() => alTerminar(null)}
          alGuardar={(campos) => crear.mutate(campos, { onSuccess: alTerminar, onError: (e) => setAviso(avisoDeError(e, 'No se pudo crear la guía.')) })}
        />
      </Suspense>
    </Tarjeta>
  );
}

function Guia({
  contratoId,
  modo,
  aviso,
  ir,
}: {
  contratoId: string;
  modo: { tipo: 'ver' | 'editar' | 'historial'; guiaId: string };
  aviso: Aviso;
  ir: (siguiente: Modo, aviso?: Aviso) => void;
}) {
  const detalle = useGuia(modo.guiaId);
  const [error, setError] = useState<Aviso>(null);
  const guardar = useCambioGuia((p: { campos: CamposGuia; base: number }) => guardarGuia(modo.guiaId, p.campos, p.base));
  const publicar = useCambioGuia((revision: number) => publicarGuia(modo.guiaId, revision));

  const volver = (
    <Boton variante="fantasma" className="self-start" onClick={() => ir({ tipo: 'lista' })}>
      <ArrowLeft className="size-4" aria-hidden="true" /> Todas las guías
    </Boton>
  );
  if (detalle.isPending) return <Cargando etiqueta="Cargando la guía" alto="h-48" />;
  if (detalle.isError || !detalle.data) {
    return (
      <div className="flex flex-col gap-2">
        {volver}
        <p role="alert">No pudimos cargar la guía. Puede que ya no exista o que no tengas acceso.</p>
      </div>
    );
  }
  const { guia: g, revision: r, puedeEditar, puedeAprobar } = detalle.data;
  const compartible = g.visibilidad === 'compartible';
  const puedePublicar = g.estado === 'borrador' && (compartible ? puedeAprobar : puedeEditar);

  return (
    <article aria-label={g.titulo} className="flex min-w-0 flex-col gap-3">
      {volver}
      <header className="flex min-w-0 flex-col gap-1">
        <h2 className="text-xl font-semibold break-words">{g.titulo}</h2>
        <InsigniasGuia guia={g} />
        <p className="text-xs text-atlas-texto-suave">
          Revisión {r.numero} de {r.autor.nombre} · <time dateTime={r.guardadaEn}>{formatearFechaHora(new Date(r.guardadaEn))}</time>
          {g.revisionPublicada !== null && g.revisionPublicada !== g.revisionActual && ` · publicada: la ${g.revisionPublicada}`}
          {g.aprobadaPor && ` · aprobó ${g.aprobadaPor.nombre}`}
        </p>
      </header>
      {modo.tipo !== 'editar' && <MostrarAviso aviso={error ?? aviso} />}

      {modo.tipo !== 'editar' && (
        <div className="flex flex-wrap gap-2" role="group" aria-label="Acciones de la guía">
          <Boton variante={modo.tipo === 'ver' ? 'primario' : 'secundario'} aria-pressed={modo.tipo === 'ver'} onClick={() => ir({ tipo: 'ver', guiaId: g.id })}>
            Leer
          </Boton>
          <Boton
            variante={modo.tipo === 'historial' ? 'primario' : 'secundario'}
            aria-pressed={modo.tipo === 'historial'}
            onClick={() => ir({ tipo: 'historial', guiaId: g.id })}
          >
            Historial
          </Boton>
          <BotonCompartir ruta={`/contratos/${encodeURIComponent(contratoId)}/guias/${encodeURIComponent(g.id)}`} titulo={g.titulo} que="esta guía" />
          {puedeEditar && (
            <Boton variante="secundario" onClick={() => ir({ tipo: 'editar', guiaId: g.id })}>
              Editar
            </Boton>
          )}
          {puedePublicar && (
            <Boton
              disabled={publicar.isPending}
              onClick={() =>
                publicar.mutate(g.revisionActual, {
                  onSuccess: () => {
                    setError(null);
                    ir(modo, { tono: 'exito', texto: compartible ? `Revisión ${g.revisionActual} aprobada y publicada.` : `Revisión ${g.revisionActual} publicada.` });
                  },
                  onError: (e) => setError(avisoDeError(e, 'No se pudo publicar la guía.')),
                })
              }
            >
              {compartible ? 'Aprobar y publicar' : 'Publicar'}
            </Boton>
          )}
        </div>
      )}
      {g.estado === 'borrador' && compartible && !puedeAprobar && (
        <p role="note" className="text-sm text-atlas-texto-suave">
          Pendiente de aprobación: la publica el responsable principal o secundario del aplicativo.
        </p>
      )}

      {modo.tipo === 'ver' && (
        <Tarjeta etiqueta="Contenido de la guía">
          <VistaMarkdown texto={r.contenido} etiqueta={`Contenido de ${g.titulo}`} />
        </Tarjeta>
      )}
      {modo.tipo === 'historial' && (
        <Historial
          guiaId={g.id}
          revisionActual={g.revisionActual}
          puedeEditar={puedeEditar}
          alRestaurar={(a) => ir(a?.tono === 'exito' ? { tipo: 'ver', guiaId: g.id } : modo, a)}
        />
      )}
      {modo.tipo === 'editar' && (
        <Tarjeta titulo={`Editar · sobre la revisión ${g.revisionActual}`}>
          <Suspense fallback={<Cargando etiqueta="Abriendo el editor" alto="h-72" />}>
            <Editor
              inicial={camposDe(r)}
              titulo="Editar la guía"
              guardando={guardar.isPending}
              aviso={error}
              alCancelar={() => {
                setError(null);
                ir({ tipo: 'ver', guiaId: g.id });
              }}
              alGuardar={(campos) =>
                guardar.mutate(
                  { campos, base: g.revisionActual },
                  {
                    onSuccess: (d) => {
                      setError(null);
                      ir({ tipo: 'ver', guiaId: g.id }, { tono: 'exito', texto: `Guardada la revisión ${d.revision.numero}.` });
                    },
                    onError: (e) => setError(avisoDeError(e, 'No se pudo guardar la guía.')),
                  },
                )
              }
            />
          </Suspense>
        </Tarjeta>
      )}
    </article>
  );
}
