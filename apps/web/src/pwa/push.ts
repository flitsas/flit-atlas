import { darDeBajaSuscripcion, obtenerClavePush, registrarSuscripcion } from '../app/api';

/**
 * Web Push con VAPID (prompt maestro §11): una suscripción por dispositivo, guardada en
 * `plataforma_suscripcion_push`. En iOS solo funciona con Atlas instalado en la pantalla de inicio (iOS 16.4+): se
 * detecta y se guía a la persona en lugar de prometer algo que el dispositivo no da.
 */
export type EstadoPush =
  | 'no_soportado'
  | 'ios_sin_instalar'
  | 'deshabilitado_en_servidor'
  | 'bloqueado'
  | 'inactivo'
  | 'activo';

export function esIos(agente = navigator.userAgent, puntosTactiles = navigator.maxTouchPoints): boolean {
  // iPadOS se presenta como Mac de escritorio; se reconoce por la pantalla táctil.
  return /iPad|iPhone|iPod/.test(agente) || (/Macintosh/.test(agente) && puntosTactiles > 1);
}

export function instalada(): boolean {
  return (
    window.matchMedia?.('(display-mode: standalone)').matches === true ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}

function soportaPush(): boolean {
  return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
}

/** Nombre corto del dispositivo para «tus dispositivos» (sin la cadena completa del navegador). */
export function resumenDispositivo(agente = navigator.userAgent): string {
  const navegador = /Edg\//.test(agente)
    ? 'Edge'
    : /Firefox\//.test(agente)
      ? 'Firefox'
      : /Chrome\//.test(agente)
        ? 'Chrome'
        : /Safari\//.test(agente)
          ? 'Safari'
          : 'Navegador';
  const sistema = /Android/.test(agente)
    ? 'Android'
    : /iPhone|iPad|iPod/.test(agente)
      ? 'iOS'
      : /Windows/.test(agente)
        ? 'Windows'
        : /Mac OS X/.test(agente)
          ? 'macOS'
          : /Linux/.test(agente)
            ? 'Linux'
            : 'otro sistema';
  return `${navegador} en ${sistema}`;
}

export function claveABytes(base64url: string): Uint8Array<ArrayBuffer> {
  const base64 = (base64url + '='.repeat((4 - (base64url.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/');
  const binario = atob(base64);
  const bytes = new Uint8Array(new ArrayBuffer(binario.length));
  for (let i = 0; i < binario.length; i++) bytes[i] = binario.charCodeAt(i);
  return bytes;
}

function mismaClave(suscripcion: PushSubscription, clave: Uint8Array): boolean {
  const actual = suscripcion.options?.applicationServerKey;
  if (!actual) return true; // El navegador no la expone: se asume la misma y el servidor depura las que fallen.
  const bytes = new Uint8Array(actual);
  return bytes.length === clave.length && bytes.every((b, i) => b === clave[i]);
}

async function registroActual(): Promise<ServiceWorkerRegistration | undefined> {
  return navigator.serviceWorker.getRegistration();
}

export async function estadoPush(): Promise<EstadoPush> {
  if (esIos() && !instalada()) return 'ios_sin_instalar';
  if (!soportaPush()) return 'no_soportado';
  if (Notification.permission === 'denied') return 'bloqueado';
  const registro = await registroActual();
  const suscripcion = await registro?.pushManager.getSubscription();
  return suscripcion && Notification.permission === 'granted' ? 'activo' : 'inactivo';
}

async function enviarAlServidor(suscripcion: PushSubscription): Promise<void> {
  const json = suscripcion.toJSON();
  await registrarSuscripcion({
    endpoint: suscripcion.endpoint,
    p256dh: json.keys?.p256dh ?? '',
    auth: json.keys?.auth ?? '',
    dispositivo: resumenDispositivo(),
  });
}

/** Pide permiso, suscribe el dispositivo con la llave VAPID del servidor y lo registra en Atlas. */
export async function activarPush(): Promise<EstadoPush> {
  const estado = await estadoPush();
  if (estado === 'ios_sin_instalar' || estado === 'no_soportado' || estado === 'bloqueado') return estado;

  const permiso = await Notification.requestPermission();
  if (permiso !== 'granted') return permiso === 'denied' ? 'bloqueado' : 'inactivo';

  const { habilitado, clavePublica } = await obtenerClavePush();
  if (!habilitado || !clavePublica) return 'deshabilitado_en_servidor';
  const clave = claveABytes(clavePublica);

  const registro = await navigator.serviceWorker.ready;
  let suscripcion = await registro.pushManager.getSubscription();
  if (suscripcion && !mismaClave(suscripcion, clave)) {
    // La llave VAPID rotó: la suscripción vieja ya no sirve.
    await suscripcion.unsubscribe();
    suscripcion = null;
  }
  suscripcion ??= await registro.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: clave });
  await enviarAlServidor(suscripcion);
  return 'activo';
}

/** Quita la suscripción de este dispositivo en el navegador y en Atlas. También lo usa el cierre de sesión. */
export async function desactivarPush(): Promise<void> {
  if (!soportaPush()) return;
  const registro = await registroActual();
  const suscripcion = await registro?.pushManager.getSubscription();
  if (!suscripcion) return;
  const endpoint = suscripcion.endpoint;
  await suscripcion.unsubscribe();
  await darDeBajaSuscripcion(endpoint).catch(() => undefined);
}

/**
 * Al abrir la app: si el dispositivo ya estaba suscrito, vuelve a informar la suscripción (el servidor la asocia a la
 * persona con sesión) y la renueva si la llave VAPID cambió.
 */
export async function sincronizarPush(): Promise<void> {
  if (!soportaPush() || Notification.permission !== 'granted') return;
  const registro = await registroActual();
  const suscripcion = await registro?.pushManager.getSubscription();
  if (!suscripcion) return;
  const { habilitado, clavePublica } = await obtenerClavePush();
  if (!habilitado || !clavePublica) return;
  if (!mismaClave(suscripcion, claveABytes(clavePublica))) {
    await suscripcion.unsubscribe();
    await activarPush();
    return;
  }
  await enviarAlServidor(suscripcion);
}
