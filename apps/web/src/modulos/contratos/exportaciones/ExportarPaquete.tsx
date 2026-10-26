import { CircleAlert, CircleCheck, Download, FileDown, LoaderCircle } from 'lucide-react';
import { useRef, useState } from 'react';

import { ErrorApi, urlEntrarMicrosoft } from '../../../app/api';
import { useSesionOpcional } from '../../../app/sesion';
import { formatearFechaHora } from '../../../app/ui/formato';
import { Hoja } from '../../../app/ui/Hoja';
import { Boton, cn } from '../../../app/ui/Tarjeta';
import { Casilla } from '../paquetes/componentes';
import {
  type BaseExportaciones,
  type Exportacion,
  ETIQUETA_FORMATO,
  type Formato,
  FORMATOS,
  type OrigenExportacion,
  pedirDescarga,
  useExportacion,
  useSolicitarExportacion,
} from './api';
import { CompartirExportacion, type EnlaceCompartido } from './CompartirExportacion';
import { abrirEnLaMismaPestana } from './descarga';

/** Lo que la persona ve de un error de la API de exportaciones. */
type Aviso = { texto: string; reautenticar?: boolean } | null;

const MENSAJES: Record<string, string> = {
  sin_permiso: 'No tienes permiso para exportar esto.',
  no_encontrado: 'La exportación no existe o ya no la puedes ver.',
  no_lista: 'La exportación todavía no está lista.',
  vencida: 'La exportación venció; solicítala de nuevo.',
  almacenamiento_no_disponible: 'Las exportaciones no están disponibles en este momento. Inténtalo más tarde.',
};

export function avisoExportacion(error: unknown, porDefecto: string): Aviso {
  if (!(error instanceof ErrorApi)) return { texto: porDefecto };
  if (error.codigo === 'reautenticacion_requerida') return { texto: 'Por seguridad, confirma tu identidad antes de exportar.', reautenticar: true };
  if (error.codigo === 'no_exportable') {
    const detalles = Array.isArray(error.cuerpo?.detalles) ? (error.cuerpo.detalles as string[]) : [];
    return { texto: detalles.length > 0 ? `No se puede exportar: ${detalles.join(' · ')}` : error.mensajePara(porDefecto) };
  }
  const conocido = error.codigo ? MENSAJES[error.codigo] : undefined;
  if (conocido) return { texto: conocido };
  if (error.estado === 403) return { texto: MENSAJES.sin_permiso! };
  return { texto: error.mensajePara(porDefecto) };
}

