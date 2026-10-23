import { enviarJson, pedir } from '../../app/api';

/** Cliente de `/api/v1/plataforma/organizaciones` y `/api/v1/plataforma/invitaciones` (HU 1.5). */
export interface Organizacion {
  id: string;
  nombre: string;
  tipo: 'interna' | 'cliente' | 'proveedor';
  estado: 'activa' | 'suspendida';
  adminOrganizacionHabilitado: boolean;
  personas: number;
  invitacionesPendientes: number;
  creadaEn: string;
}

export interface SolicitudOrganizacion {
  nombre: string;
  tipo: 'interna' | 'cliente' | 'proveedor';
  adminOrganizacionHabilitado: boolean;
}

export interface Alcance {
  tipo: string;
  id: string | null;
}

export type EstadoInvitacion = 'pendiente' | 'enlace_vencido' | 'aceptada' | 'acceso_vencido' | 'suspendida' | 'revocada';

export interface Invitacion {
  id: string;
  correo: string;
  nombre: string | null;
  organizacionId: string;
  organizacionNombre: string;
  rol: string;
  rolNombre: string;
  alcance: Alcance[];
  permisosExtra: string[];
  accesoVenceEn: string | null;
  enlaceVenceEn: string;
  estado: EstadoInvitacion;
  personaId: string | null;
  aceptadaEn: string | null;
  enviadaEn: string | null;
  envios: number;
  creadaEn: string;
}

export interface InvitacionEnviada {
  invitacion: Invitacion;
  correoEnviado: boolean;
}

export interface OpcionesInvitacion {
  organizaciones: { id: string; nombre: string; tipo: string; adminOrganizacionHabilitado: boolean }[];
  roles: { codigo: string; nombre: string; descripcion: string }[];
  tiposAlcance: string[];
  permisosExtra: string[];
  vigenciaMaximaDias: number;
  soloSuOrganizacion: boolean;
}

export interface SolicitudInvitacion {
  correo: string;
  nombre: string | null;
  organizacionId: string;
  rol: string;
  alcance: Alcance[];
  accesoVenceEn: string | null;
  permisosExtra: string[];
}

const ORGANIZACIONES = '/api/v1/plataforma/organizaciones';
const INVITACIONES = '/api/v1/plataforma/invitaciones';
const json = async <T>(respuesta: Response) => (await respuesta.json()) as T;

export const listarOrganizaciones = async () => json<Organizacion[]>(await pedir(ORGANIZACIONES));
export const crearOrganizacion = async (s: SolicitudOrganizacion) => json<Organizacion>(await enviarJson(ORGANIZACIONES, 'POST', s));
export const actualizarOrganizacion = async (id: string, s: SolicitudOrganizacion) =>
  json<Organizacion>(await enviarJson(`${ORGANIZACIONES}/${id}`, 'PUT', s));
export const cambiarEstadoOrganizacion = async (id: string, accion: 'suspender' | 'reactivar') =>
  json<Organizacion>(await enviarJson(`${ORGANIZACIONES}/${id}/${accion}`, 'POST'));
export const eliminarOrganizacion = async (id: string) => {
  await enviarJson(`${ORGANIZACIONES}/${id}`, 'DELETE');
};

export const listarInvitaciones = async () => json<Invitacion[]>(await pedir(INVITACIONES));
export const opcionesInvitacion = async () => json<OpcionesInvitacion>(await pedir(`${INVITACIONES}/opciones`));
export const crearInvitacion = async (s: SolicitudInvitacion) => json<InvitacionEnviada>(await enviarJson(INVITACIONES, 'POST', s));
export const reenviarInvitacion = async (id: string) => json<InvitacionEnviada>(await enviarJson(`${INVITACIONES}/${id}/reenviar`, 'POST'));
export const suspenderInvitacion = async (id: string) => json<Invitacion>(await enviarJson(`${INVITACIONES}/${id}/suspender`, 'POST'));
export const renovarInvitacion = async (id: string, accesoVenceEn: string | null) =>
  json<InvitacionEnviada>(await enviarJson(`${INVITACIONES}/${id}/renovar`, 'POST', { accesoVenceEn }));
export const revocarInvitacion = async (id: string) => json<Invitacion>(await enviarJson(`${INVITACIONES}/${id}/revocar`, 'POST'));
export const restablecerCuenta = async (personaId: string) =>
  json<{ correoEnviado: boolean }>(await enviarJson(`/api/v1/identidad/admin/personas/${personaId}/restablecer`, 'POST'));

/** Fecha `aaaa-mm-dd` de un `<input type="date">` → fin de ese día en Bogotá (UTC−5), en ISO 8601. */
export const finDelDiaBogota = (fecha: string): string | null => (fecha ? `${fecha}T23:59:00-05:00` : null);
