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

// --- Roles: mapeos de grupos de Entra y asignaciones (HU 1.4, ATLAS-ADR-0023; endpoints de la etiqueta «Permisos») ---

/** `GET /api/v1/plataforma/roles` (`RolConPermisos`). */
export interface RolAtlas {
  codigo: string;
  nombre: string;
  tipo: 'interno' | 'externo' | 'implicito';
  descripcion: string;
  /** Los implícitos y `atlas.lector` no se asignan ni se mapean. */
  asignable: boolean;
  permisos: string[];
}

export interface MapeoGrupo {
  id: string;
  /** Object id del grupo en Entra ID. */
  grupoId: string;
  grupoNombre: string;
  rol: string;
  creadoEn: string;
}

export interface SolicitudMapeo {
  grupoId: string;
  grupoNombre: string;
  rol: string;
}

export type OrigenAsignacion = 'manual' | 'grupo_entra' | 'invitacion' | 'emergencia';

export interface Asignacion {
  id: string;
  personaId: string;
  rol: string;
  rolNombre: string;
  tipoRol: 'interno' | 'externo';
  organizacionId: string | null;
  alcance: { tipo: string; id: string | null };
  vigenteDesde: string;
  vigenteHasta: string | null;
  origen: OrigenAsignacion;
  vigente: boolean;
  asignadoPor: string | null;
  creadoEn: string;
}

const PLATAFORMA = '/api/v1/plataforma';

export const listarRoles = async () => json<RolAtlas[]>(await pedir(`${PLATAFORMA}/roles`));
export const listarMapeos = async () => json<MapeoGrupo[]>(await pedir(`${PLATAFORMA}/mapeos-grupos-entra`));
export const crearMapeo = async (s: SolicitudMapeo) => json<MapeoGrupo>(await enviarJson(`${PLATAFORMA}/mapeos-grupos-entra`, 'POST', s));
export const eliminarMapeo = async (id: string) => {
  await enviarJson(`${PLATAFORMA}/mapeos-grupos-entra/${encodeURIComponent(id)}`, 'DELETE');
};
/** Con `todas`, también las vencidas y las que aún no empiezan. */
export const listarAsignaciones = async (personaId: string, todas = false) =>
  json<Asignacion[]>(await pedir(`${PLATAFORMA}/personas/${encodeURIComponent(personaId)}/asignaciones${todas ? '?todas=true' : ''}`));
export const retirarAsignacion = async (id: string) => {
  await enviarJson(`${PLATAFORMA}/asignaciones/${encodeURIComponent(id)}`, 'DELETE');
};

/** Rol interno asignado a mano: global, en la organización de la persona y con vencimiento opcional. */
export interface SolicitudAsignacion {
  rol: string;
  vigenteHasta: string | null;
}

/** `POST /personas/{id}/asignaciones`: exige `atlas.roles.gestionar` y un ingreso reciente (`reautenticacion_requerida`). */
export const asignarRol = async (personaId: string, s: SolicitudAsignacion) =>
  json<Asignacion>(await enviarJson(`${PLATAFORMA}/personas/${encodeURIComponent(personaId)}/asignaciones`, 'POST', s));

// --- Buscador de personas (`GET /api/v1/plataforma/personas`, `atlas.roles.ver`) ---

export interface PersonaEncontrada {
  id: string;
  nombre: string;
  correo: string;
  tipo: 'empleado' | 'externo';
  organizacion: { id: string; nombre: string };
  estado: 'activa' | 'inactiva' | 'suspendida';
  /** Cuenta de administrador de emergencia (ATLAS-ADR-0027). */
  emergencia: boolean;
  /** URL de la foto (empleados sincronizados con Graph, ATLAS-ADR-0030); `null` si no tiene. */
  foto: string | null;
  /** Códigos de los roles asignados y vigentes (sin `atlas.lector` ni los implícitos). */
  roles: string[];
}

export interface ResultadoBusquedaPersonas {
  personas: PersonaEncontrada[];
  /** Hay más que el límite: la persona debe afinar la búsqueda. */
  hayMas: boolean;
}

/** Mínimo de caracteres que exige el servidor (sin espacios a los lados). */
export const BUSQUEDA_MINIMA = 2;

/** Por nombre o correo, sin mayúsculas ni tildes, o por id exacto. Ordenado por nombre, hasta `limite` (máximo 50). */
export const buscarPersonas = async (q: string, limite = 20, signal?: AbortSignal) =>
  json<ResultadoBusquedaPersonas>(
    await pedir(`${PLATAFORMA}/personas?q=${encodeURIComponent(q.trim())}&limite=${limite}`, signal ? { signal } : {}),
  );

/** Roles que un grupo de Entra puede otorgar: internos y asignables (`atlas.lector` ya lo tiene todo empleado). */
export const rolesMapeables = (roles: RolAtlas[]): RolAtlas[] => roles.filter((r) => r.tipo === 'interno' && r.asignable);

/** Object id de Entra ID (o id de persona de Atlas): un GUID. */
export const esGuid = (valor: string): boolean => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(valor.trim());

/** Por qué una asignación no se retira desde esta pantalla, o `null` si se puede retirar (las manuales). */
export function motivoNoRetirable(a: Pick<Asignacion, 'origen'>): string | null {
  switch (a.origen) {
    case 'grupo_entra':
      return 'Viene de un grupo de Entra: se quita sacando a la persona del grupo en Entra ID o eliminando el mapeo.';
    case 'emergencia':
      return 'Es la cuenta de administrador de emergencia: no se desarma desde Atlas; se rota con el comando de consola.';
    case 'invitacion':
      return 'Viene de una invitación: se retira revocando la invitación en Invitaciones.';
    default:
      return null;
  }
}
