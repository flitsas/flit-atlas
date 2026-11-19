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

/** Las versiones de un contrato; se piden solo cuando la persona abre el contrato. */
export function useVersiones(contratoId: string, habilitado: boolean) {
  return useQuery({
    queryKey: ['contratos', 'versiones', contratoId],
    queryFn: () => listarVersiones(contratoId),
    enabled: habilitado,
    staleTime: 60_000,
  });
}
