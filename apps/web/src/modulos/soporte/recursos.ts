import type { RecursoSinConexion } from '../../app/modulos/contrato';
import { type Dia, hoy, sumarDias, sumarMeses } from './fechas';

/**
 * Lo que soporte guarda en el dispositivo para verlo sin conexión (HU 2.12, ATLAS-ADR-0034): el calendario de todos los
 * equipos de hoy a tres meses, el soporte de este sábado y los equipos. Lo usan el manifiesto (el shell lo sincroniza) y
 * el cliente (lo lee sin red). Sin teléfonos.
 */
export const TTL_HORAS = 72;

/** Los tres meses del calendario: el mismo rango de la vista en lista. */
export function tresMeses(desde: Dia = hoy()): { desde: Dia; hasta: Dia } {
  return { desde, hasta: sumarDias(sumarMeses(desde, 3), -1) };
}

const ver = 'atlas.soporte.ver';

export const RECURSO_TURNOS = { clave: 'soporte.turnos', permiso: ver, ttlHoras: TTL_HORAS } as const;
export const RECURSO_ESTE_SABADO = { clave: 'soporte.este-sabado', permiso: ver, ttlHoras: TTL_HORAS } as const;
export const RECURSO_EQUIPOS = { clave: 'soporte.equipos', permiso: ver, ttlHoras: TTL_HORAS } as const;

export const RECURSOS_SIN_CONEXION: RecursoSinConexion[] = [
  {
    ...RECURSO_TURNOS,
    ruta: () => {
      const { desde, hasta } = tresMeses();
      return `/api/v1/soporte/turnos?desde=${desde}&hasta=${hasta}`;
    },
  },
  { ...RECURSO_ESTE_SABADO, ruta: () => '/api/v1/soporte/este-sabado' },
  { ...RECURSO_EQUIPOS, ruta: () => '/api/v1/soporte/equipos' },
];
