import { useQuery } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../app/api';
import type { Dia } from './fechas';

/** Contrato de `/api/v1/soporte` (contracts/openapi/atlas.v1.yaml, etiqueta «Soporte»). Horas HH:mm de Bogotá. */
export type EstadoTurno = 'programado' | 'confirmado' | 'aplicado' | 'reemplazado' | 'cancelado';
export type OrigenTurno = 'importado' | 'generado' | 'manual';
export type ReglaFestivos = 'sin_turno' | 'turno_normal';
export type TipoAusencia = 'vacaciones' | 'incapacidad' | 'otra';

export interface Equipo {
  id: string;
  nombre: string;
  area: string | null;
  personasMinimo: number;
  personasMaximo: number;
  horaInicio: string | null;
  horaFin: string | null;
  /** «08:00 a 12:00»: el propio o el global. */
  horarioEfectivo: string;
  activo: boolean;
  /** `atlas.soporte.gestionar` global o con alcance a este equipo. */
  puedeGestionar: boolean;
  /** `atlas.soporte.aprobar` global o con alcance a este equipo. */
  puedeAprobar: boolean;
}

export interface CambioEquipo {
  nombre: string;
  area: string | null;
  personasMinimo: number;
  personasMaximo: number;
  horaInicio: string | null;
  horaFin: string | null;
  activo: boolean;
}

export interface EquipoResumen {
  id: string;
  nombre: string;
}

export interface PersonaSoporte {
  id: string;
  nombre: string;
  correo: string | null;
  cargo: string | null;
  /** Solo con `atlas.equipo.ver_contacto`. */
  telefono: string | null;
}

export interface Turno {
  id: string;
  equipo: EquipoResumen;
  fecha: Dia;
  persona: PersonaSoporte;
  horaInicio: string | null;
  horaFin: string | null;
  estado: EstadoTurno;
  origen: OrigenTurno;
  turnoReemplazadoId: string | null;
  festivo: string | null;
  observacion: string | null;
  requiereRevision: boolean;
}

export interface Festivo {
  fecha: Dia;
  nombre: string;
  tipo: 'fijo' | 'trasladable_al_lunes' | 'semana_santa' | 'relativo_a_pascua' | 'adicional';
  trasladado: boolean;
  id: string | null;
}

export interface Calendario {
  desde: Dia;
  hasta: Dia;
  equipoId: string | null;
  turnos: Turno[];
  festivos: Festivo[];
}

export interface EsteSabado {
  fecha: Dia;
  festivo: string | null;
  equipos: { equipo: EquipoResumen; turnos: Turno[]; faltan: number }[];
}

export interface CanalSoporte {
  fecha: Dia;
  configurado: boolean;
  canal: string | null;
  horaInicio: string | null;
  horaFin: string | null;
  haySoporte: boolean;
}

export interface Configuracion {
  horaInicioPorDefecto: string;
  horaFinPorDefecto: string;
  reglaFestivos: ReglaFestivos;
  diasAnticipacionRecordatorio: number | null;
  canalExterno: string | null;
  actualizadoEn: string | null;
}

export interface CambioConfiguracion {
  horaInicioPorDefecto: string;
  horaFinPorDefecto: string;
  reglaFestivos: ReglaFestivos;
  diasAnticipacionRecordatorio: number | null;
  canalExterno: string | null;
}

export interface Participante {
  id: string;
  equipoId: string;
  personaId: string;
  nombre: string;
  correo: string;
  orden: number;
  activo: boolean;
  estadoPersona: string;
}

export interface Candidato {
  id: string;
  nombre: string;
  correo: string;
  cargo: string | null;
}

export interface Ausencia {
  id: string;
  personaId: string;
  nombre: string;
  desde: Dia;
  hasta: Dia;
  tipo: TipoAusencia;
}

export interface SabadoPrevia {
  fecha: Dia;
  decision: 'asignado' | 'fijo' | 'festivo' | 'sin_disponibles';
  personas: { personaId: string; nombre: string; nueva: boolean }[];
  festivo: string | null;
  cambia: boolean;
  ausenteEnFijo: boolean;
  faltan: number;
}

