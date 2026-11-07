import { useMutation, useQuery } from '@tanstack/react-query';
import { CircleAlert, CircleCheck, Download, FileDown, LoaderCircle } from 'lucide-react';
import { useRef, useState } from 'react';

import { enviarJson, ErrorApi, pedir, urlEntrarMicrosoft } from '../../app/api';
import { useSesionOpcional } from '../../app/sesion';
import { formatearFechaHora } from '../../app/ui/formato';
import { Hoja } from '../../app/ui/Hoja';
import { Boton, cn } from '../../app/ui/Tarjeta';

/**
 * Exportar un documento o una colección (HU 5.6, ATLAS-ADR-0053 §7): solicitar, ver el estado mientras atlas-docs genera los
 * archivos y descargar con una URL firmada de 15 minutos. La descarga abre la URL en la misma pestaña: así funciona en iOS.
 */
export type FormatoDocumento = 'html' | 'pdf' | 'word' | 'markdown' | 'zip';

const RUTA = '/api/v1/documentacion/exportaciones';
const FORMATOS: Array<{ valor: Exclude<FormatoDocumento, 'zip'>; etiqueta: string }> = [
  { valor: 'pdf', etiqueta: 'PDF' },
  { valor: 'word', etiqueta: 'Word' },
  { valor: 'html', etiqueta: 'HTML' },
  { valor: 'markdown', etiqueta: 'Markdown' },
];

interface Exportacion {
  id: string;
  estado: 'pendiente' | 'procesando' | 'lista' | 'fallida' | 'vencida';
  nombre: string;
  formatos: string[];
  solicitadaEn: string;
  venceEn: string | null;
  archivos: { ruta: string; nombre: string; formato: string; bytes: number }[];
  error: { codigo: string; mensaje: string } | null;
}

export interface DocumentoAExportar {
  documentoId: string;
  version?: number;
}

const TEXTO_ESTADO: Record<Exportacion['estado'], string> = {
  pendiente: 'En cola: empezará en unos segundos.',
  procesando: 'Generando los archivos…',
  lista: 'Tu exportación está lista',
  fallida: 'La exportación falló.',
  vencida: 'La exportación venció; solicítala de nuevo.',
};

const MENSAJES: Record<string, string> = {
  no_encontrado: 'Alguno de los documentos ya no existe o no tienes acceso.',
  sin_version: 'Un documento sin versión publicada no se puede exportar.',
  no_lista: 'La exportación todavía no está lista.',
  vencida: 'La exportación venció; solicítala de nuevo.',
  almacenamiento_no_disponible: 'Las exportaciones no están disponibles en este momento. Inténtalo más tarde.',
};

type Aviso = { texto: string; reautenticar?: boolean } | null;

function avisoDe(error: unknown, porDefecto: string): Aviso {
  if (!(error instanceof ErrorApi)) return { texto: porDefecto };
  if (error.codigo === 'reautenticacion_requerida') return { texto: 'Por seguridad, confirma tu identidad antes de exportar.', reautenticar: true };
  const conocido = error.codigo ? MENSAJES[error.codigo] : undefined;
  if (conocido) return { texto: conocido };
  if (error.estado === 403) return { texto: 'No tienes permiso para exportar.' };
  return { texto: error.mensajePara(porDefecto) };
}

function MostrarAviso({ aviso }: { aviso: Aviso }) {
  const sesion = useSesionOpcional();
  if (!aviso) return null;
  const local = sesion ? sesion.yo.tipoPrincipal === 'externo' || sesion.yo.proveedor === 'emergencia' || sesion.yo.proveedor === 'local' : false;
  return (
    <div role="alert" className="rounded-xl border border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] p-3 text-sm text-[var(--badge-danger-fg)]">
      {aviso.texto}{' '}
      {aviso.reautenticar && !local && (
        <a className="font-semibold underline" href={`${urlEntrarMicrosoft(window.location.pathname + window.location.search)}&reautenticar=true`}>
          Confirmar con Microsoft
        </a>
      )}
      {aviso.reautenticar && local && (
        <button type="button" className="font-semibold underline" onClick={() => void sesion?.cerrarSesion()}>
          Salir y volver a entrar
        </button>
      )}
    </div>
  );
}

