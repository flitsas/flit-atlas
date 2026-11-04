import type { RecursoSinConexion } from '../../app/modulos/contrato';

/**
 * Lo que equipo guarda en el dispositivo para verlo sin conexión (HU 2.12, ATLAS-ADR-0034): el reparto vigente de
 * responsables tal como la API lo devolvió a la persona (sin teléfonos). Con él se arman sin red los responsables y
 * «¿A quién llamo?» de cada aplicativo.
 */
export const TTL_HORAS = 72;

export const RECURSO_REPARTO = { clave: 'equipo.responsabilidades', permiso: 'atlas.equipo.ver', ttlHoras: TTL_HORAS } as const;

export const RECURSOS_SIN_CONEXION: RecursoSinConexion[] = [{ ...RECURSO_REPARTO, ruta: () => '/api/v1/equipo/responsabilidades' }];
