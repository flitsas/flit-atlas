import { ArrowLeft } from 'lucide-react';
import { useId, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';

import { Boton, cn } from '../../../app/ui/Tarjeta';
import { formatearFechaHora } from '../../../app/ui/formato';
import {
  type CamposDocumento,
  crearDocumento,
  type DetalleRevision,
  type DocumentoDetalle,
  guardarDocumento,
  mensajeDeError,
  restaurarRevision,
  TEXTO_ORIGEN_REVISION,
  TEXTO_TIPO,
  TEXTO_VISIBILIDAD,
  type TipoDocumento,
  useAplicativos,
  useCambioDocumento,
  useDocumento,
  useRevision,
  useRevisiones,
  type VisibilidadDocumento,
} from '../api';
import { Aviso, Cargando, claseCampo, VistaDocumento } from '../componentes';

// El ADR solo llega desde los repos.
const TIPOS = (Object.keys(TEXTO_TIPO) as TipoDocumento[]).filter((t) => t !== 'adr');
const VISIBILIDADES = Object.keys(TEXTO_VISIBILIDAD) as VisibilidadDocumento[];

const claseOpcion = (activa: boolean) =>
  cn('inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm', activa ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card');

function Opciones<T extends string>({ titulo, valor, opciones, textos, alCambiar }: { titulo: string; valor: T; opciones: T[]; textos: Record<T, string>; alCambiar: (v: T) => void }) {
  const id = useId();
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-sm font-medium">{titulo}</legend>
      <div className="flex flex-wrap gap-2">
        {opciones.map((o) => (
          <label key={o} className={claseOpcion(valor === o)}>
            <input type="radio" name={id} value={o} checked={valor === o} onChange={() => alCambiar(o)} className="size-4 accent-[var(--flit-brand-ink)]" />
            {textos[o]}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/**
 * Editor de un documento escrito en Atlas (HU 5.3, ATLAS-ADR-0053 §8): un `<textarea>` de MDX con vista previa del mismo
 * renderizador del lector. En el móvil, «Escribir» y «Vista previa» se alternan; desde `lg`, van lado a lado. Cada guardado
 * es una revisión nueva; lo publicado no cambia hasta volver a publicar.
 */
function Formulario({
  inicial,
  esNuevo,
  guardando,
  error,
  alGuardar,
  volverA,
}: {
  inicial: CamposDocumento;
  esNuevo: boolean;
  guardando: boolean;
  error: string | null;
  alGuardar: (c: CamposDocumento) => void;
  volverA: string;
}) {
  const [campos, setCampos] = useState(inicial);
  const [audiencias, setAudiencias] = useState(inicial.audiencias.join(', '));
  const [modo, setModo] = useState<'escribir' | 'vista'>('escribir');
  const id = useId();
  const aplicativos = useAplicativos();
  const navegar = useNavigate();
  const cambiar = <K extends keyof CamposDocumento>(clave: K, valor: CamposDocumento[K]) => setCampos((c) => ({ ...c, [clave]: valor }));
  const completo = campos.titulo.trim() && campos.contenido.trim() && campos.nota.trim() && campos.responsable.trim() && (!esNuevo || campos.aplicativoId);

  return (
    <form
      aria-label={esNuevo ? 'Nuevo documento' : 'Editar el documento'}
      className="flex min-w-0 flex-col gap-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (completo) {
          alGuardar({
            ...campos,
            audiencias: audiencias
              .split(',')
              .map((a) => a.trim())
              .filter(Boolean),
          });
        }
      }}
    >
      {esNuevo && (
        <fieldset className="min-w-0">
          <legend className="mb-1 text-sm font-medium">Aplicativo</legend>
          {aplicativos.isPending && <Cargando etiqueta="Cargando los aplicativos" alto="h-12" />}
          {aplicativos.isError && <p role="alert">No pudimos cargar los aplicativos.</p>}
          <div className="flex max-h-56 flex-wrap gap-2 overflow-y-auto">
            {(aplicativos.data ?? []).map((a) => (
              <label key={a.id} className={claseOpcion(campos.aplicativoId === a.id)}>
                <input type="radio" name={`${id}-aplicativo`} checked={campos.aplicativoId === a.id} onChange={() => cambiar('aplicativoId', a.id)} className="size-4 accent-[var(--flit-brand-ink)]" />
                {a.nombre}
              </label>
            ))}
          </div>
        </fieldset>
      )}
      <label className="text-sm font-medium">
        Título
        <input required maxLength={200} value={campos.titulo} onChange={(e) => cambiar('titulo', e.target.value)} className={claseCampo} />
      </label>
      <Opciones titulo="Tipo" valor={campos.tipo} opciones={TIPOS} textos={TEXTO_TIPO} alCambiar={(v) => cambiar('tipo', v)} />
      <Opciones titulo="Visibilidad" valor={campos.visibilidad} opciones={VISIBILIDADES} textos={TEXTO_VISIBILIDAD} alCambiar={(v) => cambiar('visibilidad', v)} />
      {campos.visibilidad === 'publico_clientes' && (
        <p className="text-xs text-atlas-texto-suave">Para publicarlo lo aprueba el responsable principal o secundario del aplicativo.</p>
      )}

      <div className="flex gap-2 lg:hidden" role="group" aria-label="Modo del editor">
        {(['escribir', 'vista'] as const).map((m) => (
          <Boton key={m} variante={modo === m ? 'primario' : 'secundario'} aria-pressed={modo === m} onClick={() => setModo(m)}>
            {m === 'escribir' ? 'Escribir' : 'Vista previa'}
          </Boton>
        ))}
      </div>
      <div className="grid min-w-0 gap-3 lg:grid-cols-2">
        <label className={cn('flex min-w-0 flex-col text-sm font-medium', modo !== 'escribir' && 'hidden lg:flex')}>
          Cuerpo en MDX
          <textarea
            required
            maxLength={300_000}
            rows={18}
            spellCheck
            value={campos.contenido}
            onChange={(e) => cambiar('contenido', e.target.value)}
            aria-describedby={`${id}-ayuda`}
            className={`${claseCampo} min-h-72 py-2 font-mono text-sm`}
          />
          <span id={`${id}-ayuda`} className="mt-1 text-xs font-normal text-atlas-texto-suave">
            Markdown con los componentes Contrato, Diagrama, Responsables, SoporteDeTurno, Alerta y Pestanas. Los atributos van entre comillas; no se admiten expresiones, import ni export. Usa variables como $TOKEN, nunca credenciales reales.
          </span>
        </label>
        <section aria-label="Vista previa" className={cn('min-w-0 rounded-xl border border-border p-3', modo !== 'vista' && 'hidden lg:block')}>
          <p className="mb-2 text-xs font-medium text-atlas-texto-suave">Vista previa</p>
          {campos.contenido.trim() ? (
            <VistaDocumento texto={campos.contenido} etiqueta="Vista previa del documento" conIndice={false} />
          ) : (
            <p className="text-sm text-atlas-texto-suave">Escribe el cuerpo para ver cómo queda.</p>
          )}
        </section>
      </div>

      <label className="text-sm font-medium">
        Responsable funcional
        <input required maxLength={200} value={campos.responsable} onChange={(e) => cambiar('responsable', e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Audiencias (separadas por coma)
        <input maxLength={500} value={audiencias} onChange={(e) => setAudiencias(e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Nota del cambio
        <textarea required maxLength={500} rows={2} value={campos.nota} onChange={(e) => cambiar('nota', e.target.value)} className={`${claseCampo} py-2`} />
      </label>

      {error && <Aviso tono="error">{error}</Aviso>}
      <div className="flex flex-wrap gap-2">
        <Boton type="submit" disabled={!completo || guardando}>
          {guardando ? 'Guardando…' : 'Guardar revisión'}
        </Boton>
        <Boton variante="secundario" onClick={() => void navegar(volverA)}>
          Cancelar
        </Boton>
      </div>
    </form>
  );
}

const camposDe = (d: DocumentoDetalle, r: DetalleRevision): CamposDocumento => ({
  aplicativoId: d.documento.aplicativoId,
  tipo: r.tipo,
  titulo: r.revision.titulo,
  contenido: r.contenido,
  visibilidad: r.revision.visibilidad,
  audiencias: r.audiencias,
  responsable: r.responsable,
  nota: '',
});

export function NuevoDocumento() {
  const navegar = useNavigate();
  const crear = useCambioDocumento(crearDocumento);
  return (
    <div className="mx-auto flex max-w-6xl min-w-0 flex-col gap-4">
      <Link to="/documentacion" className="inline-flex min-h-toque items-center gap-2 self-start text-sm underline">
        <ArrowLeft className="size-4" aria-hidden="true" /> Documentación
      </Link>
      <h1 className="text-2xl font-semibold">Nuevo documento</h1>
      <Formulario
        esNuevo
        volverA="/documentacion"
        inicial={{ aplicativoId: null, tipo: 'guia_funcional', titulo: '', contenido: '', visibilidad: 'interno_negocio', audiencias: [], responsable: '', nota: '' }}
        guardando={crear.isPending}
        error={crear.isError ? mensajeDeError(crear.error, 'No se pudo crear el documento.') : null}
        alGuardar={(c) => crear.mutate(c, { onSuccess: (d) => void navegar(`/documentacion/${d.documento.id}/editar`) })}
      />
    </div>
  );
}

/** Historial con restauración: restaurar crea una revisión nueva igual a la elegida, sin reescribir la historia. */
function Historial({ id, revisionActual }: { id: string; revisionActual: number }) {
  const revisiones = useRevisiones(id);
  const [nota, setNota] = useState('');
  const restaurar = useCambioDocumento((numero: number) => restaurarRevision(id, numero, nota, revisionActual));
  if (revisiones.isPending) return <Cargando etiqueta="Cargando el historial" />;
  if (revisiones.isError) return <p role="alert">No pudimos cargar el historial.</p>;
  return (
    <section aria-label="Historial de revisiones" className="flex min-w-0 flex-col gap-2">
      <h2 className="text-lg font-semibold">Historial</h2>
      <label className="text-sm font-medium">
        Nota para restaurar
        <input maxLength={500} value={nota} onChange={(e) => setNota(e.target.value)} className={claseCampo} />
      </label>
      {restaurar.isError && <Aviso tono="error">{mensajeDeError(restaurar.error, 'No se pudo restaurar.')}</Aviso>}
      {restaurar.isSuccess && <Aviso tono="exito">Revisión restaurada: es la nueva última revisión.</Aviso>}
      <ol className="flex flex-col gap-2">
        {revisiones.data.map((r) => (
          <li key={r.numero} className="flex min-w-0 flex-wrap items-center justify-between gap-2 rounded-xl border border-border p-3 text-sm">
            <span className="min-w-0 break-words">
              <span className="font-medium">Revisión {r.numero}</span> · {TEXTO_ORIGEN_REVISION[r.origen]}
              {r.restauradaDe && ` de la ${r.restauradaDe}`} · {formatearFechaHora(new Date(r.guardadaEn))}
              <span className="block text-atlas-texto-suave">{r.nota}</span>
            </span>
            {r.numero !== revisionActual && (
              <Boton variante="secundario" disabled={!nota.trim() || restaurar.isPending} aria-label={`Restaurar la revisión ${r.numero}`} onClick={() => restaurar.mutate(r.numero)}>
                Restaurar
              </Boton>
            )}
          </li>
        ))}
      </ol>
    </section>
  );
}

export default function EditarDocumento() {
  const { id = '' } = useParams();
  const navegar = useNavigate();
  const detalle = useDocumento(id);
  const revision = useRevision(id, detalle.data?.documento.revisionActual ?? null);
  const guardar = useCambioDocumento(({ c, base }: { c: CamposDocumento; base: number }) => guardarDocumento(id, c, base));
  const volverA = `/documentacion/${encodeURIComponent(id)}`;

  if (detalle.isPending || (detalle.isSuccess && detalle.data.documento.revisionActual > 0 && revision.isPending)) return <Cargando etiqueta="Cargando el editor" alto="h-64" />;
  if (detalle.isError || revision.isError) return <p role="alert">No pudimos abrir el documento para editarlo.</p>;
  const d = detalle.data;
  if (!d.puedeEditar || d.documento.origen === 'repo' || !revision.data) {
    return (
      <div className="mx-auto flex max-w-3xl flex-col gap-3">
        <h1 className="text-2xl font-semibold">No puedes editar este documento</h1>
        <p>{d.documento.origen === 'repo' ? 'Vive en su repo: se cambia allí, con un PR.' : 'Solo el líder técnico o el responsable del aplicativo lo editan.'}</p>
        <Link to={volverA} className="min-h-toque self-start underline">
          Volver al documento
        </Link>
      </div>
    );
  }
  return (
    <div className="mx-auto flex max-w-6xl min-w-0 flex-col gap-4">
      <Link to={volverA} className="inline-flex min-h-toque items-center gap-2 self-start text-sm underline">
        <ArrowLeft className="size-4" aria-hidden="true" /> Documento
      </Link>
      <h1 className="text-2xl font-semibold break-words">Editar: {d.documento.titulo}</h1>
      <Formulario
        // Una revisión nueva (por ejemplo, al restaurar) recarga el formulario con ella.
        key={d.documento.revisionActual}
        esNuevo={false}
        volverA={volverA}
        inicial={camposDe(d, revision.data)}
        guardando={guardar.isPending}
        error={guardar.isError ? mensajeDeError(guardar.error, 'No se pudo guardar.') : null}
        alGuardar={(c) => guardar.mutate({ c, base: d.documento.revisionActual }, { onSuccess: () => void navegar(volverA) })}
      />
      <Historial id={id} revisionActual={d.documento.revisionActual} />
    </div>
  );
}
