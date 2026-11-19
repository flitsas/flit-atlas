import { enviarJson } from '../api';

/**
 * Cuentas locales de externos (HU 1.5, ATLAS-ADR-0025): aceptar invitación, ingreso con passkey o contraseña + TOTP y
 * recuperación. Los tokens de los enlaces llegan en el fragmento de la URL y solo viajan en el cuerpo de un POST.
 */
export interface DocumentoVigente {
  documento: 'terminos' | 'privacidad';
  version: string;
  titulo: string;
  texto: string;
  hash: string;
}

export interface InvitacionPublica {
  correo: string;
  nombre: string | null;
  organizacion: string;
  rol: string;
  rolNombre: string;
  accesoVenceEn: string | null;
  enlaceVenceEn: string;
  documentos: DocumentoVigente[];
}

export interface OpcionesPasskey {
  estado: string;
  opciones: string;
}

export interface ClaveTotp {
  estado: string;
  clave: string;
  uri: string;
  qr: string;
}

export interface CuentaLista {
  codigosRecuperacion: string[];
  sesionIniciada: boolean;
}

export type Factor =
  | { metodo: 'passkey'; estado: string; credencial: string }
  | { metodo: 'contrasena'; estado: string; contrasena: string; codigo: string };

export interface AceptacionDocumento {
  documento: string;
  version: string;
}

export interface TerminosPendientes {
  error: 'terminos_pendientes';
  estado: string;
  documentos: DocumentoVigente[];
}

const json = async <T>(respuesta: Response) => (await respuesta.json()) as T;
const post = (ruta: string, cuerpo: unknown) => enviarJson(ruta, 'POST', cuerpo);

export const aceptaciones = (documentos: DocumentoVigente[]): AceptacionDocumento[] =>
  documentos.map((d) => ({ documento: d.documento, version: d.version }));

// --- Invitación ---

export const consultarInvitacion = async (token: string) =>
  json<InvitacionPublica>(await post('/api/v1/identidad/invitaciones/consultar', { token }));

export const opcionesPasskeyInvitacion = async (token: string, nombre: string) =>
  json<OpcionesPasskey>(await post('/api/v1/identidad/invitaciones/passkey/opciones', { token, nombre }));

export const claveTotpInvitacion = async (token: string) =>
  json<ClaveTotp>(await post('/api/v1/identidad/invitaciones/totp/clave', { token }));

export const aceptarInvitacion = async (token: string, nombre: string, documentos: AceptacionDocumento[], factor: Factor) =>
  json<CuentaLista>(await post('/api/v1/identidad/invitaciones/aceptar', { token, nombre, documentos, factor }));

// --- Ingreso ---

/** Paso 1 con contraseña: nunca abre sesión, devuelve el paso del TOTP. */
export const entrarConContrasena = async (correo: string, contrasena: string) =>
  json<{ paso: 'totp'; estado: string }>(await post('/api/v1/identidad/entrar/local', { correo, contrasena }));

export const entrarConSegundoFactor = (estado: string, codigo?: string, codigoRecuperacion?: string) =>
  post('/api/v1/identidad/entrar/local/segundo-factor', { estado, codigo, codigoRecuperacion });

export const opcionesPasskeyIngreso = async () => json<OpcionesPasskey>(await post('/api/v1/identidad/entrar/passkey/opciones', {}));

export const entrarConPasskey = (estado: string, credencial: string) => post('/api/v1/identidad/entrar/passkey', { estado, credencial });

export const aceptarTerminosAlEntrar = (estado: string, documentos: AceptacionDocumento[]) =>
  post('/api/v1/identidad/entrar/terminos', { estado, documentos });

// --- Recuperación ---

export const solicitarRecuperacion = (correo: string) => post('/api/v1/identidad/recuperacion', { correo });

export const consultarRecuperacion = async (enlace: string) =>
  json<{ correo: string; nombre: string }>(await post('/api/v1/identidad/recuperacion/consultar', { enlace }));

export const opcionesPasskeyRecuperacion = async (enlace: string) =>
  json<OpcionesPasskey>(await post('/api/v1/identidad/recuperacion/passkey/opciones', { enlace }));

export const claveTotpRecuperacion = async (enlace: string) =>
  json<ClaveTotp>(await post('/api/v1/identidad/recuperacion/totp/clave', { enlace }));

export const completarRecuperacion = async (enlace: string, factor: Factor) =>
  json<CuentaLista>(await post('/api/v1/identidad/recuperacion/completar', { enlace, factor }));