function MostrarAviso({ aviso }: { aviso: Aviso }) {
  const sesion = useSesionOpcional();
  if (!aviso) return null;
  // Los externos y la cuenta de emergencia tienen cuenta local: confirman volviendo a entrar, no con Microsoft.
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

const TODOS: Formato[] = FORMATOS.map((f) => f.valor);

function tamano(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024).toLocaleString('es-CO')} KB`;
  return `${(bytes / (1024 * 1024)).toLocaleString('es-CO', { maximumFractionDigits: 1 })} MB`;
}

/**
 * Botón «Exportar» con su hoja (HU 4.5): elegir formatos, solicitar, ver el estado en vivo y descargar. Se monta en el
 * detalle del paquete interno (`base="/api/v1/contratos"`) y en el portal de clientes (`base="/api/v1/portal"`, solo
 * paquetes). La descarga abre la URL firmada en la misma pestaña (`window.location.assign`): así funciona en iOS, donde
 * un `blob:` o una ventana nueva no descargan.
 */
export function ExportarPaquete({
  base,
  origen,
  exportacionInicial = null,
  etiqueta = 'Exportar',
  controlada,
  enlace,
}: {
  base: BaseExportaciones;
  origen: OrigenExportacion;
  /** Exportación que se abre al montar (p. ej. desde el enlace de la notificación «Tu exportación está lista»). */
  exportacionInicial?: string | null;
  etiqueta?: string;
  /**
   * Sin botón propio: la hoja la abre quien monta el componente (el botón «Exportar» del portal, `acciones.ts`) y
   * `alCerrar` le avisa al cerrarla.
   */
  controlada?: { alCerrar: () => void };
  /**
   * Enlace interno que se comparte desde el resultado (HU 4.6). Por defecto, el del paquete: `/contratos/paquetes/<id>`
   * o, en el portal, `/portal/paquetes/<id>` (el externo solo recibe rutas que puede abrir).
   */
  enlace?: EnlaceCompartido;
}) {
  const [abiertaPropia, setAbierta] = useState(exportacionInicial !== null);
  const [id, setId] = useState<string | null>(exportacionInicial);
  const abierta = controlada ? true : abiertaPropia;
  const cerrar = controlada ? controlada.alCerrar : () => setAbierta(false);
  const compartido = enlace ?? enlacePorDefecto(base, origen);
  return (
    <>
      {!controlada && (
        <Boton variante="secundario" onClick={() => setAbierta(true)}>
          <FileDown className="size-4" aria-hidden="true" /> {etiqueta}
        </Boton>
      )}
      <Hoja abierta={abierta} alCerrar={cerrar} titulo="Exportar">
        <div className="flex flex-col gap-3 px-4 pt-2 pb-5 text-sm">
          {abierta && (id === null ? <Elegir base={base} origen={origen} alSolicitar={setId} /> : <Estado base={base} id={id} enlace={compartido} alNueva={() => setId(null)} />)}
        </div>
      </Hoja>
    </>
  );
}

/** El paquete en la PWA interna o en el portal; una versión interna no tiene enlace propio: el de contratos. */
function enlacePorDefecto(base: BaseExportaciones, origen: OrigenExportacion): EnlaceCompartido {
  if ('paqueteId' in origen) {
    const id = encodeURIComponent(origen.paqueteId);
    return { ruta: base === '/api/v1/portal' ? `/portal/paquetes/${id}` : `/contratos/paquetes/${id}`, que: 'este paquete', etiqueta: 'Compartir enlace al paquete' };
  }
  return { ruta: '/contratos', que: 'los contratos' };
}

function Elegir({ base, origen, alSolicitar }: { base: BaseExportaciones; origen: OrigenExportacion; alSolicitar: (id: string) => void }) {
  const [elegidos, setElegidos] = useState<Formato[]>(TODOS);
  const [aviso, setAviso] = useState<Aviso>(null);
  const solicitar = useSolicitarExportacion(base);
  const alternar = (f: Formato) => setElegidos((actuales) => (actuales.includes(f) ? actuales.filter((x) => x !== f) : [...actuales, f]));
  const formatos = elegidos.filter((f) => f !== 'zip');
  return (
    <>
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="mb-1 font-medium">Formatos</legend>
        {FORMATOS.map((f) => (
          <Casilla key={f.valor} marcada={f.valor === 'zip' || elegidos.includes(f.valor)} deshabilitada={f.valor === 'zip'} alCambiar={() => alternar(f.valor)}>
            {f.etiqueta}
            {f.valor === 'zip' && <span className="block text-xs text-atlas-texto-suave">Siempre se incluye, con los formatos elegidos.</span>}
          </Casilla>
        ))}
      </fieldset>
      <p className="text-xs text-atlas-texto-suave">
        Se genera en segundo plano; puedes cerrar esta hoja y te avisamos cuando esté lista. Cada archivo lleva marca de agua y el hash del contenido.
      </p>
      <MostrarAviso aviso={aviso} />
      <Boton
        disabled={formatos.length === 0 || solicitar.isPending}
        onClick={() => {
          setAviso(null);
          solicitar.mutate(
            { origen, formatos: [...formatos, 'zip'] },
            { onSuccess: (e) => alSolicitar(e.id), onError: (e) => setAviso(avisoExportacion(e, 'No se pudo solicitar la exportación.')) },
          );
        }}
      >
        <FileDown className="size-4" aria-hidden="true" /> {solicitar.isPending ? 'Solicitando…' : 'Solicitar exportación'}
      </Boton>
    </>
  );
}

const TEXTO_ESTADO: Record<Exportacion['estado'], string> = {
  pendiente: 'En cola: empezará en unos segundos.',
  procesando: 'Generando los archivos…',
  lista: 'Tu exportación está lista',
  fallida: 'La exportación falló.',
  vencida: 'La exportación venció; solicítala de nuevo.',
};

function Estado({ base, id, enlace, alNueva }: { base: BaseExportaciones; id: string; enlace: EnlaceCompartido; alNueva: () => void }) {
  const desde = useRef(Date.now());
  const consulta = useExportacion(base, id, desde.current);
  const [aviso, setAviso] = useState<Aviso>(null);
  const [descargando, setDescargando] = useState<string | null>(null);

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
        <MostrarAviso aviso={avisoExportacion(consulta.error, 'No pudimos consultar la exportación. Revisa tu conexión.')} />
        <Boton variante="secundario" onClick={alNueva}>
          Nueva exportación
        </Boton>
      </>
    );
  }
  const e = consulta.data;
  const enCurso = e.estado === 'pendiente' || e.estado === 'procesando';
  const zip = e.archivos.find((a) => a.formato === 'zip');
  const sueltos = e.archivos.filter((a) => a.formato !== 'zip');

  async function descargar(archivo?: string) {
    setAviso(null);
    setDescargando(archivo ?? 'zip');
    try {
      const d = await pedirDescarga(base, id, archivo);
      abrirEnLaMismaPestana(d.url);
    } catch (error) {
      setAviso(avisoExportacion(error, 'No se pudo preparar la descarga.'));
    } finally {
      setDescargando(null);
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
      <p className="text-xs text-atlas-texto-suave">
        Solicitada el <time dateTime={e.solicitadaEn}>{formatearFechaHora(new Date(e.solicitadaEn))}</time> · {e.formatos.map((f) => ETIQUETA_FORMATO[f] ?? f).join(', ')}
        {e.estado === 'lista' && e.venceEn && (
          <>
            {' '}
            · Disponible hasta el <time dateTime={e.venceEn}>{formatearFechaHora(new Date(e.venceEn))}</time>
          </>
        )}
      </p>
      <MostrarAviso aviso={aviso} />
      {e.estado === 'lista' && (
        <>
          <Boton disabled={descargando !== null} onClick={() => void descargar()}>
            <Download className="size-4" aria-hidden="true" /> Descargar ZIP{zip ? ` (${tamano(zip.bytes)})` : ''}
          </Boton>
          {sueltos.length > 0 && (
            <details className="min-w-0">
              <summary className="flex min-h-toque cursor-pointer items-center font-medium">Archivos sueltos ({sueltos.length})</summary>
              <ul aria-label="Archivos de la exportación" className="flex flex-col gap-1">
                {sueltos.map((a) => (
                  <li key={a.ruta}>
                    <button
                      type="button"
                      disabled={descargando !== null}
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
            </details>
          )}
          <CompartirExportacion base={base} exportacion={e} enlace={enlace} />
        </>
      )}
      {!enCurso && (
        <Boton variante="secundario" onClick={alNueva}>
          Nueva exportación
        </Boton>
      )}
    </>
  );
}
