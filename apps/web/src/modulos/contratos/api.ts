import { useQuery } from '@tanstack/react-query';

import { pedir } from '../../app/api';

/** Contrato de `/api/v1/contratos` (contracts/openapi/atlas.v1.yaml, etiqueta «Contratos», HU 3.2). */
export type TipoContrato = 'openapi' | 'asyncapi' | 'proto';

export interface VersionResumen {
  id: string;
  version: string;
  esEstable: boolean;
  sha256: string;
  formato: 'json' | 'yaml' | 'proto';
  tamano: number;
  repositorio: string;
  rama: string;
  commit: string;
  workflow: string | null;
  workflowDelJob: string;
  ejecucion: string | null;
  publicadoPor: string;
  publicadoEn: string;
  tieneBreaking: boolean;
  estado: 'vigente' | 'deprecada' | 'retirada';
  retiroEn: string | null;
}

export interface ContratoResumen {
  id: string;
  codigo: string;
  nombre: string;
  descripcion: string | null;
  tipo: TipoContrato;
  visibilidad: 'interna' | 'compartible';
  aplicativo: { id: string; slug: string | null; nombre: string | null };
  repositorio: string;
  versiones: number;
  ultimaVersion: VersionResumen | null;
  ultimaEstable: VersionResumen | null;
}

export interface ListadoContratos {
  total: number;
  elementos: ContratoResumen[];
}

export interface FiltroContratos {
  texto?: string;
  tipo?: TipoContrato;
  aplicativo?: string;
}

export const TIPOS: Record<TipoContrato, string> = { openapi: 'OpenAPI', asyncapi: 'AsyncAPI', proto: 'proto' };

export async function listarContratos(filtro: FiltroContratos = {}): Promise<ListadoContratos> {
  const parametros = new URLSearchParams();
  for (const [clave, valor] of Object.entries(filtro)) if (valor) parametros.set(clave, valor);
  const consulta = parametros.toString();
  return (await (await pedir(`/api/v1/contratos${consulta ? `?${consulta}` : ''}`)).json()) as ListadoContratos;
}

export async function listarVersiones(contratoId: string): Promise<VersionResumen[]> {
  return (await (await pedir(`/api/v1/contratos/${encodeURIComponent(contratoId)}/versiones`)).json()) as VersionResumen[];
}

export function useContratos(filtro: FiltroContratos = {}) {
  return useQuery({ queryKey: ['contratos', 'listado', filtro], queryFn: () => listarContratos(filtro), staleTime: 60_000 });
}

// --- Pantalla del contrato (HU 3.6) ---

/** Operación, webhook, canal o rpc del índice de una versión (`/versiones/{v}/operaciones`). */
export interface OperacionIndice {
  clave: string;
  clase: string;
  metodo: string | null;
  ruta: string | null;
  operationId: string | null;
  resumen: string | null;
  etiquetas: string[];
  publica: boolean;
}

export interface ResumenDiff {
  total: number;
  errores: number;
  advertencias: number;
  informativos: number;
  breaking: number;
  hayBreaking: boolean;
}

export interface CambioDiff {
  regla: string;
  nivel: string;
  breaking: boolean;
  texto: string;
  comentario: string | null;
  metodo: string | null;
  ruta: string | null;
  operationId: string | null;
  seccion: string | null;
}

/** `GET /contratos/{id}/diff`: los cambios que rompen compatibilidad vienen primero (ATLAS-ADR-0036). */
export interface DiffContrato {
  contratoId: string;
  desde: string;
  hasta: string;
  motor: string;
  sinDiferencias: boolean;
  resumen: ResumenDiff;
  cambios: CambioDiff[];
  changelog: string;
  desdeCache: boolean;
}

const rutaContrato = (id: string) => `/api/v1/contratos/${encodeURIComponent(id)}`;

export function useContrato(id: string) {
  return useQuery({
    queryKey: ['contratos', 'contrato', id],
    queryFn: async () => (await (await pedir(rutaContrato(id))).json()) as ContratoResumen,
    staleTime: 60_000,
  });
}

/** El spec tal como se publicó (texto); es inmutable por versión, así que no se vuelve a pedir. */
export function useSpec(id: string, version: string | undefined) {
  return useQuery({
    queryKey: ['contratos', 'spec', id, version],
    queryFn: async () => (await pedir(`${rutaContrato(id)}/versiones/${encodeURIComponent(version!)}/spec`)).text(),
    enabled: !!version,
    staleTime: Infinity,
    gcTime: 10 * 60_000,
  });
}

export function useOperaciones(id: string, version: string | undefined) {
  return useQuery({
    queryKey: ['contratos', 'operaciones', id, version],
    queryFn: async () =>
      (await (await pedir(`${rutaContrato(id)}/versiones/${encodeURIComponent(version!)}/operaciones`)).json()) as OperacionIndice[],
    enabled: !!version,
    staleTime: Infinity,
  });
}

/** Diff entre dos versiones; el mismo par siempre da lo mismo (versiones inmutables). */
export function useDiff(id: string, desde: string | undefined, hasta: string | undefined) {
  return useQuery({
    queryKey: ['contratos', 'diff', id, desde, hasta],
    queryFn: async () =>
      (await (await pedir(`${rutaContrato(id)}/diff?desde=${encodeURIComponent(desde!)}&hasta=${encodeURIComponent(hasta!)}`)).json()) as DiffContrato,
    enabled: !!desde && !!hasta && desde !== hasta,
    staleTime: Infinity,
    retry: false,
  });
}

/** Las versiones de un contrato; se piden solo cuando la persona abre el contrato. */
export function useVersiones(contratoId: string, habilitado: boolean) {
  return useQuery({
    queryKey: ['contratos', 'versiones', contratoId],
    queryFn: () => listarVersiones(contratoId),
    enabled: habilitado,
    staleTime: 60_000,
  });
}
