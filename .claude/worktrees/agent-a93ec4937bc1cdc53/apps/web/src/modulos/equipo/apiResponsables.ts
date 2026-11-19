import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../app/api';
import { conRespaldo, conRespaldoDerivado } from '../../pwa/sinConexion';
import { RECURSO_REPARTO } from './recursos';

/**
 * Cliente de los responsables (`/api/v1/equipo`, etiqueta «Equipo», HU 2.3, 2.4 y 2.5, ATLAS-ADR-0031): reparto por
 * aplicativo y por persona, mapa de carga, historial, traspasos, revisión trimestral, CODEOWNERS y «¿A quién llamo?».
 * El teléfono solo llega con `atlas.equipo.ver_contacto`; cambiar el reparto exige `atlas.equipo.gestionar`.
 */
export type TipoResponsabilidad = 'principal' | 'secundario';
export type EstadoTraspaso = 'en_acompanamiento' | 'habilitado';
export type TipoAplicativo = 'producto' | 'modulo' | 'servicio';

export interface AplicativoRef {
  id: string;
  slug: string;
  nombre: string;
  tipo: TipoAplicativo;
}

export interface PersonaEquipo {
  id: string;
  nombre: string;
  correo: string | null;
  cargo: string | null;
  foto: string | null;
  /** Solo con `atlas.equipo.ver_contacto`. */
  telefono: string | null;
  usuarioGitHub: string | null;
  activa: boolean;
}

export interface Responsable {
  id: string;
  tipo: TipoResponsabilidad;
  persona: PersonaEquipo;
  dedicacion: number | null;
  vigenteDesde: string;
  vigenteHasta: string | null;
  vigente: boolean;
  estadoTraspaso: EstadoTraspaso;
}

export interface FilaReparto {
  aplicativo: AplicativoRef;
  principal: Responsable | null;
  secundario: Responsable | null;
}

export interface PersonaReparto {
  persona: PersonaEquipo;
  principalDe: AplicativoRef[];
  secundarioDe: AplicativoRef[];
}

export interface DefinicionRol {
  rol: TipoResponsabilidad;
  titulo: string;
  puntos: string[];
}

export interface VersionResumen {
  numero: number;
  origen: 'importacion' | 'cambio' | 'restauracion';
  restauradaDe: number | null;
  fechaDocumento: string | null;
  definidoPor: string | null;
  autor: string | null;
  nota: string;
  creadaEn: string;
  huella: string;
  responsabilidades: number;
}

export interface Reparto {
  version: VersionResumen | null;
  definiciones: DefinicionRol[];
  porPersona: PersonaReparto[];
  productos: FilaReparto[];
  modulos: FilaReparto[];
  servicios: FilaReparto[];
  huerfanas: number;
  veContacto: boolean;
  puedeGestionar: boolean;
}

export interface CargaPersona {
  persona: PersonaEquipo;
  principales: number;
  secundarias: number;
  puntaje: number;
  nivel: 'alta' | 'normal' | 'baja';
}

export interface MapaCarga {
  media: number;
  umbralAlto: number;
  umbralBajo: number;
  personas: CargaPersona[];
}

export interface ResponsabilidadDePersona {
  aplicativo: AplicativoRef;
  responsable: Responsable;
}

export interface ResponsabilidadesPersona {
  persona: PersonaEquipo;
  principalDe: ResponsabilidadDePersona[];
  secundarioDe: ResponsabilidadDePersona[];
}

export interface ActividadTraspaso {
  id: string;
  tipo: 'despliegue' | 'guardia';
  fecha: string;
  nota: string | null;
  registradaEn: string;
}

export interface Traspaso {
  responsabilidadId: string;
  aplicativo: AplicativoRef;
  secundario: PersonaEquipo;
  principal: PersonaEquipo | null;
  estado: EstadoTraspaso;
  despliegues: number;
  guardias: number;
  completo: boolean;
  actividades: ActividadTraspaso[];
}

export interface ResponsablesAplicativo {
  aplicativo: AplicativoRef;
  principal: Responsable | null;
  secundario: Responsable | null;
  heredadoDe: AplicativoRef | null;
  definiciones: DefinicionRol[];
  traspaso: Traspaso | null;
  version: number | null;
  puedeGestionar: boolean;
  veContacto: boolean;
}

export interface EquipoTurno {
  id: string;
  nombre: string;
  area: string | null;
  horaInicio: string | null;
  horaFin: string | null;
  personas: PersonaEquipo[];
}

export interface AQuienLlamo {
  aplicativo: AplicativoRef;
  hoy: string;
  esSabado: boolean;
  soporte: { fecha: string; festivo: string | null; equipos: EquipoTurno[] } | null;
  principal: Responsable | null;
  secundario: Responsable | null;
  heredadoDe: AplicativoRef | null;
  veContacto: boolean;
}

