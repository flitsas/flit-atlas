import { ArrowLeft } from 'lucide-react';
import { lazy, Suspense, useCallback, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';

import { DiagramaMermaid } from '../../../app/diagramas/DiagramaMermaid';
import { VisorBpmn } from '../../../app/diagramas/VisorBpmn';
import { Boton } from '../../../app/ui/Tarjeta';
import {
  type AplicativoRef,
  crearDiagrama,
  FUENTE_INICIAL,
  guardarDiagrama,
  mensajeDeError,
  TEXTO_TIPO,
  TEXTO_VISIBILIDAD,
  type TipoDiagrama,
  useAplicativos,
  useCambioDiagrama,
  useDiagrama,
  type VisibilidadDiagrama,
} from '../api';
import { Aviso, Cargando, claseCampo, Opciones } from '../componentes';

// React Flow y sus estilos en su propio chunk: solo se descargan al editar un lienzo.
const EditorLienzo = lazy(() => import('../EditorLienzo').then((m) => ({ default: m.EditorLienzo })));

const TIPOS = Object.keys(TEXTO_TIPO) as TipoDiagrama[];
const VISIBILIDADES = Object.keys(TEXTO_VISIBILIDAD) as VisibilidadDiagrama[];

function EditorFuente({ tipo, fuente, aplicativos, alCambiar }: { tipo: TipoDiagrama; fuente: string; aplicativos: AplicativoRef[]; alCambiar: (f: string) => void }) {
  if (tipo === 'lienzo') {
    return (
      <Suspense fallback={<Cargando etiqueta="Preparando el lienzo" alto="h-96" />}>
        <EditorLienzo fuente={fuente} aplicativos={aplicativos} alCambiar={alCambiar} />
      </Suspense>
    );
  }
  if (tipo === 'bpmn') return <VisorBpmn xml={fuente} titulo="Editor del proceso BPMN" editable alCambiar={alCambiar} />;
  return (
    <div className="grid min-w-0 gap-3 lg:grid-cols-2">
      <label className="flex min-w-0 flex-col text-sm font-medium">
        Fuente en Mermaid
        <textarea required maxLength={20_000} rows={14} spellCheck={false} value={fuente} onChange={(e) => alCambiar(e.target.value)} className={`${claseCampo} py-2 font-mono text-sm`} />
      </label>
      <div className="min-w-0">
        <p className="text-sm font-medium">Vista previa</p>
        <DiagramaMermaid fuente={fuente} />
      </div>
    </div>
  );
}

/** Crear un diagrama (HU 6.6 y 6.7). El código y el tipo no cambian después: el código es lo que citan los documentos. */
export function NuevoDiagrama() {
  const navegar = useNavigate();
  const cambio = useCambioDiagrama();
  const aplicativos = useAplicativos();
  const [codigo, setCodigo] = useState('');
  const [aplicativoId, setAplicativoId] = useState<string | null>(null);
  const [tipo, setTipo] = useState<TipoDiagrama>('lienzo');
  const [titulo, setTitulo] = useState('');
  const [visibilidad, setVisibilidad] = useState<VisibilidadDiagrama>('interno_negocio');
  const [nota, setNota] = useState('Primera versión');
  const [fuentes, setFuentes] = useState<Partial<Record<TipoDiagrama, string>>>({});
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const alCambiar = useCallback((f: string) => setFuentes((a) => ({ ...a, [tipo]: f })), [tipo]);
  const fuente = fuentes[tipo] ?? FUENTE_INICIAL[tipo];
  const completo = /^[a-z0-9][a-z0-9-]*$/.test(codigo) && aplicativoId && titulo.trim() && nota.trim();
  const textosAplicativo = Object.fromEntries((aplicativos.data ?? []).map((a) => [a.id, a.nombre]));

  return (
    <form
      aria-label="Nuevo diagrama"
      className="flex min-w-0 flex-col gap-4"
      onSubmit={(e) => {
        e.preventDefault();
        if (!completo) return;
        setGuardando(true);
        setError(null);
        crearDiagrama({ codigo, aplicativoId, tipo, titulo, visibilidad, fuente, nota })
          .then(async (d) => {
            await cambio();
            await navegar(`/diagramas/${d.diagrama.id}`);
          })
          .catch((motivo: unknown) => setError(mensajeDeError(motivo)))
          .finally(() => setGuardando(false));
      }}
    >
      <Link to="/diagramas" className="inline-flex min-h-toque w-fit items-center gap-1 text-sm underline">
        <ArrowLeft aria-hidden className="size-4" /> Diagramas
      </Link>
      <h1 className="text-2xl font-semibold">Nuevo diagrama</h1>
      <label className="text-sm font-medium">
        Título
        <input required maxLength={200} value={titulo} onChange={(e) => setTitulo(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Código
        <input
          required
          maxLength={100}
          pattern="[a-z0-9][a-z0-9\-]*"
          value={codigo}
          onChange={(e) => setCodigo(e.target.value)}
          aria-describedby="ayuda-codigo"
          className={claseCampo}
        />
      </label>
      <p id="ayuda-codigo" className="-mt-3 text-xs text-atlas-texto-suave">
        Minúsculas, números y guiones. Es lo que citan los documentos y no cambia después.
      </p>
      {aplicativos.isPending && <Cargando etiqueta="Cargando los aplicativos" alto="h-12" />}
      {aplicativos.isError && <p role="alert">No pudimos cargar los aplicativos.</p>}
      {aplicativos.data && <Opciones titulo="Aplicativo" valor={aplicativoId} opciones={aplicativos.data.map((a) => a.id)} textos={textosAplicativo} alCambiar={setAplicativoId} />}
      <Opciones titulo="Tipo" valor={tipo} opciones={TIPOS} textos={TEXTO_TIPO} alCambiar={setTipo} />
      <Opciones titulo="Visibilidad" valor={visibilidad} opciones={VISIBILIDADES} textos={TEXTO_VISIBILIDAD} alCambiar={setVisibilidad} />
      <EditorFuente key={tipo} tipo={tipo} fuente={fuente} aplicativos={aplicativos.data ?? []} alCambiar={alCambiar} />
      <label className="text-sm font-medium">
        Nota del cambio
        <input required maxLength={500} value={nota} onChange={(e) => setNota(e.target.value)} className={claseCampo} />
      </label>
      {error && <Aviso tono="error">{error}</Aviso>}
      <div>
        <Boton type="submit" disabled={guardando || !completo}>
          {guardando ? 'Guardando…' : 'Crear el diagrama'}
        </Boton>
      </div>
    </form>
  );
}

/** Editar: cada guardado es una versión nueva e inmutable. */
export default function EditarDiagrama() {
  const { id = '' } = useParams();
  const detalle = useDiagrama(id);
  const aplicativos = useAplicativos();
  if (detalle.isPending) return <Cargando etiqueta="Cargando el diagrama" alto="h-64" />;
  if (detalle.isError || !detalle.data.version) return <p role="alert">Este diagrama no existe o no tienes acceso.</p>;
  if (!detalle.data.puedeEditar) return <p role="alert">Solo quien tiene permiso sobre este aplicativo puede editar el diagrama.</p>;
  return <FormularioEdicion key={detalle.data.diagrama.versionActual} id={id} detalle={detalle.data} aplicativos={aplicativos.data ?? []} />;
}

function FormularioEdicion({ id, detalle, aplicativos }: { id: string; detalle: NonNullable<ReturnType<typeof useDiagrama>['data']>; aplicativos: AplicativoRef[] }) {
  const navegar = useNavigate();
  const cambio = useCambioDiagrama();
  const { diagrama: d, version } = detalle;
  const [titulo, setTitulo] = useState(version?.version.titulo ?? d.titulo);
  const [visibilidad, setVisibilidad] = useState<VisibilidadDiagrama>(version?.version.visibilidad ?? d.visibilidad);
  const [fuente, setFuente] = useState(version?.fuente ?? '');
  const [nota, setNota] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  return (
    <form
      aria-label="Editar el diagrama"
      className="flex min-w-0 flex-col gap-4"
      onSubmit={(e) => {
        e.preventDefault();
        setGuardando(true);
        setError(null);
        guardarDiagrama(id, { titulo, visibilidad, fuente, nota, versionBase: d.versionActual })
          .then(async () => {
            await cambio();
            await navegar(`/diagramas/${id}`);
          })
          .catch((motivo: unknown) => setError(mensajeDeError(motivo)))
          .finally(() => setGuardando(false));
      }}
    >
      <Link to={`/diagramas/${id}`} className="inline-flex min-h-toque w-fit items-center gap-1 text-sm underline">
        <ArrowLeft aria-hidden className="size-4" /> Volver al diagrama
      </Link>
      <h1 className="text-2xl font-semibold">Editar el diagrama</h1>
      <label className="text-sm font-medium">
        Título
        <input required maxLength={200} value={titulo} onChange={(e) => setTitulo(e.target.value)} className={claseCampo} />
      </label>
      <Opciones titulo="Visibilidad" valor={visibilidad} opciones={VISIBILIDADES} textos={TEXTO_VISIBILIDAD} alCambiar={setVisibilidad} />
      <EditorFuente tipo={d.tipo} fuente={fuente} aplicativos={aplicativos} alCambiar={setFuente} />
      <label className="text-sm font-medium">
        Nota del cambio
        <input required maxLength={500} value={nota} onChange={(e) => setNota(e.target.value)} className={claseCampo} />
      </label>
      {error && <Aviso tono="error">{error}</Aviso>}
      <div>
        <Boton type="submit" disabled={guardando || !nota.trim() || !titulo.trim()}>
          {guardando ? 'Guardando…' : 'Guardar la versión'}
        </Boton>
      </div>
    </form>
  );
}
