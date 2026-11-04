/**
 * Cliente de la API de Atlas. La sesión va en la cookie HttpOnly del BFF (ATLAS-ADR-0008): la PWA nunca ve ni guarda
 * tokens. Las peticiones que cambian estado llevan la cabecera antifalsificación que exige atlas-api.
 */
export const CABECERA_CSRF = 'X-Atlas-Csrf';

export interface Yo {
  sub: string;
  personaId: string;
  nombre: string;
  tipoPrincipal: 'empleado' | 'externo';
  organizacionId: string;
  proveedor: string;
  roles: string[];
  /** Llega con la HU 1.4. Mientras no venga, la navegación solo se filtra por bandera. */
  permisos?: string[];
  /** Sesión de la cuenta de administrador de emergencia (ATLAS-ADR-0027): el shell muestra el aviso. */
  emergencia?: boolean;
  /** URL de la foto de la persona (Graph, ATLAS-ADR-0030), versionada por su hash; `null` si no tiene. */
  foto?: string | null;
}

export interface Proveedores {
  microsoft: boolean;
  simulado: boolean;
}

export class ErrorApi extends Error {
  constructor(
    public readonly estado: number,
    /** Cuerpo JSON de la respuesta de error, si lo hay (problem+json o `{ error, mensaje }`). */
    public readonly cuerpo?: Record<string, unknown>,
  ) {
    super(`La API respondió ${estado}`);
  }

  /** Código estable del error (`error` o `codigo` del cuerpo). */
  get codigo(): string | undefined {
    const valor = this.cuerpo?.error ?? this.cuerpo?.codigo;
    return typeof valor === 'string' ? valor : undefined;
  }

  /** Mensaje para la persona: el del servidor o, si no hay, `porDefecto`. */
  mensajePara(porDefecto: string): string {
    const errores = this.cuerpo?.errors as Record<string, string[]> | undefined;
    const primero = errores ? Object.values(errores)[0]?.[0] : undefined;
    const valor = this.cuerpo?.mensaje ?? primero ?? this.cuerpo?.title;
    return typeof valor === 'string' ? valor : porDefecto;
  }
}

/** Petición a atlas-api con la cookie del BFF y la cabecera antifalsificación; los módulos la usan para sus endpoints. */
export async function pedir(ruta: string, opciones: RequestInit = {}): Promise<Response> {
  const metodo = (opciones.method ?? 'GET').toUpperCase();
  const cabeceras = new Headers(opciones.headers);
  if (!['GET', 'HEAD', 'OPTIONS'].includes(metodo)) {
    cabeceras.set(CABECERA_CSRF, '1');
  }
  const respuesta = await fetch(ruta, { ...opciones, headers: cabeceras, credentials: 'same-origin' });
  if (!respuesta.ok) {
    const cuerpo = (respuesta.headers.get('Content-Type') ?? '').includes('json')
      ? ((await respuesta.json().catch(() => undefined)) as Record<string, unknown> | undefined)
      : undefined;
    throw new ErrorApi(respuesta.status, cuerpo);
  }
  return respuesta;
}

export async function enviarJson(ruta: string, metodo: 'POST' | 'PUT' | 'DELETE', cuerpo?: unknown): Promise<Response> {
  return pedir(ruta, {
    method: metodo,
    headers: cuerpo === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
  });
}

/** Persona con sesión, o `null` si no hay sesión (401). */
export async function obtenerYo(): Promise<Yo | null> {
  try {
    return (await (await pedir('/api/v1/plataforma/yo')).json()) as Yo;
  } catch (error) {
    if (error instanceof ErrorApi && error.estado === 401) return null;
    throw error;
  }
}

export async function obtenerProveedores(): Promise<Proveedores> {
  return (await (await pedir('/api/v1/identidad/proveedores')).json()) as Proveedores;
}

export async function entrarSimulado(correo: string, nombre: string, administrador = false): Promise<void> {
  await enviarJson('/api/v1/identidad/simulado/entrar', 'POST', { correo, nombre, administrador });
}

