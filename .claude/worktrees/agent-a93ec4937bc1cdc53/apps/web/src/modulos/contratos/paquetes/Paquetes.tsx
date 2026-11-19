import { Archive, ArrowLeft, ChevronRight, Pencil, Pin, Plus, Send } from 'lucide-react';
import { lazy, Suspense, useState } from 'react';
import { Link, Route, Routes, useLocation, useNavigate, useParams } from 'react-router';

import { ErrorApi } from '../../../app/api';
import { NoEncontrada } from '../../../app/paginas/Estados';
import { formatearFechaHora } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, Tarjeta } from '../../../app/ui/Tarjeta';
import { Etiqueta } from '../componentes/ui';
import { type Aviso, Cargando, MostrarAviso } from '../guias/componentes';
import {
  archivarPaquete,
  crearPaquete,
  type EntradaPaquete,
  fijarRevision,
  type PaqueteDetalle,
  publicarPaquete,
  revisarPaquete,
  type RevisionPaqueteResumen,
  useCambioPaquete,
  usePaquete,
  usePaquetes,
  useRevisionPaquete,
} from './api';
import { Comparticiones } from './Comparticiones';
import { avisoDeError, InsigniasPaquete, Opciones } from './componentes';
import { type EstadoAsistente, estadoDesde, solicitudDe, tieneAlgoQueCompartir, VACIO } from './reglas';

// El asistente en su propio chunk: quien solo lee la lista o el detalle no lo descarga.
const Asistente = lazy(() => import('./Asistente'));

/**
 * Paquetes de contratos (HU 4.1, plan 00 §8.9 «Paquetes», ATLAS-ADR-0041): `/contratos/paquetes` (lista),
 * `/contratos/paquetes/nuevo` (asistente) y `/contratos/paquetes/<id>` (detalle con la última revisión, publicar, el
 * historial con «Fijar esta» y archivar) y, desde la HU 4.3, «Compartido con» (ATLAS-ADR-0043).
 */
export default function Paquetes() {
  return (
    <div className="mx-auto flex max-w-5xl min-w-0 flex-col gap-4">
      <Routes>
        <Route index element={<Lista />} />
        <Route path="nuevo" element={<Nuevo />} />
        <Route path=":paqueteId" element={<Detalle />} />
        <Route path="*" element={<NoEncontrada />} />
      </Routes>
    </div>
  );
}

function Volver({ a, texto }: { a: string; texto: string }) {
  return (
    <Link to={a} className="inline-flex min-h-toque items-center gap-2 self-start text-sm font-medium text-atlas-tinta hover:underline">
      <ArrowLeft className="size-4" aria-hidden="true" /> {texto}
    </Link>
  );
}

// --- Lista ---

