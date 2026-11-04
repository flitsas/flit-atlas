/** Formatos de Colombia (prompt maestro §1): fechas dd/mm/aaaa y hora de Bogotá. */
const ZONA = 'America/Bogota';

const FECHA = new Intl.DateTimeFormat('es-CO', { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: ZONA });
const HORA = new Intl.DateTimeFormat('es-CO', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: ZONA });
const DIA = new Intl.DateTimeFormat('es-CO', { weekday: 'long', timeZone: ZONA });

export const formatearFecha = (fecha: Date): string => FECHA.format(fecha);

export const formatearFechaHora = (fecha: Date): string => `${FECHA.format(fecha)} ${HORA.format(fecha)}`;

/** «jueves 22/10/2026». */
export const formatearDia = (fecha: Date): string => `${DIA.format(fecha)} ${FECHA.format(fecha)}`;

/** «hace 5 min», «hace 3 h» o la fecha, para el centro de notificaciones. */
export function formatearRelativo(fecha: Date, ahora = new Date()): string {
  const minutos = Math.round((ahora.getTime() - fecha.getTime()) / 60_000);
  if (minutos < 1) return 'ahora';
  if (minutos < 60) return `hace ${minutos} min`;
  const horas = Math.round(minutos / 60);
  if (horas < 24) return `hace ${horas} h`;
  return formatearFecha(fecha);
}

/** Próximo sábado (hoy, si hoy es sábado) en la hora de Bogotá. */
export function proximoSabado(desde = new Date()): Date {
  const bogota = new Date(desde.toLocaleString('en-US', { timeZone: ZONA }));
  const dias = (6 - bogota.getDay() + 7) % 7;
  return new Date(desde.getTime() + dias * 86_400_000);
}

const NOMBRES_ROL: Record<string, string> = {
  'atlas.superadmin': 'Superadministración',
  'atlas.lider_tecnico': 'Líder técnico',
  'atlas.desarrollador': 'Desarrollo',
  'atlas.soporte': 'Soporte',
  'atlas.negocio': 'Negocio',
  'atlas.gobierno_ia': 'Gobierno de IA',
  'atlas.auditor': 'Auditoría',
  'atlas.lector': 'Lectura',
  'ext.integrador': 'Integrador',
  'ext.cliente_funcional': 'Cliente funcional',
  'ext.asesor_comercial': 'Asesor comercial',
  'ext.proveedor': 'Proveedor',
  'ext.admin_organizacion': 'Administración de la organización',
};

/** Nombre legible de un rol del §7; si no se conoce, el código tal cual. */
export const nombreRol = (codigo: string): string => NOMBRES_ROL[codigo] ?? codigo;

export function iniciales(nombre: string): string {
  const partes = nombre.trim().split(/\s+/).filter(Boolean);
  const letras = partes.length > 1 ? [partes[0], partes[partes.length - 1]] : partes;
  return letras.map((p) => p![0]!.toUpperCase()).join('') || '?';
}

export const primerNombre = (nombre: string): string => nombre.trim().split(/\s+/)[0] ?? nombre;
