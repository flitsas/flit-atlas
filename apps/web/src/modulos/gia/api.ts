import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, ErrorApi, pedir } from '../../app/api';

/** Gobierno de IA (ATLAS-ADR-0055): `/api/v1/gia` (contracts/openapi/atlas.v1.yaml). */
const RAIZ = '/api/v1/gia';
const CLAVE = ['gia'] as const;

export interface PersonaGia {
  id: string;
  nombre: string;
}

export interface CasoResumen {
  id: string;
  codigo: string;
  nombre: string;
  aplicacion: string;
  estado: string;
  nivelRiesgo: string | null;
  riesgoPreliminar: string | null;
  dueno: PersonaGia | null;
  proximaRevision: string | null;
  revisionVencida: boolean;
}

export interface FichaCaso {
  nombre: string;
  aplicacion: string;
  aplicativoId: string | null;
  repo: string | null;
  duenoPersonaId: string | null;
  ambientes: string[];
  proposito: string;
  tipo: string | null;
  usuariosAfectados: string[];
  decisionSobrePersonas: boolean | null;
  decisionDescripcion: string | null;
  humanoEnBucle: string | null;
  categoriasDato: string[];
  tiposPii: string[];
  volumenMensualEstimado: number | null;
  baseLegal: string | null;
  baseLegalRef: string | null;
  minimizacion: string | null;
  presidioActivo: boolean | null;
  presidioEntidades: string[];
  aliasUsados: string[];
  proveedorActual: string | null;
  transferenciaInternacional: string | null;
  metricasCalidad: string | null;
  evalsRuta: string | null;
  promptsUbicacion: string | null;
  controles: string | null;
  avisoUsuario: string | null;
  planRollback: string | null;
  enlaces: string[];
}

export interface CasoDetalle {
  caso: CasoResumen;
  ficha: FichaCaso;
  detallePlan: string | null;
  versionActual: number;
  aprobadoPor: PersonaGia | null;
  aprobadoEn: string | null;
  faltantes: string[];
  puedeGestionar: boolean;
}

export interface EscenarioRiesgo {
  dimension: string;
  escenario: string;
  p: number;
  i: number;
  controlesExistentes: string | null;
  controlesNuevos: string | null;
  p2: number | null;
  i2: number | null;
}

export interface VotoResumen {
  persona: PersonaGia;
  rol: string;
  voto: string;
  condiciones: string | null;
  comentario: string | null;
  votadoEn: string;
}

export interface Evaluacion {
  id: string;
  version: number;
  dimensiones: EscenarioRiesgo[];
  puntajeInherente: number;
  nivelInherente: string;
  puntajeResidual: number;
  nivelResidual: string;
  decision: string;
  condiciones: string | null;
  evaluador: PersonaGia;
  evaluadaEn: string;
  decididaEn: string | null;
  rolesRequeridos: string[];
  requisitos: string[];
  votos: VotoResumen[];
}

export interface Eipd {
  id: string;
  estado: string;
  descripcion: string;
  necesidad: string;
  riesgos: string;
  medidas: string;
  opinionOpd: string;
  planAccion: string;
  documentoUrl: string | null;
  decididaPor: PersonaGia | null;
  decididaEn: string | null;
}

export interface PoliticaResumen {
  id: string;
  codigo: string;
  version: string;
  titulo: string;
  estado: string;
  requiereAceptacion: boolean;
  vigenteDesde: string | null;
  pendienteDeRedaccion: boolean;
  aceptadaPorMi: boolean;
}

export interface PoliticaDetalle {
  politica: PoliticaResumen;
  contenido: string;
  fuenteGit: string | null;
  aprobadaPor: PersonaGia | null;
}

export interface Herramienta {
  id: string;
  codigo: string;
  nombre: string;
  estado: string;
  plan: string | null;
  proveedor: string | null;
  nivelDatoMaximo: string;
  asientosActivos: number;
}

export interface Incidente {
  id: string;
  codigo: string;
  tipo: string;
  severidad: string;
  descripcion: string;
  detectadoEn: string;
  cerradoEn: string | null;
  responsable: PersonaGia;
}

export interface CatalogoIncidentes {
  tipos: { codigo: string; tipo: string; ejemplo: string; playbook: string[] }[];
  severidades: { codigo: string; nombre: string; criterio: string; tiempoRespuesta: string; notificaA: string }[];
}

export interface RolGobierno {
  id: string;
  persona: PersonaGia;
  rol: string;
  asignadoEn: string;
}

export interface Kpi {
  codigo: string;
  nombre: string;
  disponible: boolean;
  valor: number | null;
  total: number | null;
  meta: string;
  nota: string | null;
}

export interface Tablero {
  casosPorEstado: { clave: string; total: number }[];
  casosPorRiesgo: { clave: string; total: number }[];
  revisionesVencidas: number;
  kpis: Kpi[];
}

export const TEXTO_ESTADO: Record<string, string> = {
  borrador: 'Borrador',
  en_revision: 'En revisión',
  aprobado: 'Aprobado',
  en_produccion: 'En producción',
  suspendido: 'Suspendido',
  retirado: 'Retirado',
  vigente: 'Vigente',
  reemplazada: 'Reemplazada',
  aprobada: 'Aprobada',
  rechazada: 'Rechazada',
};