function Lista() {
  const [vista, setVista] = useState<'activos' | 'archivados'>('activos');
  const lista = usePaquetes(vista === 'archivados' ? { estado: 'archivado' } : {});
  return (
    <>
      <Volver a="/contratos" texto="Contratos" />
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">Paquetes de contratos</h1>
          <p className="text-sm text-atlas-texto-suave">Selecciones de contratos compartibles, en versiones estables, para compartir con clientes.</p>
        </div>
        {lista.data?.puedeCrear && (
          <Link
            to="/contratos/paquetes/nuevo"
            className="inline-flex min-h-toque items-center gap-2 rounded-xl bg-flit-primary px-4 text-sm font-medium text-white hover:bg-[#1f3a6b] dark:bg-flit-brand dark:text-[#0b1220]"
          >
            <Plus className="size-4" aria-hidden="true" /> Nuevo paquete
          </Link>
        )}
      </header>
      <Opciones
        leyenda="Mostrar"
        nombre="vista-paquetes"
        valor={vista}
        alCambiar={setVista}
        opciones={[
          { valor: 'activos', etiqueta: 'Borradores y publicados' },
          { valor: 'archivados', etiqueta: 'Archivados' },
        ]}
      />
      {lista.isPending && <Cargando etiqueta="Cargando los paquetes" alto="h-40" />}
      {lista.isError && <p role="alert">No pudimos cargar los paquetes. Revisa tu conexión e inténtalo de nuevo.</p>}
      {lista.data?.elementos.length === 0 && (
        <p className="rounded-2xl border border-border bg-card p-6 text-center text-sm text-atlas-texto-suave">
          {vista === 'archivados' ? 'No hay paquetes archivados.' : 'Todavía no hay paquetes.'}
          {vista === 'activos' && lista.data.puedeCrear && ' Arma el primero con «Nuevo paquete».'}
        </p>
      )}
      {lista.data && lista.data.elementos.length > 0 && (
        <ul aria-label="Paquetes" className="flex flex-col gap-2">
          {lista.data.elementos.map((p) => (
            <li key={p.id}>
              <Link
                to={`/contratos/paquetes/${p.id}`}
                className="flex min-h-toque w-full items-start gap-2 rounded-2xl border border-border bg-card p-4 shadow-tarjeta hover:bg-atlas-activo"
              >
                <span className="flex min-w-0 flex-1 flex-col gap-2">
                  <span className="font-semibold break-words">{p.nombre}</span>
                  <InsigniasPaquete paquete={p} />
                </span>
                <ChevronRight className="size-4 shrink-0 self-center text-atlas-texto-suave" aria-hidden="true" />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

// --- Nuevo ---

function Nuevo() {
  const navegar = useNavigate();
  const [aviso, setAviso] = useState<Aviso>(null);
  const crear = useCambioPaquete((estado: EstadoAsistente) => crearPaquete(solicitudDe(estado)));
  return (
    <>
      <Volver a="/contratos/paquetes" texto="Paquetes" />
      <h1 className="text-2xl font-semibold">Nuevo paquete</h1>
      <Suspense fallback={<Cargando etiqueta="Abriendo el asistente" alto="h-72" />}>
        <Asistente
          inicial={VACIO}
          titulo="Asistente del paquete"
          guardando={crear.isPending}
          aviso={aviso}
          alCancelar={() => navegar('/contratos/paquetes')}
          alGuardar={(estado) =>
            crear.mutate(estado, {
              onSuccess: (d) => navegar(`/contratos/paquetes/${d.paquete.id}`, { state: { aviso: 'Paquete creado en borrador (revisión 1).' } }),
              onError: (e) => setAviso(avisoDeError(e, 'No se pudo crear el paquete.')),
            })
          }
        />
      </Suspense>
    </>
  );
}

// --- Detalle ---

function Detalle() {
  const { paqueteId = '' } = useParams();
  const ubicacion = useLocation();
  const inicial = (ubicacion.state as { aviso?: string } | null)?.aviso;
  const detalle = usePaquete(paqueteId);
  const [editando, setEditando] = useState(false);
  const [aviso, setAviso] = useState<Aviso>(inicial ? { tono: 'exito', texto: inicial } : null);
  const [errorAsistente, setErrorAsistente] = useState<Aviso>(null);
  const [confirmando, setConfirmando] = useState<{ tipo: 'archivar' } | { tipo: 'fijar'; numero: number } | null>(null);
  const revisar = useCambioPaquete((p: { estado: EstadoAsistente; base: number }) => revisarPaquete(paqueteId, solicitudDe(p.estado, p.base)));
  const publicar = useCambioPaquete((revision: number) => publicarPaquete(paqueteId, revision));
  const fijar = useCambioPaquete((numero: number) => fijarRevision(paqueteId, numero));
  const archivar = useCambioPaquete(() => archivarPaquete(paqueteId));

  if (detalle.isPending) return <Cargando etiqueta="Cargando el paquete" alto="h-60" />;
  if (detalle.isError || !detalle.data) {
    if (detalle.error instanceof ErrorApi && detalle.error.estado === 404) return <NoEncontrada />;
    return <p role="alert">No pudimos cargar el paquete. Revisa tu conexión e inténtalo de nuevo.</p>;
  }
  const { paquete: p, revision: r, revisiones } = detalle.data;
  const gestionable = p.puedeGestionar && p.estado !== 'archivado';
  const porPublicar = gestionable && p.revisionPublicada !== p.revisionActual;
  const publicable = tieneAlgoQueCompartir(r.definicion);
  const resultado = (texto: string) => (d: PaqueteDetalle) => {
    setConfirmando(null);
    setAviso({ tono: 'exito', texto: texto.replace('{n}', String(d.paquete.revisionPublicada ?? d.paquete.revisionActual)) });
  };
  const fallo = (porDefecto: string) => (e: unknown) => {
    setConfirmando(null);
    setAviso(avisoDeError(e, porDefecto));
  };

  if (editando) {
    return (
      <>
        <Boton variante="fantasma" className="self-start" onClick={() => setEditando(false)}>
          <ArrowLeft className="size-4" aria-hidden="true" /> {p.nombre}
        </Boton>
        <h1 className="text-2xl font-semibold break-words">Nueva revisión · sobre la {p.revisionActual}</h1>
        <Suspense fallback={<Cargando etiqueta="Abriendo el asistente" alto="h-72" />}>
          <Asistente
            inicial={estadoDesde(r)}
            titulo="Asistente del paquete"
            guardando={revisar.isPending}
            aviso={errorAsistente}
            alCancelar={() => {
              setErrorAsistente(null);
              setEditando(false);
            }}
            alGuardar={(estado) =>
              revisar.mutate(
                { estado, base: p.revisionActual },
                {
                  onSuccess: (d) => {
                    setErrorAsistente(null);
                    setEditando(false);
                    setAviso({ tono: 'exito', texto: `Guardada la revisión ${d.paquete.revisionActual}. Publícala para que sea la vigente.` });
                  },
                  onError: (e) => setErrorAsistente(avisoDeError(e, 'No se pudo guardar la revisión.')),
                },
              )
            }
          />
        </Suspense>
      </>
    );
  }

  return (
    <>
      <Volver a="/contratos/paquetes" texto="Paquetes" />
      <header className="flex min-w-0 flex-col gap-3 rounded-3xl bg-portada p-5 text-white shadow-tarjeta sm:p-6">
        <p className="text-sm text-white/85">Paquete de contratos</p>
        <h1 className="text-2xl font-semibold break-words sm:text-3xl">{p.nombre}</h1>
        {p.descripcion && <p className="max-w-3xl text-sm break-words text-white/85">{p.descripcion}</p>}
        {p.publicadaPor && p.publicadaEn && (
          <p className="text-xs text-white/85">
            Vigente desde <time dateTime={p.publicadaEn}>{formatearFechaHora(new Date(p.publicadaEn))}</time> · {p.publicadaPor.nombre}
          </p>
        )}
      </header>
      <InsigniasPaquete paquete={p} />
      <MostrarAviso aviso={aviso} />

      {gestionable && (
        <div className="flex flex-wrap gap-2" role="group" aria-label="Acciones del paquete">
          <Boton
            variante="secundario"
            onClick={() => {
              setAviso(null);
              setEditando(true);
            }}
          >
            <Pencil className="size-4" aria-hidden="true" /> Editar (nueva revisión)
          </Boton>
          {porPublicar && (
            <Boton
              disabled={!publicable || publicar.isPending}
              onClick={() => publicar.mutate(p.revisionActual, { onSuccess: resultado('Publicada: la revisión {n} es la vigente.'), onError: fallo('No se pudo publicar.') })}
            >
              <Send className="size-4" aria-hidden="true" /> Publicar la revisión {p.revisionActual}
            </Boton>
          )}
          <Boton variante="fantasma" onClick={() => setConfirmando({ tipo: 'archivar' })}>
            <Archive className="size-4" aria-hidden="true" /> Archivar
          </Boton>
        </div>
      )}
      {porPublicar && !publicable && (
        <p role="note" className="text-sm text-atlas-texto-suave">
          Para publicar, la revisión {p.revisionActual} necesita al menos un contrato con una operación.
        </p>
      )}

      {p.estado !== 'borrador' && <Comparticiones paqueteId={p.id} revisiones={revisiones.map((x) => x.numero).sort((a, b) => a - b)} vigente={p.revisionPublicada} />}

      <Tarjeta titulo={`Revisión ${r.numero}${r.vigente ? ' · vigente' : ''}`}>
        <p className="mb-3 text-xs text-atlas-texto-suave">
          {r.autor.nombre} · <time dateTime={r.guardadaEn}>{formatearFechaHora(new Date(r.guardadaEn))}</time> · {r.nota}
        </p>
        <Definicion definicion={r.definicion} />
      </Tarjeta>

      <Historial
        paqueteId={p.id}
        revisiones={revisiones}
        revisionActual={p.revisionActual}
        puedeFijar={gestionable}
        alFijar={(numero) => setConfirmando({ tipo: 'fijar', numero })}
      />

      <Hoja
        abierta={confirmando !== null}
        alCerrar={() => setConfirmando(null)}
        titulo={confirmando?.tipo === 'fijar' ? `Fijar la revisión ${confirmando.numero}` : 'Archivar el paquete'}
      >
        <div className="flex flex-col gap-3 px-4 pt-2 pb-5 text-sm">
          {confirmando?.tipo === 'fijar' ? (
            <>
              <p>
                La revisión {confirmando.numero} vuelve a ser la vigente (la que verán los clientes). Las revisiones más nuevas no se borran: puedes publicar la
                última cuando quieras.
              </p>
              <Boton
                disabled={fijar.isPending}
                onClick={() => fijar.mutate(confirmando.numero, { onSuccess: resultado('Fijada: la revisión {n} es la vigente.'), onError: fallo('No se pudo fijar la revisión.') })}
              >
                <Pin className="size-4" aria-hidden="true" /> Fijar la revisión {confirmando.numero}
              </Boton>
            </>
          ) : (
            <>
              <p>El paquete deja de admitir revisiones y publicaciones. Su historia queda.</p>
              <Boton
                disabled={archivar.isPending}
                onClick={() => archivar.mutate(undefined, { onSuccess: resultado('Paquete archivado.'), onError: fallo('No se pudo archivar el paquete.') })}
              >
                <Archive className="size-4" aria-hidden="true" /> Archivar
              </Boton>
            </>
          )}
          <Boton variante="secundario" onClick={() => setConfirmando(null)}>
            Cancelar
          </Boton>
        </div>
      </Hoja>
    </>
  );
}

/** Contratos de una revisión: versión fijada, operaciones y guías. */
function Definicion({ definicion }: { definicion: EntradaPaquete[] }) {
  if (definicion.length === 0) return <p className="text-sm">Esta revisión no tiene contratos.</p>;
  return (
    <ul aria-label="Contratos de la revisión" className="flex flex-col gap-3">
      {definicion.map((e) => (
        <li key={e.contratoId} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            {e.codigo ? (
              <Link to={`/contratos/${e.contratoId}/v/${encodeURIComponent(e.version ?? '')}`} className="font-semibold break-words text-atlas-tinta hover:underline">
                {e.nombre ?? e.codigo}
              </Link>
            ) : (
              <span className="font-semibold">Contrato que ya no existe</span>
            )}
            <Etiqueta>Versión {e.version ?? '—'}</Etiqueta>
            {e.estadoVersion === 'deprecada' && <Etiqueta tono="alerta">Deprecada</Etiqueta>}
            {e.estadoVersion === 'retirada' && <Etiqueta tono="peligro">Retirada</Etiqueta>}
            {e.visibilidad === 'interna' && <Etiqueta tono="peligro">Ya no es compartible</Etiqueta>}
          </div>
          <p className="text-xs break-words text-atlas-texto-suave">
            {e.aplicativo?.nombre ?? e.aplicativo?.slug ?? ''} · {e.codigo}
          </p>
          <details className="min-w-0">
            <summary className="flex min-h-toque cursor-pointer items-center text-sm font-medium">
              {e.operaciones.length} {e.operaciones.length === 1 ? 'operación' : 'operaciones'}
            </summary>
            <ul className="flex flex-col gap-1 pl-2">
              {e.operaciones.map((o) => (
                <li key={o}>
                  <code className="text-sm break-all">{o}</code>
                </li>
              ))}
            </ul>
          </details>
          {e.guias.length > 0 && (
            <p className="text-sm break-words">
              Guías: {e.guias.map((g) => `${g.titulo ?? 'Guía que ya no existe'} (revisión ${g.revision})`).join(' · ')}
            </p>
          )}
        </li>
      ))}
    </ul>
  );
}

function Historial({
  paqueteId,
  revisiones,
  revisionActual,
  puedeFijar,
  alFijar,
}: {
  paqueteId: string;
  revisiones: RevisionPaqueteResumen[];
  revisionActual: number;
  puedeFijar: boolean;
  alFijar: (numero: number) => void;
}) {
  const [abierta, setAbierta] = useState<number | null>(null);
  const revision = useRevisionPaquete(paqueteId, abierta);
  return (
    <Tarjeta titulo="Historial de revisiones">
      <ol aria-label="Revisiones del paquete" className="flex flex-col gap-3">
        {revisiones.map((r) => (
          <li key={r.numero} aria-label={`Revisión ${r.numero}`} className="flex min-w-0 flex-col gap-2 rounded-xl border border-border p-3">
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-semibold">Revisión {r.numero}</p>
              {r.numero === revisionActual && <Etiqueta tono="info">Última</Etiqueta>}
              {r.vigente && <Etiqueta tono="exito">Vigente</Etiqueta>}
            </div>
            <p className="text-sm break-words">{r.nota}</p>
            <p className="text-xs text-atlas-texto-suave">
              {r.autor.nombre} · <time dateTime={r.guardadaEn}>{formatearFechaHora(new Date(r.guardadaEn))}</time> · {r.contratos}{' '}
              {r.contratos === 1 ? 'contrato' : 'contratos'} · {r.operaciones} {r.operaciones === 1 ? 'operación' : 'operaciones'} · {r.guias}{' '}
              {r.guias === 1 ? 'guía' : 'guías'}
            </p>
            <div className="flex flex-wrap gap-2">
              <Boton variante="secundario" aria-expanded={abierta === r.numero} onClick={() => setAbierta(abierta === r.numero ? null : r.numero)}>
                {abierta === r.numero ? 'Ocultar' : 'Ver'}
              </Boton>
              {puedeFijar && !r.vigente && r.numero < revisionActual && (
                <Boton variante="secundario" onClick={() => alFijar(r.numero)}>
                  <Pin className="size-4" aria-hidden="true" /> Fijar esta
                </Boton>
              )}
            </div>
            {abierta === r.numero && (
              <div className="min-w-0">
                {revision.isPending && <Cargando etiqueta={`Cargando la revisión ${r.numero}`} />}
                {revision.isError && <p role="alert">No pudimos cargar la revisión.</p>}
                {revision.data && <Definicion definicion={revision.data.definicion} />}
              </div>
            )}
          </li>
        ))}
      </ol>
    </Tarjeta>
  );
}
