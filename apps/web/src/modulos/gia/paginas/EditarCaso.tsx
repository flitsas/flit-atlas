import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';

import { useCaso, useEnvio, type CasoDetalle, type FichaCaso, type PersonaGia } from '../api';
import { Cargando, claseBoton, claseCampo, ElegirPersona, ErrorEnvio, lista, Multiples, Opciones, Pagina, vacioANulo } from '../componentes';

const opciones = (valores: string[]) => valores.map((v) => ({ valor: v, texto: v.replace(/_/g, ' ') }));

const FICHA_VACIA: FichaCaso = {
  nombre: '', aplicacion: '', aplicativoId: null, repo: null, duenoPersonaId: null, ambientes: [], proposito: '', tipo: null, usuariosAfectados: [],
  decisionSobrePersonas: null, decisionDescripcion: null, humanoEnBucle: null, categoriasDato: [], tiposPii: [], volumenMensualEstimado: null, baseLegal: null,
  baseLegalRef: null, minimizacion: null, presidioActivo: null, presidioEntidades: [], aliasUsados: [], proveedorActual: null, transferenciaInternacional: null,
  metricasCalidad: null, evalsRuta: null, promptsUbicacion: null, controles: null, avisoUsuario: null, planRollback: null, enlaces: [],
};

type CampoTexto = 'repo' | 'decisionDescripcion' | 'baseLegal' | 'baseLegalRef' | 'minimizacion' | 'proveedorActual' | 'transferenciaInternacional' | 'metricasCalidad' | 'evalsRuta' | 'promptsUbicacion' | 'controles' | 'avisoUsuario' | 'planRollback';

const TEXTOS: { campo: CampoTexto; titulo: string }[] = [
  { campo: 'repo', titulo: 'Repo' },
  { campo: 'decisionDescripcion', titulo: 'Descripción de la decisión' },
  { campo: 'baseLegal', titulo: 'Base legal' },
  { campo: 'baseLegalRef', titulo: 'Referencia de la base legal' },
  { campo: 'minimizacion', titulo: 'Minimización aplicada' },
  { campo: 'proveedorActual', titulo: 'Proveedor actual' },
  { campo: 'transferenciaInternacional', titulo: 'País de procesamiento o transferencia internacional' },
  { campo: 'metricasCalidad', titulo: 'Métricas de calidad' },
  { campo: 'evalsRuta', titulo: 'Suite de evals (ruta en el repo)' },
  { campo: 'promptsUbicacion', titulo: 'Dónde están versionados los prompts' },
  { campo: 'controles', titulo: 'Controles aplicados' },
  { campo: 'avisoUsuario', titulo: 'Aviso al usuario' },
  { campo: 'planRollback', titulo: 'Plan de rollback' },
];

const SI_NO = [{ valor: 'si', texto: 'Sí' }, { valor: 'no', texto: 'No' }];
const aSiNo = (v: boolean | null) => (v === null ? null : v ? 'si' : 'no');