export const TEXTO_RIESGO: Record<string, string> = {
  bajo: 'Bajo',
  medio: 'Medio',
  medio_alto: 'Medio-alto',
  alto: 'Alto',
  critico: 'Crítico',
  sin_evaluar: 'Sin evaluar',
};

export const TEXTO_ROL: Record<string, string> = {
  responsable_ia: 'Responsable de IA',
  opd: 'Oficial de Protección de Datos',
  comite: 'Comité de IA',
  seguridad: 'Seguridad',
};

export const TEXTO_DECISION: Record<string, string> = {
  pendiente: 'Pendiente',
  aprobado: 'Aprobado',
  aprobado_condiciones: 'Aprobado con condiciones',
  rechazado: 'Rechazado',
};

export const DIMENSIONES: { id: string; nombre: string; pregunta: string }[] = [
  { id: 'D1', nombre: 'Privacidad', pregunta: '¿Qué pasa si los datos enviados se exponen o se usan para otra finalidad?' },
  { id: 'D2', nombre: 'Exactitud', pregunta: '¿Qué pasa si el modelo se equivoca o inventa?' },
  { id: 'D3', nombre: 'Seguridad', pregunta: '¿Inyección de prompt, fuga del system prompt, salida insegura, agencia excesiva?' },
  { id: 'D4', nombre: 'Efecto sobre personas', pregunta: '¿La salida afecta derechos, dinero o trámites de un ciudadano?' },
  { id: 'D5', nombre: 'Continuidad', pregunta: '¿Qué pasa si el proveedor cae o cambia el modelo?' },
  { id: 'D6', nombre: 'Costo', pregunta: '¿Puede dispararse el gasto?' },
];

async function leer<T>(ruta: string): Promise<T> {
  return (await (await pedir(`${RAIZ}${ruta}`)).json()) as T;
}

/** El detalle de un 422: los campos o requisitos que faltan. */
export function detalleError(error: unknown): string[] {
  if (!(error instanceof ErrorApi)) return [];
  const detalle = error.cuerpo?.detalle;
  return Array.isArray(detalle) ? (detalle as string[]) : [];
}

export const useTablero = () => useQuery({ queryKey: [...CLAVE, 'tablero'], queryFn: () => leer<Tablero>('/tablero') });

export const useCasos = () => useQuery({ queryKey: [...CLAVE, 'casos'], queryFn: () => leer<{ elementos: CasoResumen[] }>('/casos') });

export const useCaso = (id: string) => useQuery({ queryKey: [...CLAVE, 'casos', id], queryFn: () => leer<CasoDetalle>(`/casos/${encodeURIComponent(id)}`) });

export const useEvaluaciones = (id: string) =>
  useQuery({ queryKey: [...CLAVE, 'casos', id, 'evaluaciones'], queryFn: () => leer<Evaluacion[]>(`/casos/${encodeURIComponent(id)}/evaluaciones`) });

export const useEipd = (id: string) =>
  useQuery({
    queryKey: [...CLAVE, 'casos', id, 'eipd'],
    queryFn: async () => {
      try {
        return await leer<Eipd>(`/casos/${encodeURIComponent(id)}/eipd`);
      } catch (error) {
        if (error instanceof ErrorApi && error.estado === 404) return null;
        throw error;
      }
    },
  });

export const usePoliticas = () => useQuery({ queryKey: [...CLAVE, 'politicas'], queryFn: () => leer<PoliticaResumen[]>('/politicas') });

export const usePolitica = (id: string) => useQuery({ queryKey: [...CLAVE, 'politicas', id], queryFn: () => leer<PoliticaDetalle>(`/politicas/${encodeURIComponent(id)}`) });

export const useHerramientas = () => useQuery({ queryKey: [...CLAVE, 'herramientas'], queryFn: () => leer<Herramienta[]>('/herramientas') });

export const useIncidentes = () => useQuery({ queryKey: [...CLAVE, 'incidentes'], queryFn: () => leer<Incidente[]>('/incidentes') });

export const useCatalogoIncidentes = () => useQuery({ queryKey: [...CLAVE, 'catalogo'], queryFn: () => leer<CatalogoIncidentes>('/incidentes/catalogo'), staleTime: Infinity });

export const useRoles = () => useQuery({ queryKey: [...CLAVE, 'roles'], queryFn: () => leer<RolGobierno[]>('/roles-gobierno') });

export const usePersonas = (texto: string, activa: boolean) =>
  useQuery({
    queryKey: [...CLAVE, 'personas', texto],
    queryFn: () => leer<PersonaGia[]>(`/personas?texto=${encodeURIComponent(texto)}`),
    enabled: activa,
  });

/** Cualquier escritura del módulo: al terminar se refresca todo lo del módulo, porque un voto cambia caso, evaluación y tablero. */
export function useEnvio<T = unknown>() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async ({ ruta, metodo = 'POST', cuerpo }: { ruta: string; metodo?: 'POST' | 'PUT' | 'DELETE'; cuerpo?: unknown }) => {
      const respuesta = await enviarJson(`${RAIZ}${ruta}`, metodo, cuerpo);
      return respuesta.status === 204 ? (undefined as T) : ((await respuesta.json()) as T);
    },
    onSuccess: () => cliente.invalidateQueries({ queryKey: CLAVE }),
  });
}