function tamano(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024).toLocaleString('es-CO')} KB`;
  return `${(bytes / (1024 * 1024)).toLocaleString('es-CO', { maximumFractionDigits: 1 })} MB`;
}

/** Botón «Exportar» con su hoja. `exportacionInicial` la abre al montar: el enlace del aviso «Tu exportación está lista». */
export function ExportarDocumentos({
  documentos,
  nombre,
  etiqueta = 'Exportar',
  exportacionInicial = null,
}: {
  documentos: DocumentoAExportar[];
  nombre?: string;
  etiqueta?: string;
  exportacionInicial?: string | null;
}) {
  const [abierta, setAbierta] = useState(exportacionInicial !== null);
  const [id, setId] = useState<string | null>(exportacionInicial);
  return (
    <>
      <Boton variante="secundario" onClick={() => setAbierta(true)}>
        <FileDown className="size-4" aria-hidden="true" /> {etiqueta}
      </Boton>
      <Hoja abierta={abierta} alCerrar={() => setAbierta(false)} titulo="Exportar">
        <div className="flex flex-col gap-3 px-4 pt-2 pb-5 text-sm">
          {abierta && (id === null ? <Elegir documentos={documentos} nombre={nombre} alSolicitar={setId} /> : <Estado id={id} alNueva={() => setId(null)} />)}
        </div>
      </Hoja>
    </>
  );
}

function Elegir({ documentos, nombre, alSolicitar }: { documentos: DocumentoAExportar[]; nombre?: string; alSolicitar: (id: string) => void }) {
  const [elegidos, setElegidos] = useState<FormatoDocumento[]>(['pdf']);
  const [aviso, setAviso] = useState<Aviso>(null);
  const solicitar = useMutation({
    mutationFn: async () => (await (await enviarJson(RUTA, 'POST', { documentos, nombre, formatos: [...elegidos, 'zip'] })).json()) as Exportacion,
  });
  const alternar = (f: FormatoDocumento) => setElegidos((actuales) => (actuales.includes(f) ? actuales.filter((x) => x !== f) : [...actuales, f]));
  return (
    <>
      <fieldset className="flex min-w-0 flex-col gap-1">
        <legend className="mb-1 font-medium">Formatos</legend>
        {FORMATOS.map((f) => (
          <label key={f.valor} className="flex min-h-toque cursor-pointer items-center gap-2">
            <input type="checkbox" className="size-5" checked={elegidos.includes(f.valor)} onChange={() => alternar(f.valor)} />
            {f.etiqueta}
          </label>
        ))}
      </fieldset>
      <p className="text-xs text-atlas-texto-suave">
        {documentos.length > 1 ? `Son ${documentos.length} documentos. ` : ''}
        Siempre se incluye un ZIP con los formatos elegidos. Se genera en segundo plano: puedes cerrar esta hoja y te avisamos cuando esté lista. Cada archivo lleva marca de agua y el hash del contenido, con
        los valores vigentes hoy de lo que el documento incrusta.
      </p>
      <MostrarAviso aviso={aviso} />
      <Boton
        disabled={elegidos.length === 0 || solicitar.isPending}
        onClick={() => {
          setAviso(null);
          solicitar.mutate(undefined, {
            onSuccess: (e) => alSolicitar(e.id),
            onError: (e) => setAviso(avisoDe(e, 'No se pudo solicitar la exportación.')),
          });
        }}
      >
        <FileDown className="size-4" aria-hidden="true" /> {solicitar.isPending ? 'Solicitando…' : 'Solicitar exportación'}
      </Boton>
    </>
  );
}

function Estado({ id, alNueva }: { id: string; alNueva: () => void }) {
  const desde = useRef(Date.now());
  const consulta = useQuery({
    queryKey: ['documentacion', 'exportaciones', id],
    queryFn: async () => (await (await pedir(`${RUTA}/${encodeURIComponent(id)}`)).json()) as Exportacion,
    refetchInterval: (c) => (c.state.data && !['pendiente', 'procesando'].includes(c.state.data.estado) ? false : Date.now() - desde.current > 30_000 ? 5_000 : 2_000),
  });
  const [aviso, setAviso] = useState<Aviso>(null);
  const [descargando, setDescargando] = useState(false);

  if (consulta.isPending) {
    return (
      <p role="status" className="flex items-center gap-2">
        <LoaderCircle className="size-4 animate-spin" aria-hidden="true" /> Consultando la exportación…
      </p>
    );
  }
  if (consulta.isError) {
    return (
      <>
        <MostrarAviso aviso={avisoDe(consulta.error, 'No pudimos consultar la exportación. Revisa tu conexión.')} />
        <Boton variante="secundario" onClick={alNueva}>
          Nueva exportación
        </Boton>
      </>
    );
  }
  const e = consulta.data;
  const enCurso = e.estado === 'pendiente' || e.estado === 'procesando';

  async function descargar(archivo?: string) {
    setAviso(null);
    setDescargando(true);
    try {
      const q = archivo ? `?archivo=${encodeURIComponent(archivo)}` : '';
      const d = (await (await pedir(`${RUTA}/${encodeURIComponent(id)}/descarga${q}`)).json()) as { url: string };
      window.location.assign(d.url);
    } catch (error) {
      setAviso(avisoDe(error, 'No se pudo preparar la descarga.'));
    } finally {
      setDescargando(false);
    }
  }

  return (
    <>
      <p className="font-medium break-words">{e.nombre}</p>
      <div
        role="status"
        aria-live="polite"
        className={cn(
          'flex items-start gap-2 rounded-xl border p-3',
          e.estado === 'lista' && 'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
          e.estado === 'fallida' && 'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]',
          (enCurso || e.estado === 'vencida') && 'border-border bg-card',
        )}
      >
        {enCurso && <LoaderCircle className="mt-0.5 size-4 shrink-0 animate-spin" aria-hidden="true" />}
        {e.estado === 'lista' && <CircleCheck className="mt-0.5 size-4 shrink-0" aria-hidden="true" />}
        {(e.estado === 'fallida' || e.estado === 'vencida') && <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />}
        <span className="min-w-0 break-words">
          {TEXTO_ESTADO[e.estado]}
          {e.estado === 'fallida' && e.error && <span className="block">{e.error.mensaje}</span>}
        </span>
      </div>
      {e.estado === 'lista' && e.venceEn && (
        <p className="text-xs text-atlas-texto-suave">
          Disponible hasta el <time dateTime={e.venceEn}>{formatearFechaHora(new Date(e.venceEn))}</time>
        </p>
      )}
      <MostrarAviso aviso={aviso} />
      {e.estado === 'lista' && (
        <ul aria-label="Archivos de la exportación" className="flex flex-col gap-1">
          {e.archivos.map((a) => (
            <li key={a.ruta}>
              <button
                type="button"
                disabled={descargando}
                onClick={() => void descargar(a.ruta)}
                className="flex min-h-toque w-full items-center gap-2 rounded-xl px-2 text-left hover:bg-atlas-activo disabled:opacity-60"
              >
                <Download className="size-4 shrink-0" aria-hidden="true" />
                <span className="min-w-0 flex-1 break-all">{a.nombre}</span>
                <span className="shrink-0 text-xs text-atlas-texto-suave">{tamano(a.bytes)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {!enCurso && (
        <Boton variante="secundario" onClick={alNueva}>
          Nueva exportación
        </Boton>
      )}
    </>
  );
}