export async function salir(): Promise<void> {
  await pedir('/api/v1/identidad/salir', { method: 'POST' });
}

/** Navegación completa hacia Entra ID; vuelve a `volver` (solo rutas locales, lo valida el servidor). */
export function urlEntrarMicrosoft(volver = '/'): string {
  return `/api/v1/identidad/entrar/microsoft?volver=${encodeURIComponent(volver)}`;
}

// --- Banderas de módulo (ATLAS-ADR-0016) ---

/** `modulo.<id>` → activo para la organización de la persona. Las banderas que no vengan usan su valor por defecto. */
export async function obtenerBanderas(): Promise<Record<string, boolean>> {
  return (await (await pedir('/api/v1/plataforma/banderas')).json()) as Record<string, boolean>;
}

// --- Centro de notificaciones (§8.9) ---

export interface Notificacion {
  id: string;
  tipo: string;
  titulo: string;
  cuerpo: string;
  enlace: string | null;
  creadaEn: string;
  leidaEn: string | null;
}

export interface BandejaNotificaciones {
  noLeidas: number;
  elementos: Notificacion[];
}

export type Canal = 'app' | 'push' | 'correo' | 'teams';

export interface PreferenciaCanal {
  canal: Canal;
  activa: boolean;
  /** `false` mientras el canal no exista en este ambiente (correo sin proveedor). Teams nunca: no hay aviso personal por Teams (ATLAS-ADR-0033). */
  disponible: boolean;
  /** El canal `app` siempre está activo: es el centro de notificaciones. */
  editable: boolean;
}

export interface PreferenciaTipo {
  tipo: string;
  nombre: string;
  descripcion: string;
  canales: PreferenciaCanal[];
}

export async function obtenerNotificaciones(limite = 30): Promise<BandejaNotificaciones> {
  return (await (await pedir(`/api/v1/plataforma/notificaciones?limite=${limite}`)).json()) as BandejaNotificaciones;
}

export async function marcarLeida(id: string): Promise<void> {
  await enviarJson(`/api/v1/plataforma/notificaciones/${encodeURIComponent(id)}/leida`, 'POST');
}

export async function marcarTodasLeidas(): Promise<void> {
  await enviarJson('/api/v1/plataforma/notificaciones/leidas', 'POST');
}

export async function obtenerPreferencias(): Promise<PreferenciaTipo[]> {
  return (await (await pedir('/api/v1/plataforma/notificaciones/preferencias')).json()) as PreferenciaTipo[];
}

export async function guardarPreferencia(tipo: string, canal: Canal, activa: boolean): Promise<void> {
  await enviarJson('/api/v1/plataforma/notificaciones/preferencias', 'PUT', [{ tipo, canal, activa }]);
}

// --- Web Push (VAPID, §11) ---

export interface ClavePush {
  habilitado: boolean;
  clavePublica: string | null;
}

export interface SuscripcionDispositivo {
  endpoint: string;
  p256dh: string;
  auth: string;
  dispositivo: string;
}

export async function obtenerClavePush(): Promise<ClavePush> {
  return (await (await pedir('/api/v1/plataforma/push/clave')).json()) as ClavePush;
}

export async function registrarSuscripcion(suscripcion: SuscripcionDispositivo): Promise<void> {
  await enviarJson('/api/v1/plataforma/push/suscripciones', 'POST', suscripcion);
}

export async function darDeBajaSuscripcion(endpoint: string): Promise<void> {
  await enviarJson('/api/v1/plataforma/push/suscripciones/baja', 'POST', { endpoint });
}

/** Crea una notificación de prueba y la envía por push a los dispositivos de la persona. */
export async function enviarPushDePrueba(): Promise<{ dispositivos: number }> {
  return (await (await enviarJson('/api/v1/plataforma/push/prueba', 'POST')).json()) as { dispositivos: number };
}
