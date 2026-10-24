/**
 * Compartir desde el móvil (HU 4.6, prompt maestro §8.5 y §11, ATLAS-ADR-0042 «Compartir»). Sin React: lo usan
 * `app/ui/Compartir.tsx` y la hoja de exportación.
 *
 * Reglas:
 * - Lo que se comparte como enlace es **siempre** una ruta interna de Atlas (`/contratos/…`, `/portal/…`), que exige
 *   sesión y permisos al abrirse. Nunca una URL firmada ni un enlace anónimo (§8.5: «nunca enlaces anónimos por
 *   defecto»). `enlaceInterno` rechaza todo lo que no sea una ruta del propio origen.
 * - Un archivo exportado se comparte con el `File` que el navegador ya descargó de la URL firmada: no pasa por atlas-api
 *   ni hay endpoint nuevo. La descarga ya queda auditada (`contratos.exportacion_descargada`, ATLAS-ADR-0042).
 * - No se registra «compartido» en ningún lado: la PWA no tiene telemetría de cliente y el destino (correo, Teams,
 *   WhatsApp…) no se conoce; lo que sale del servidor ya está auditado (la vista del enlace, la descarga del archivo).
 * - Cancelar la hoja nativa (`AbortError`) no es un error y no se muestra.
 */

export type ResultadoCompartir = 'compartido' | 'cancelado' | 'no_soportado' | 'no_permitido' | 'error';

/** Tope de lo que se descarga en memoria para compartir como archivo (Safari en iOS se cierra con archivos grandes). */
export const TOPE_ARCHIVO_BYTES = 50 * 1024 * 1024;

const TIPOS: Record<string, string> = {
  pdf: 'application/pdf',
  zip: 'application/zip',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  html: 'text/html',
  json: 'application/json',
  yaml: 'application/yaml',
  yml: 'application/yaml',
  csv: 'text/csv',
  txt: 'text/plain',
};

/** Tipo MIME por la extensión del nombre (las URL firmadas no siempre traen el `Content-Type` que la hoja espera). */
export function tipoDeArchivo(nombre: string): string {
  const extension = nombre.toLowerCase().split('.').pop() ?? '';
  return TIPOS[extension] ?? 'application/octet-stream';
}

/** URL absoluta de una ruta interna de Atlas. Lanza si no es una ruta del propio origen. */
export function enlaceInterno(ruta: string, origen: string = window.location.origin): string {
  if (!ruta.startsWith('/') || ruta.startsWith('//') || ruta.startsWith('/\\')) throw new Error('Solo se comparten rutas internas de Atlas.');
  const url = new URL(ruta, origen);
  if (url.origin !== new URL(origen).origin) throw new Error('Solo se comparten rutas internas de Atlas.');
  return url.href;
}

/** El navegador tiene la hoja nativa de compartir (Android, iOS, Windows, macOS). */
export function soportaCompartir(): boolean {
  return typeof navigator !== 'undefined' && typeof navigator.share === 'function';
}

/** Si estos datos se pueden compartir con la hoja nativa. Sin `canShare` (Safari < 14) solo enlace y texto. */
export function puedeCompartir(datos: ShareData): boolean {
  if (!soportaCompartir()) return false;
  if (typeof navigator.canShare !== 'function') return !datos.files || datos.files.length === 0;
  try {
    return navigator.canShare(datos);
  } catch {
    return false;
  }
}

/**
 * Si este dispositivo comparte un archivo con ese nombre (y su tipo). Se prueba con un `File` vacío antes de
 * descargarlo: Chrome en Android solo deja ciertos tipos (PDF, imágenes, texto…; un ZIP no) y el escritorio sin hoja
 * nativa ninguno.
 */
export function puedeCompartirArchivo(nombre: string): boolean {
  if (typeof File === 'undefined') return false;
  return puedeCompartir({ files: [new File([], nombre, { type: tipoDeArchivo(nombre) })] });
}

/**
 * Abre la hoja nativa. Debe llamarse dentro del gesto de la persona (un clic): Safari exige la «activación
 * transitoria» y, si antes hubo un `await` largo (p. ej. descargar el archivo), responde `NotAllowedError`. Por eso el
 * archivo se prepara con un toque y se comparte con otro (`no_permitido` pide volver a tocar).
 */
export async function compartir(datos: ShareData): Promise<ResultadoCompartir> {
  if (!puedeCompartir(datos)) return 'no_soportado';
  try {
    await navigator.share(datos);
    return 'compartido';
  } catch (error) {
    const nombre = error instanceof DOMException || error instanceof Error ? error.name : '';
    if (nombre === 'AbortError') return 'cancelado';
    if (nombre === 'NotAllowedError') return 'no_permitido';
    return 'error';
  }
}

/** Copia el enlace al portapapeles. `false` si no hay portapapeles (http, permisos) o falla. */
export async function copiarEnlace(enlace: string): Promise<boolean> {
  if (typeof navigator === 'undefined' || !navigator.clipboard?.writeText) return false;
  try {
    await navigator.clipboard.writeText(enlace);
    return true;
  } catch {
    return false;
  }
}

/**
 * `mailto:` con asunto y el enlace interno; el cuerpo no lleva nada más que el enlace y una frase (ningún dato del
 * contrato, de la organización ni de la persona: el correo sale del dispositivo y no sabemos a quién).
 */
export function urlCorreo(asunto: string, enlace: string): string {
  const cuerpo = `Te comparto este enlace de Flit Atlas (necesitas iniciar sesión para abrirlo):\n${enlace}`;
  return `mailto:?subject=${encodeURIComponent(asunto)}&body=${encodeURIComponent(cuerpo)}`;
}

/**
 * Deep link «Compartir en Teams» (`teams.microsoft.com/share`, el mismo del botón para sitios web de Microsoft) con la
 * URL interna. `preview=false`: Teams no intenta leer la página (sin sesión solo vería la de ingreso).
 */
export function urlTeams(enlace: string, texto: string): string {
  const parametros = new URLSearchParams({ href: enlace, msgText: texto, preview: 'false' });
  return `https://teams.microsoft.com/share?${parametros.toString()}`;
}

export class ErrorArchivo extends Error {
  constructor(readonly motivo: 'red' | 'estado' | 'grande') {
    super(motivo);
    this.name = 'ErrorArchivo';
  }
}

/**
 * Descarga un archivo de su URL firmada a memoria, como `File`, para compartirlo con la hoja nativa. Sin credenciales
 * (la URL ya lleva la firma; la cookie del BFF no viaja al S3) y sin referer. Si la CSP (`connect-src`) o el CORS del
 * bucket no lo permiten, falla con `red` y quien llama ofrece descargar en la misma pestaña.
 */
export async function descargarComoArchivo(url: string, nombre: string, senal?: AbortSignal): Promise<File> {
  let respuesta: Response;
  try {
    respuesta = await fetch(url, { credentials: 'omit', mode: 'cors', cache: 'no-store', referrerPolicy: 'no-referrer', signal: senal });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ErrorArchivo('red');
  }
  if (!respuesta.ok) throw new ErrorArchivo('estado');
  const largo = Number(respuesta.headers.get('Content-Length') ?? '0');
  if (largo > TOPE_ARCHIVO_BYTES) throw new ErrorArchivo('grande');
  const contenido = await respuesta.arrayBuffer();
  if (contenido.byteLength > TOPE_ARCHIVO_BYTES) throw new ErrorArchivo('grande');
  return new File([contenido], nombre, { type: tipoDeArchivo(nombre) });
}