export interface VistaPrevia {
  equipo: EquipoResumen;
  desde: Dia;
  hasta: Dia;
  huella: string;
  sabados: SabadoPrevia[];
  turnosPorPersona: { personaId: string; nombre: string; turnos: number }[];
  desbalance: number;
  nuevos: number;
  eliminados: number;
  avisos: string[];
}

export interface ResultadoGeneracion {
  generacionId: string;
  creados: number;
  eliminados: number;
}

export interface IndicadorPersona {
  personaId: string;
  nombre: string;
  turnos: number;
  aplicados: number;
  reemplazados: number;
  reemplazosHechos: number;
  cancelados: number;
  compensacionesPendientes: number;
  cumplimiento: number | null;
}

export interface Indicadores {
  desde: Dia;
  hasta: Dia;
  equipoId: string | null;
  personas: IndicadorPersona[];
  turnos: number;
  reemplazos: number;
  compensacionesPendientes: number;
  cumplimiento: number | null;
}

// --- Importador del Excel (HU 2.7) ---

export interface NombreImportacion {
  texto: string;
  clave: string;
  coincidencia: 'exacta' | 'parcial' | 'ambigua' | 'ninguna';
  personaId: string | null;
  nombre: string | null;
  nueva: boolean;
}

export interface FilaImportacion {
  fila: number;
  fecha: Dia | null;
  textoFecha: string | null;
  responsable: NombreImportacion | null;
  estado: EstadoTurno;
  reemplazo: NombreImportacion | null;
  compensacion: { para: 'responsable' | 'reemplazo'; fecha: Dia | null; estado: EstadoCompensacion } | null;
  observacion: string | null;
  requiereRevision: boolean;
  accion: 'crear' | 'ya_importado' | 'omitir';
  motivo: string | null;
  interpretaciones: string[];
  ambiguedades: string[];
}

export interface ResumenImportacion {
  filas: number;
  importadas: number;
  yaImportadas: number;
  omitidas: number;
  turnos: number;
  compensaciones: number;
  personasInactivas: number;
  participantes: number;
  generadosQuitados: number;
  interpretaciones: number;
  ambiguos: number;
}

export interface VistaPreviaImportacion {
  equipo: EquipoResumen;
  hoja: string;
  huella: string;
  resumen: ResumenImportacion;
  filas: FilaImportacion[];
  personasNuevas: { clave: string; nombre: string; filas: number; ultimaFecha: Dia | null; confirmada: boolean }[];
  nombresAmbiguos: { clave: string; texto: string; candidatas: { id: string; nombre: string; activa: boolean }[]; elegida: string | null }[];
  participantesNuevos: { personaId: string | null; nombre: string; activo: boolean }[];
  avisos: string[];
}

export interface SolicitudImportacion {
  equipoId: string | null;
  nombreArchivo: string | null;
  contenido: string;
  crearInactivas: string[];
  elegidas: { clave: string; personaId: string }[];
  agregarParticipantes: boolean;
  huella?: string;
}

// --- Cambios de turno y compensaciones (HU 2.9) ---

export type EstadoSolicitud = 'pendiente_reemplazo' | 'pendiente_aprobacion' | 'aprobada' | 'rechazada' | 'cancelada';
export type EstadoCompensacion = 'pendiente' | 'tomada';

export interface SolicitudCambio {
  id: string;
  turno: Turno;
  solicitante: PersonaSoporte;
  reemplazo: PersonaSoporte;
  estado: EstadoSolicitud;
  motivo: string | null;
  turnoNuevoId: string | null;
  resueltaPor: PersonaSoporte | null;
  resueltaEn: string | null;
  creadaEn: string;
  historial: { accion: string; persona: { id: string; nombre: string }; nota: string | null; ocurridoEn: string; hechaEn: string | null }[];
  puedeResponder: boolean;
  puedeResolver: boolean;
  puedeCancelar: boolean;
}