export interface CambioVersion {
  aplicativo: AplicativoRef;
  tipo: TipoResponsabilidad;
  cambio: 'agregada' | 'retirada' | 'persona' | 'datos';
  antes: string | null;
  despues: string | null;
  campos: string[];
}

export interface Comparacion {
  desde: number;
  hasta: number;
  iguales: boolean;
  cambios: CambioVersion[];
}

export interface FilaVistaPrevia {
  aplicativo: AplicativoRef;
  tipo: TipoResponsabilidad;
  personaId: string;
  persona: string;
  dedicacion: number | null;
}

export interface Pendiente {
  tipo: 'aplicativo' | 'persona';
  nombre: string;
  motivo: 'no_encontrado' | 'ambiguo';
  detalle: string;
}

export interface VistaPrevia {
  version: number | null;
  fecha: string | null;
  definidoPor: string | null;
  descripcionVersion: string | null;
  dedicacionSecundario: number | null;
  proximaRevision: string | null;
  definiciones: DefinicionRol[];
  filas: FilaVistaPrevia[];
  pendientes: Pendiente[];
  inconsistencias: string[];
  avisos: string[];
  errores: string[];
  versionActual: number | null;
  puedeConfirmar: boolean;
  huella: string;
}

export interface Revision {
  id: string;
  fecha: string;
  estado: 'pendiente' | 'hecha';
  recordatorioEnviadoEn: string | null;
  hechaEn: string | null;
  hechaPor: string | null;
  nota: string | null;
}

export interface Revisiones {
  proxima: Revision | null;
  historial: Revision[];
  diasAnticipacion: number;
}

export interface LineaCodeowners {
  patron: string;
  aplicativo: AplicativoRef;
  propietarios: string[];
  sinUsuario: string[];
  heredadoDe: AplicativoRef | null;
}

export interface BloqueCodeowners {
  repo: string;
  texto: string;
  lineas: LineaCodeowners[];
}

export interface Codeowners {
  version: number | null;
  gitHubConfigurado: boolean;
  modoGitHub: 'token' | 'app' | 'ninguno';
  repos: BloqueCodeowners[];
  personasSinUsuario: PersonaEquipo[];
}

export interface DiferenciaCodeowners {
  patron: string;
  estado: 'igual' | 'distinto' | 'falta_en_github' | 'solo_en_github';
  enAtlas: string[];
  enGitHub: string[];
}

export interface DiferenciasCodeowners {
  repo: string;
  configurado: boolean;
  mensaje: string | null;
  archivo: string | null;
  diferencias: DiferenciaCodeowners[];
}

const EQUIPO = '/api/v1/equipo';

/** Prefijo de las consultas del reparto: cualquier cambio invalida todo lo que cuelga de él. */
export const CLAVE_REPARTO = ['equipo', 'reparto'] as const;

async function leer<T>(ruta: string): Promise<T> {
  return (await (await pedir(ruta)).json()) as T;
}

async function enviar<T>(ruta: string, metodo: 'POST' | 'PUT' | 'DELETE', cuerpo?: unknown): Promise<T> {
  return (await (await enviarJson(ruta, metodo, cuerpo)).json()) as T;
}

const deSlug = (slug: string) => `${EQUIPO}/aplicativos/${encodeURIComponent(slug)}`;

export const obtenerReparto = () => conRespaldo(RECURSO_REPARTO, () => leer<Reparto>(`${EQUIPO}/responsabilidades`));
export const obtenerCarga = () => leer<MapaCarga>(`${EQUIPO}/carga`);
export const obtenerMias = () => leer<ResponsabilidadesPersona>(`${EQUIPO}/yo/responsabilidades`);
export const obtenerVersiones = () => leer<VersionResumen[]>(`${EQUIPO}/versiones`);
export const obtenerTraspasos = () => leer<Traspaso[]>(`${EQUIPO}/traspasos`);
export const obtenerRevisiones = () => leer<Revisiones>(`${EQUIPO}/revisiones`);
export const obtenerCodeowners = () => leer<Codeowners>(`${EQUIPO}/codeowners`);
/** La fila del reparto guardado de un aplicativo (sin conexión); `null` si no tiene responsables propios en él. */
export function filaDe(reparto: Reparto, slug: string): FilaReparto | null {
  return [...reparto.productos, ...reparto.modulos, ...reparto.servicios].find((f) => f.aplicativo.slug === slug) ?? null;
}

/** Sin conexión (HU 2.12) salen del reparto guardado: sin herencia del padre, sin traspaso y sin contacto. */
export const obtenerResponsablesDe = (slug: string) =>
  conRespaldoDerivado<ResponsablesAplicativo, Reparto>(RECURSO_REPARTO, () => leer<ResponsablesAplicativo>(`${deSlug(slug)}/responsables`), (reparto) => {
    const fila = filaDe(reparto, slug);
    return fila
      ? {
          aplicativo: fila.aplicativo,
          principal: fila.principal,
          secundario: fila.secundario,
          heredadoDe: null,
          definiciones: reparto.definiciones,
          traspaso: null,
          version: reparto.version?.numero ?? null,
          puedeGestionar: false,
          veContacto: false,
        }
      : null;
  });

