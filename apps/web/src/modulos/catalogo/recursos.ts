import type { RecursoSinConexion } from '../../app/modulos/contrato';

/**
 * Lo que el catálogo guarda en el dispositivo para verlo sin conexión (HU 2.12, ATLAS-ADR-0034): el listado completo
 * que la API le devuelve a la persona (sin filtros). Sin red, los filtros y la ficha se arman a partir de él.
 */
export const TTL_HORAS = 72;

export const RECURSO_APLICATIVOS = { clave: 'catalogo.aplicativos', permiso: 'atlas.catalogo.ver', ttlHoras: TTL_HORAS } as const;

export const RECURSOS_SIN_CONEXION: RecursoSinConexion[] = [{ ...RECURSO_APLICATIVOS, ruta: () => '/api/v1/catalogo/aplicativos' }];