export interface Compensacion {
  id: string;
  turno: { id: string; fecha: Dia; equipo: EquipoResumen };
  persona: PersonaSoporte;
  fechaCompensada: Dia | null;
  estado: EstadoCompensacion;
  aprobadaPor: PersonaSoporte | null;
  nota: string | null;
  puedeCambiar: boolean;
  creadaEn: string;
}

export interface ReporteCompensaciones {
  equipoId: string | null;
  total: number;
  personas: { persona: PersonaSoporte; pendientes: number; sinFecha: number; proxima: Dia | null; compensaciones: Compensacion[] }[];
}

const BASE = '/api/v1/soporte';
const json = async <T>(respuesta: Promise<Response>): Promise<T> => (await (await respuesta).json()) as T;
const conEquipo = (equipo: string | null | undefined) => (equipo ? `&equipo=${encodeURIComponent(equipo)}` : '');

export const CLAVE = ['soporte'] as const;

export const obtenerCalendario = (desde: Dia, hasta: Dia, equipo?: string | null) =>
  json<Calendario>(pedir(`${BASE}/turnos?desde=${desde}&hasta=${hasta}${conEquipo(equipo)}`));
export const obtenerEsteSabado = () => json<EsteSabado>(pedir(`${BASE}/este-sabado`));
export const obtenerCanal = () => json<CanalSoporte>(pedir(`${BASE}/canal`));
export const obtenerConfiguracion = () => json<Configuracion>(pedir(`${BASE}/configuracion`));
export const guardarConfiguracion = (cambio: CambioConfiguracion) =>
  json<Configuracion>(enviarJson(`${BASE}/configuracion`, 'PUT', cambio));
export const listarEquipos = () => json<Equipo[]>(pedir(`${BASE}/equipos`));
export const crearEquipo = (cambio: CambioEquipo) => json<Equipo>(enviarJson(`${BASE}/equipos`, 'POST', cambio));
export const cambiarEquipo = (id: string, cambio: CambioEquipo) =>
  json<Equipo>(enviarJson(`${BASE}/equipos/${encodeURIComponent(id)}`, 'PUT', cambio));
export const listarParticipantes = (equipo: string) =>
  json<Participante[]>(pedir(`${BASE}/equipos/${encodeURIComponent(equipo)}/participantes`));
export const agregarParticipante = (equipo: string, personaId: string) =>
  json<Participante>(enviarJson(`${BASE}/equipos/${encodeURIComponent(equipo)}/participantes`, 'POST', { personaId }));
export const cambiarParticipante = (id: string, activo: boolean) =>
  json<Participante>(enviarJson(`${BASE}/participantes/${encodeURIComponent(id)}`, 'PUT', { activo }));
export const ordenarParticipantes = (equipo: string, participantes: string[]) =>
  json<Participante[]>(enviarJson(`${BASE}/equipos/${encodeURIComponent(equipo)}/participantes/orden`, 'PUT', { participantes }));
export const buscarCandidatos = (q: string, equipo: string | null, signal?: AbortSignal) =>
  json<Candidato[]>(pedir(`${BASE}/candidatos?q=${encodeURIComponent(q)}${conEquipo(equipo)}`, { signal }));
export const crearTurno = (turno: {
  equipoId: string;
  fecha: Dia;
  personaId: string;
  horaInicio?: string | null;
  horaFin?: string | null;
  observacion?: string | null;
}) => json<Turno>(enviarJson(`${BASE}/turnos`, 'POST', turno));
export const cambiarTurno = (id: string, cambio: { horaInicio: string | null; horaFin: string | null; observacion: string | null }) =>
  json<Turno>(enviarJson(`${BASE}/turnos/${encodeURIComponent(id)}`, 'PUT', cambio));
export const cambiarEstadoTurno = (id: string, estado: 'confirmado' | 'aplicado' | 'cancelado', observacion?: string) =>
  json<Turno>(enviarJson(`${BASE}/turnos/${encodeURIComponent(id)}/estado`, 'POST', { estado, observacion: observacion ?? null }));
