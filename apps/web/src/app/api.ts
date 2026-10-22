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
}

export interface Proveedores {
  microsoft: boolean;
  simulado: boolean;
}

export class ErrorApi extends Error {
  constructor(public readonly estado: number) {
    super(`La API respondió ${estado}`);
  }
}

async function pedir(ruta: string, opciones: RequestInit = {}): Promise<Response> {
  const metodo = (opciones.method ?? 'GET').toUpperCase();
  const cabeceras = new Headers(opciones.headers);
  if (!['GET', 'HEAD', 'OPTIONS'].includes(metodo)) {
    cabeceras.set(CABECERA_CSRF, '1');
  }
  const respuesta = await fetch(ruta, { ...opciones, headers: cabeceras, credentials: 'same-origin' });
  if (!respuesta.ok) {
    throw new ErrorApi(respuesta.status);
  }
  return respuesta;
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

export async function entrarSimulado(correo: string, nombre: string): Promise<void> {
  await pedir('/api/v1/identidad/simulado/entrar', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ correo, nombre }),
  });
}

export async function salir(): Promise<void> {
  await pedir('/api/v1/identidad/salir', { method: 'POST' });
}

/** Navegación completa hacia Entra ID; vuelve a `volver` (solo rutas locales, lo valida el servidor). */
export function urlEntrarMicrosoft(volver = '/'): string {
  return `/api/v1/identidad/entrar/microsoft?volver=${encodeURIComponent(volver)}`;
}
