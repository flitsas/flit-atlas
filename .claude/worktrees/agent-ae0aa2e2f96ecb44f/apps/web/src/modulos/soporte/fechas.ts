/**
 * Fechas del soporte como días de Bogotá en texto `aaaa-mm-dd` (el formato de la API). Se calculan con UTC para que el
 * huso del dispositivo no corra un día; «hoy» sale de la hora de Bogotá (prompt maestro §1).
 */
export type Dia = string;

const ZONA = 'America/Bogota';
const HOY_BOGOTA = new Intl.DateTimeFormat('en-CA', { timeZone: ZONA, year: 'numeric', month: '2-digit', day: '2-digit' });

export const MESES = [
  'enero',
  'febrero',
  'marzo',
  'abril',
  'mayo',
  'junio',
  'julio',
  'agosto',
  'septiembre',
  'octubre',
  'noviembre',
  'diciembre',
] as const;

/** Lunes primero, como el calendario colombiano. */
export const DIAS_CORTOS = ['lun', 'mar', 'mié', 'jue', 'vie', 'sáb', 'dom'] as const;
const DIAS = ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'] as const;

/** Hoy en Bogotá. */
export const hoy = (ahora = new Date()): Dia => HOY_BOGOTA.format(ahora);

const aUtc = (dia: Dia): Date => {
  const [a, m, d] = dia.split('-').map(Number);
  return new Date(Date.UTC(a!, m! - 1, d!));
};

const deUtc = (fecha: Date): Dia => fecha.toISOString().slice(0, 10);

export const sumarDias = (dia: Dia, dias: number): Dia => {
  const fecha = aUtc(dia);
  fecha.setUTCDate(fecha.getUTCDate() + dias);
  return deUtc(fecha);
};

export const sumarMeses = (dia: Dia, meses: number): Dia => {
  const fecha = aUtc(dia);
  fecha.setUTCDate(1);
  fecha.setUTCMonth(fecha.getUTCMonth() + meses);
  return deUtc(fecha);
};

/** 0 = domingo … 6 = sábado. */
export const diaSemana = (dia: Dia): number => aUtc(dia).getUTCDay();

/** El mismo día si es sábado; si no, el sábado siguiente. */
export const sabadoDesde = (dia: Dia): Dia => sumarDias(dia, (6 - diaSemana(dia) + 7) % 7);

/** El lunes de la semana del día. */
export const lunesDe = (dia: Dia): Dia => sumarDias(dia, -((diaSemana(dia) + 6) % 7));

export const primeroDelMes = (dia: Dia): Dia => `${dia.slice(0, 7)}-01`;

export const ultimoDelMes = (dia: Dia): Dia => sumarDias(sumarMeses(primeroDelMes(dia), 1), -1);

/** «24/10/2026». */
export const formatearDia = (dia: Dia): string => `${dia.slice(8, 10)}/${dia.slice(5, 7)}/${dia.slice(0, 4)}`;

/** «sábado 24/10/2026». */
export const formatearDiaLargo = (dia: Dia): string => `${DIAS[diaSemana(dia)]} ${formatearDia(dia)}`;

/** «octubre de 2026». */
export const formatearMes = (dia: Dia): string => `${MESES[Number(dia.slice(5, 7)) - 1]} de ${dia.slice(0, 4)}`;

/** Días de la cuadrícula del mes: de lunes a domingo, completando la primera y la última semana. */
export function cuadriculaDelMes(dia: Dia): Dia[] {
  const inicio = lunesDe(primeroDelMes(dia));
  const fin = sumarDias(lunesDe(ultimoDelMes(dia)), 6);
  const dias: Dia[] = [];
  for (let d = inicio; d <= fin; d = sumarDias(d, 1)) dias.push(d);
  return dias;
}