export const reemplazarTurno = (id: string, personaId: string, observacion?: string) =>
  json<Turno>(enviarJson(`${BASE}/turnos/${encodeURIComponent(id)}/reemplazo`, 'POST', { personaId, observacion: observacion ?? null }));
export const listarAusencias = () => json<Ausencia[]>(pedir(`${BASE}/ausencias`));
export const crearAusencia = (ausencia: { personaId: string; desde: Dia; hasta: Dia; tipo: TipoAusencia }) =>
  json<Ausencia>(enviarJson(`${BASE}/ausencias`, 'POST', ausencia));
export const eliminarAusencia = async (id: string) => {
  await enviarJson(`${BASE}/ausencias/${encodeURIComponent(id)}`, 'DELETE');
};
export const listarFestivos = (anio: number) => json<Festivo[]>(pedir(`${BASE}/festivos?anio=${anio}`));
export const crearFestivo = (fecha: Dia, nombre: string) => json<Festivo>(enviarJson(`${BASE}/festivos`, 'POST', { fecha, nombre }));
export const eliminarFestivo = async (id: string) => {
  await enviarJson(`${BASE}/festivos/${encodeURIComponent(id)}`, 'DELETE');
};
export const previsualizarGeneracion = (equipoId: string, meses: number) =>
  json<VistaPrevia>(enviarJson(`${BASE}/generacion/vista-previa`, 'POST', { equipoId, meses }));
export const confirmarGeneracion = (equipoId: string, meses: number, desde: Dia, huella: string) =>
  json<ResultadoGeneracion>(enviarJson(`${BASE}/generacion/confirmar`, 'POST', { equipoId, meses, desde, huella }));
export const obtenerIndicadores = (desde: Dia, hasta: Dia, equipo?: string | null) =>
  json<Indicadores>(pedir(`${BASE}/indicadores?desde=${desde}&hasta=${hasta}${conEquipo(equipo)}`));

export const previsualizarImportacion = (solicitud: SolicitudImportacion) =>
  json<VistaPreviaImportacion>(enviarJson(`${BASE}/importacion/vista-previa`, 'POST', solicitud));
export const confirmarImportacion = (solicitud: SolicitudImportacion & { huella: string }) =>
  json<{ importacionId: string; informe: VistaPreviaImportacion }>(enviarJson(`${BASE}/importacion/confirmar`, 'POST', solicitud));

export const RUTA_SOLICITUDES = `${BASE}/solicitudes-cambio`;
export const listarSolicitudes = (estado: 'abiertas' | 'cerradas' | 'todas' = 'abiertas') =>
  json<SolicitudCambio[]>(pedir(`${RUTA_SOLICITUDES}?estado=${estado}`));
export const obtenerReporteCompensaciones = () => json<ReporteCompensaciones>(pedir(`${BASE}/compensaciones/pendientes`));
export const listarCompensaciones = (estado: 'pendiente' | 'tomada' | 'todas' = 'todas') =>
  json<Compensacion[]>(pedir(`${BASE}/compensaciones?estado=${estado}`));
export const registrarCompensacion = (turnoId: string, fechaCompensada: Dia | null, nota: string | null) =>
  json<Compensacion>(enviarJson(`${BASE}/compensaciones`, 'POST', { turnoId, fechaCompensada, nota }));
export const cambiarCompensacion = (id: string, cambio: { fechaCompensada: Dia | null; estado: EstadoCompensacion; nota: string | null }) =>
  json<Compensacion>(enviarJson(`${BASE}/compensaciones/${encodeURIComponent(id)}`, 'PUT', cambio));

// --- Microsoft 365 (HU 2.10, ATLAS-ADR-0033) ---