/** Sin conexión: el principal y el secundario del reparto guardado; el soporte de turno se ve en el calendario. */
export const obtenerAQuienLlamo = (slug: string) =>
  conRespaldoDerivado<AQuienLlamo, Reparto>(RECURSO_REPARTO, () => leer<AQuienLlamo>(`${deSlug(slug)}/a-quien-llamo`), (reparto) => {
    const fila = filaDe(reparto, slug);
    const hoy = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Bogota' }).format(new Date());
    return fila
      ? {
          aplicativo: fila.aplicativo,
          hoy,
          esSabado: new Date(`${hoy}T12:00:00Z`).getUTCDay() === 6,
          soporte: null,
          principal: fila.principal,
          secundario: fila.secundario,
          heredadoDe: null,
          veContacto: false,
        }
      : null;
  });
export const obtenerFicha = (slug: string) => leer<{ aplicativo: AplicativoRef; markdown: string }>(`${deSlug(slug)}/ficha-responsables`);
export const compararVersiones = (desde: number, hasta: number) =>
  leer<Comparacion>(`${EQUIPO}/versiones/comparacion?desde=${desde}&hasta=${hasta}`);
export const obtenerDiferencias = (repo: string) => leer<DiferenciasCodeowners>(`${EQUIPO}/codeowners/diferencias?repo=${encodeURIComponent(repo)}`);

export interface CambioResponsable {
  aplicativoId: string;
  tipo: TipoResponsabilidad;
  personaId: string;
  dedicacion: number | null;
  nota: string;
}

export const cambiarResponsable = (cambio: CambioResponsable) => enviar<VersionResumen>(`${EQUIPO}/responsabilidades`, 'PUT', cambio);
export const retirarResponsable = ({ id, nota }: { id: string; nota: string }) =>
  enviar<VersionResumen>(`${EQUIPO}/responsabilidades/${id}?nota=${encodeURIComponent(nota)}`, 'DELETE');
export const restaurarVersion = ({ numero, nota }: { numero: number; nota: string }) =>
  enviar<VersionResumen>(`${EQUIPO}/versiones/${numero}/restauracion`, 'POST', { nota });
export const previsualizarImportacion = (markdown: string) => enviar<VistaPrevia>(`${EQUIPO}/importacion/vista-previa`, 'POST', { markdown });
export const importarDocumento = ({ markdown, huella, traspaso }: { markdown: string; huella: string; traspaso: EstadoTraspaso }) =>
  enviar<VersionResumen>(`${EQUIPO}/importacion`, 'POST', { markdown, huella, traspaso });
export const registrarActividad = ({ id, tipo }: { id: string; tipo: ActividadTraspaso['tipo'] }) =>
  enviar<Traspaso>(`${EQUIPO}/responsabilidades/${id}/traspaso/actividades`, 'POST', { tipo });
export const habilitarSecundario = (id: string) => enviar<Traspaso>(`${EQUIPO}/responsabilidades/${id}/traspaso/habilitacion`, 'POST');
export const programarRevision = (fecha: string) => enviar<Revision>(`${EQUIPO}/revisiones/proxima`, 'PUT', { fecha });
export const cerrarRevision = ({ id, nota }: { id: string; nota: string | null }) => enviar<Revisiones>(`${EQUIPO}/revisiones/${id}/cierre`, 'POST', { nota });
export const guardarUsuarioGitHub = ({ personaId, usuario }: { personaId: string; usuario: string | null }) =>
  enviar<{ personaId: string; usuario: string | null }>(`${EQUIPO}/personas/${personaId}/github`, 'PUT', { usuario });

/** Una consulta del reparto; todas comparten el prefijo para invalidarse juntas. */
export function useReparto<T>(clave: readonly string[], consulta: () => Promise<T>, habilitada = true) {
  return useQuery({ queryKey: [...CLAVE_REPARTO, ...clave], queryFn: consulta, staleTime: 60_000, enabled: habilitada });
}

/** Una acción que cambia el reparto: al terminar bien, invalida todas sus consultas. */
export function useCambioReparto<A, R>(accion: (argumentos: A) => Promise<R>) {
  const cliente = useQueryClient();
  return useMutation({ mutationFn: accion, onSuccess: () => cliente.invalidateQueries({ queryKey: CLAVE_REPARTO }) });
}

export const TEXTO_TIPO: Record<TipoResponsabilidad, string> = { principal: 'Principal', secundario: 'Secundario' };

export const TEXTO_TRASPASO: Record<EstadoTraspaso, string> = { en_acompanamiento: 'En acompañamiento', habilitado: 'Habilitado' };

/** «2026-10-20» → «20/10/2026» (sin pasar por la zona horaria). */
export function fechaCorta(iso: string): string {
  const [a, m, d] = iso.slice(0, 10).split('-');
  return `${d}/${m}/${a}`;
}
