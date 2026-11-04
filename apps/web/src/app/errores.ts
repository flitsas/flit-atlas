import { CABECERA_CSRF } from './api';

/**
 * Recuperación y reporte de los errores que llegan a la pantalla «Algo salió mal» (`ErrorDeRuta`).
 *
 * - Un chunk que no se descarga suele ser un despliegue nuevo: los nombres con hash de la versión vieja ya no existen.
 *   Se recarga la página una sola vez por pestaña (marca en `sessionStorage`); si vuelve a fallar, se muestra el botón.
 * - Los demás errores se envían a `POST /api/v1/plataforma/errores-cliente`, que los escribe como advertencia en el log
 *   estructurado. Solo viaja lo técnico (tipo, mensaje, pila, ruta sin consulta ni fragmento, versión) y el servidor
 *   tacha además correos y números largos. La persona ve una referencia corta para citarla al equipo de Atlas.
 * - El endpoint exige sesión: sin ella (páginas públicas como `/invitacion` o `/emergencia`, o antes de entrar) no se
 *   envía nada, pero la persona igual ve la referencia y el botón de recargar.
 */

export const RUTA_ERRORES_CLIENTE = '/api/v1/plataforma/errores-cliente';
export const MARCA_RECARGA = 'atlas.recarga-por-chunk';

/** Tras este tiempo, otro despliegue puede volver a recargar sola la misma pestaña. */
const VENTANA_RECARGA_MS = 10 * 60_000;

const PATRONES_CARGA = [
  /ChunkLoadError/i,
  /Failed to fetch dynamically imported module/i, // Chromium
  /Importing a module script failed/i, // Safari
  /error loading dynamically imported module/i, // Firefox
  /Loading (CSS )?chunk \S+ failed/i,
  /Unable to preload CSS/i, // vite:preloadError
];

/** Recarga de la página, en un objeto para poder reemplazarla en las pruebas (jsdom no deja tocar `location`). */
export const pagina = { recargar: (): void => window.location.reload() };

/** Si el error es de descarga de un módulo diferido (import dinámico fallido). */
export function esErrorDeCarga(error: unknown): boolean {
  if (!(error instanceof Error)) return false;
  const texto = `${error.name}: ${error.message}`;
  return PATRONES_CARGA.some((patron) => patron.test(texto));
}

/**
 * Marca el intento y devuelve `true` si corresponde recargar. Sin `sessionStorage` (modo privado, bloqueado) devuelve
 * `false`: sin marca no hay forma de evitar un bucle de recargas.
 */
export function debeRecargarPorCarga(ahora = Date.now()): boolean {
  if (yaSeIntentoRecarga(ahora)) return false;
  try {
    window.sessionStorage.setItem(MARCA_RECARGA, String(ahora));
    return true;
  } catch {
    return false;
  }
}

/** Solo lee la marca (sin efectos, para el primer render). Sin `sessionStorage`, cuenta como ya intentado. */
export function yaSeIntentoRecarga(ahora = Date.now()): boolean {
  try {
    const previa = Number(window.sessionStorage.getItem(MARCA_RECARGA));
    return Boolean(previa) && ahora - previa < VENTANA_RECARGA_MS;
  } catch {
    return true;
  }
}

/** Ocho caracteres sin ambigüedades (sin 0/O ni 1/I/L), fáciles de dictar por teléfono. */
export function referenciaCorta(): string {
  const alfabeto = '23456789ABCDEFGHJKMNPQRSTUVWXYZ';
  const bytes = new Uint8Array(8);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => alfabeto[b % alfabeto.length]).join('');
}

export interface ReporteError {
  referencia: string;
  nombre: string;
  mensaje: string;
  pila: string | null;
  ruta: string;
  version: string;
}

const recortar = (texto: string, maximo: number) => (texto.length > maximo ? texto.slice(0, maximo) : texto);

/** Arma el reporte: solo el `pathname` (el fragmento puede traer el token de una invitación, ATLAS-ADR-0025). */
export function armarReporte(error: unknown, referencia: string, ruta = window.location.pathname): ReporteError {
  const comoError = error instanceof Error ? error : null;
  const respuesta = !comoError && typeof error === 'object' && error !== null && 'status' in error ? (error as { status: number; statusText?: string }) : null;
  return {
    referencia,
    nombre: recortar(comoError?.name ?? (respuesta ? 'RespuestaDeRuta' : typeof error), 100),
    mensaje: recortar(comoError?.message ?? (respuesta ? `${respuesta.status} ${respuesta.statusText ?? ''}`.trim() : String(error)), 500),
    pila: comoError?.stack ? recortar(comoError.stack, 3000) : null,
    ruta: recortar(ruta, 200),
    version: __ATLAS_VERSION__,
  };
}

const reportados = new WeakSet<object>();

/**
 * Si la PWA tiene sesión, según la última respuesta de `/yo` (la marca `sesion.tsx`). Es una variable del módulo y no
 * el contexto de la sesión porque la pantalla de error reemplaza al shell y también sale en las páginas públicas.
 */
let conSesion = false;

export function marcarSesion(activa: boolean): void {
  conSesion = activa;
}

/**
 * Envía el reporte sin esperar ni fallar: si la red o la API no responden, la persona igual tiene su referencia. Sin
 * sesión no envía nada (el servidor respondería 401).
 */
export function reportarError(error: unknown, referencia: string): void {
  if (!conSesion) return;
  if (typeof error === 'object' && error !== null) {
    // React vuelve a pintar el error (y en desarrollo, dos veces): un solo reporte por error.
    if (reportados.has(error)) return;
    reportados.add(error);
  }
  try {
    void fetch(RUTA_ERRORES_CLIENTE, {
      method: 'POST',
      credentials: 'same-origin',
      keepalive: true,
      headers: { 'Content-Type': 'application/json', [CABECERA_CSRF]: '1' },
      body: JSON.stringify(armarReporte(error, referencia)),
    }).catch(() => undefined);
  } catch {
    // Sin fetch o con el cuerpo inválido: no hay nada más que hacer.
  }
}