export interface EstadoM365 {
  calendario: {
    activo: boolean;
    /** Por qué está apagado (local, dev o sin permisos); vacío si está activo. */
    motivo: string | null;
    nombreCalendario: string;
    pendientes: number;
    conErrores: number;
    ultimaSincronizacion: string | null;
  };
  teams: { activo: boolean; motivo: string | null; pendientes: number };
}

export type TipoFeed = 'personal' | 'equipo';

export interface FeedIcs {
  id: string;
  tipo: TipoFeed;
  equipo: EquipoResumen | null;
  creadoEn: string;
  ultimoUsoEn: string | null;
}

/** La URL lleva el token: solo se ve al crearlo o regenerarlo. */
export interface FeedIcsCreado {
  id: string;
  tipo: TipoFeed;
  equipo: EquipoResumen | null;
  url: string;
  urlWebcal: string;
  creadoEn: string;
}

export const obtenerEstadoM365 = () => json<EstadoM365>(pedir(`${BASE}/microsoft-365`));
export const listarFeeds = () => json<FeedIcs[]>(pedir(`${BASE}/feeds`));
export const crearFeed = (tipo: TipoFeed, equipoId: string | null) =>
  json<FeedIcsCreado>(enviarJson(`${BASE}/feeds`, 'POST', { tipo, equipoId }));
export const revocarFeed = async (id: string) => {
  await enviarJson(`${BASE}/feeds/${encodeURIComponent(id)}`, 'DELETE');
};

export function useCalendario(desde: Dia, hasta: Dia, equipo: string | null) {
  return useQuery({
    queryKey: [...CLAVE, 'turnos', desde, hasta, equipo ?? 'todos'],
    queryFn: () => obtenerCalendario(desde, hasta, equipo),
    staleTime: 30_000,
  });
}

export function useEquipos() {
  return useQuery({ queryKey: [...CLAVE, 'equipos'], queryFn: listarEquipos, staleTime: 60_000 });
}

export function useParticipantes(equipo: string | null) {
  return useQuery({
    queryKey: [...CLAVE, 'participantes', equipo],
    queryFn: () => listarParticipantes(equipo!),
    enabled: equipo !== null,
    staleTime: 30_000,
  });
}

export const TEXTO_ESTADO: Record<EstadoTurno, string> = {
  programado: 'Programado',
  confirmado: 'Confirmado',
  aplicado: 'Aplicado',
  reemplazado: 'Reemplazado',
  cancelado: 'Cancelado',
};

export const TEXTO_ORIGEN: Record<OrigenTurno, string> = { importado: 'Importado', generado: 'Generado', manual: 'Manual' };

export const TEXTO_AUSENCIA: Record<TipoAusencia, string> = { vacaciones: 'Vacaciones', incapacidad: 'Incapacidad', otra: 'Otra' };

export const horario = (t: { horaInicio: string | null; horaFin: string | null }): string | null =>
  t.horaInicio && t.horaFin ? `${t.horaInicio} a ${t.horaFin}` : null;

/** «1 persona por sábado» o «1 a 2 personas por sábado». */
export const textoCupo = (e: Pick<Equipo, 'personasMinimo' | 'personasMaximo'>): string =>
  e.personasMinimo === e.personasMaximo
    ? `${e.personasMaximo} ${e.personasMaximo === 1 ? 'persona' : 'personas'} por sábado`
    : `${e.personasMinimo} a ${e.personasMaximo} personas por sábado`;

export const TEXTO_SOLICITUD: Record<EstadoSolicitud, string> = {
  pendiente_reemplazo: 'Espera al reemplazo',
  pendiente_aprobacion: 'Espera aprobación',
  aprobada: 'Aprobada',
  rechazada: 'Rechazada',
  cancelada: 'Cancelada',
};

export const TEXTO_PASO: Record<string, string> = {
  solicitada: 'Solicitó el cambio',
  aceptada: 'Aceptó el reemplazo',
  rechazada_por_reemplazo: 'No aceptó el reemplazo',
  aprobada: 'Aprobó el cambio',
  rechazada: 'Rechazó el cambio',
  cancelada: 'Canceló la solicitud',
};