/** Formulario de la ficha (plan 02 §7.1). Lo obligatorio se exige al enviar a revisión. */
export function FormularioCaso({ inicial, dueno, conCodigo, alEnviar, ocupado, error }: {
  inicial: FichaCaso;
  dueno: PersonaGia | null;
  conCodigo: boolean;
  alEnviar: (ficha: FichaCaso, extra: { codigo: string; motivo: string }) => void;
  ocupado: boolean;
  error: unknown;
}) {
  const [f, setF] = useState(inicial);
  const [persona, setPersona] = useState(dueno);
  const [codigo, setCodigo] = useState('');
  const [motivo, setMotivo] = useState('');
  const poner = <K extends keyof FichaCaso>(campo: K, valor: FichaCaso[K]) => setF((actual) => ({ ...actual, [campo]: valor }));

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(e) => {
        e.preventDefault();
        alEnviar({ ...f, duenoPersonaId: persona?.id ?? null }, { codigo, motivo });
      }}
    >
      {conCodigo && (
        <label className="text-sm font-medium">
          Código (minúsculas, números y guiones; no cambia)
          <input required value={codigo} onChange={(e) => setCodigo(e.target.value)} className={claseCampo} />
        </label>
      )}
      <label className="text-sm font-medium">
        Nombre
        <input required value={f.nombre} onChange={(e) => poner('nombre', e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Aplicación o repo
        <input required value={f.aplicacion} onChange={(e) => poner('aplicacion', e.target.value)} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Propósito y problema que resuelve
        <textarea required rows={3} value={f.proposito} onChange={(e) => poner('proposito', e.target.value)} className={`${claseCampo} py-2`} />
      </label>
      <ElegirPersona titulo="Dueño del caso" elegida={persona} alElegir={setPersona} />
      <Opciones titulo="Tipo" valor={f.tipo} opciones={opciones(['ocr', 'chat', 'clasificacion', 'generacion', 'agente', 'embeddings', 'otro'])} alCambiar={(v) => poner('tipo', v)} />
      <Multiples titulo="Ambientes" valores={f.ambientes} opciones={opciones(['dev', 'qa', 'pdn'])} alCambiar={(v) => poner('ambientes', v)} />
      <Multiples titulo="Usuarios afectados" valores={f.usuariosAfectados} opciones={opciones(['interno', 'cliente', 'ciudadano'])} alCambiar={(v) => poner('usuariosAfectados', v)} />
      <Opciones titulo="¿Produce decisiones con efecto sobre personas?" valor={aSiNo(f.decisionSobrePersonas)} opciones={SI_NO} alCambiar={(v) => poner('decisionSobrePersonas', v === 'si')} />
      <Opciones titulo="Humano en el bucle" valor={f.humanoEnBucle} opciones={opciones(['ninguno', 'revision_posterior', 'aprobacion_previa'])} alCambiar={(v) => poner('humanoEnBucle', v)} />
      <Multiples titulo="Categorías de datos" valores={f.categoriasDato} opciones={opciones(['publico', 'interno', 'confidencial', 'personal', 'sensible'])} alCambiar={(v) => poner('categoriasDato', v)} />
      <label className="text-sm font-medium">
        Tipos de PII (separados por comas)
        <input value={f.tiposPii.join(', ')} onChange={(e) => poner('tiposPii', lista(e.target.value))} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Volumen estimado (llamadas al mes)
        <input type="number" min={0} value={f.volumenMensualEstimado ?? ''} onChange={(e) => poner('volumenMensualEstimado', e.target.value === '' ? null : Number(e.target.value))} className={claseCampo} />
      </label>
      <Opciones titulo="¿Presidio activo?" valor={aSiNo(f.presidioActivo)} opciones={SI_NO} alCambiar={(v) => poner('presidioActivo', v === 'si')} />
      <label className="text-sm font-medium">
        Entidades de Presidio (separadas por comas)
        <input value={f.presidioEntidades.join(', ')} onChange={(e) => poner('presidioEntidades', lista(e.target.value))} className={claseCampo} />
      </label>
      <label className="text-sm font-medium">
        Alias del gateway (separados por comas)
        <input value={f.aliasUsados.join(', ')} onChange={(e) => poner('aliasUsados', lista(e.target.value))} className={claseCampo} />
      </label>
      {TEXTOS.map((t) => (
        <label key={t.campo} className="text-sm font-medium">
          {t.titulo}
          <input value={f[t.campo] ?? ''} onChange={(e) => poner(t.campo, vacioANulo(e.target.value))} className={claseCampo} />
        </label>
      ))}
      {!conCodigo && (
        <label className="text-sm font-medium">
          Motivo del cambio
          <input required value={motivo} onChange={(e) => setMotivo(e.target.value)} className={claseCampo} />
        </label>
      )}
      <ErrorEnvio error={error} porDefecto="No pudimos guardar la ficha." />
      <button type="submit" disabled={ocupado} className={`${claseBoton} self-start`}>
        Guardar
      </button>
    </form>
  );
}

export { FICHA_VACIA };

function Editor({ detalle }: { detalle: CasoDetalle }) {
  const navegar = useNavigate();
  const envio = useEnvio();
  const id = detalle.caso.id;
  return (
    <FormularioCaso
      inicial={detalle.ficha}
      dueno={detalle.caso.dueno}
      conCodigo={false}
      ocupado={envio.isPending}
      error={envio.error}
      alEnviar={(ficha, { motivo }) =>
        envio.mutate({ ruta: `/casos/${id}`, metodo: 'PUT', cuerpo: { ficha, motivo, versionBase: detalle.versionActual } }, { onSuccess: () => void navegar(`/gobierno-ia/casos/${id}`) })
      }
    />
  );
}

export default function EditarCaso() {
  const { id = '' } = useParams();
  const caso = useCaso(id);
  return (
    <Pagina titulo="Editar la ficha" descripcion={caso.data?.caso.codigo}>
      {caso.isPending && <Cargando etiqueta="Cargando la ficha" alto="h-48" />}
      {caso.isError && <p role="alert">No pudimos cargar la ficha.</p>}
      {caso.data && <Editor detalle={caso.data} />}
    </Pagina>
  );
}
