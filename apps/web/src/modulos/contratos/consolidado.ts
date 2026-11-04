import { useQuery } from '@tanstack/react-query';

import { pedir } from '../../app/api';
import type { TipoContrato } from './api';

/**
 * Contrato de `GET /api/v1/contratos/consolidado` (contracts/openapi/atlas.v1.yaml, etiqueta «Contratos», HU 3.4): por
 * contrato, la versión de cada ambiente según los canales que marca el CD, los desfases y los
 * cambios que rompen compatibilidad entre la versión de pdn y la de qa.
 */
export interface CanalConsolidado {
  ambiente: string;
  version: string | null;
  esEstable: boolean | null;
  desplegadoEn: string | null;
}

export interface Desfase {
  ambiente: string;
  respectoDe: string;
  tipo: 'adelante' | 'atrasado' | 'ausente';
  mensaje: string;
}

export interface CambioPendiente {
  regla: string;
  texto: string;
  metodo: string | null;
  ruta: string | null;
}

export interface BreakingPendientes {
  /** Versión en pdn. */
  desde: string;
  /** Versión en qa. */
  hasta: string;
  estado: 'sin_breaking' | 'con_breaking' | 'no_disponible' | 'no_soportado';
  fuente: 'changelog' | 'render' | null;
  cambios: CambioPendiente[];
}

export interface ContratoConsolidado {
  id: string;
  codigo: string;
  nombre: string;
  tipo: TipoContrato;
  aplicativo: { id: string; slug: string | null; nombre: string | null };
  canales: CanalConsolidado[];
  desfases: Desfase[];
  breakingPendientes: BreakingPendientes | null;
}

export interface ConsolidadoContratos {
  ambientes: string[];
  total: number;
  conDesfases: number;
  conBreaking: number;
  elementos: ContratoConsolidado[];
}

export interface FiltroConsolidado {
  texto?: string;
  tipo?: TipoContrato;
  aplicativo?: string;
  /** Solo los contratos con algún desfase. */
  desfases?: boolean;
}

export async function obtenerConsolidado(filtro: FiltroConsolidado = {}): Promise<ConsolidadoContratos> {
  const parametros = new URLSearchParams();
  if (filtro.aplicativo) parametros.set('aplicativo', filtro.aplicativo);
  if (filtro.tipo) parametros.set('tipo', filtro.tipo);
  if (filtro.texto) parametros.set('texto', filtro.texto);
  if (filtro.desfases) parametros.set('desfases', 'true');
  const consulta = parametros.toString();
  return (await (await pedir(`/api/v1/contratos/consolidado${consulta ? `?${consulta}` : ''}`)).json()) as ConsolidadoContratos;
}

export function useConsolidado(filtro: FiltroConsolidado = {}) {
  return useQuery({ queryKey: ['contratos', 'consolidado', filtro], queryFn: () => obtenerConsolidado(filtro), staleTime: 60_000 });
}

/** Nombre corto de cada ambiente, para las cabeceras y las tarjetas. */
export const NOMBRES_AMBIENTE: Record<string, string> = { dev: 'dev', qa: 'qa', pdn: 'pdn' };

/** Por qué no hay lista de breaking pendientes, en palabras de la persona. */
export const TEXTO_ESTADO_BREAKING: Record<BreakingPendientes['estado'], string> = {
  sin_breaking: 'Sin cambios que rompan compatibilidad entre pdn y qa.',
  con_breaking: 'Cambios que rompen compatibilidad entre pdn y qa.',
  no_disponible: 'No pudimos calcular el diff entre pdn y qa en este momento. Inténtalo más tarde.',
  no_soportado: 'Todavía no hay diff para este tipo de contrato (solo OpenAPI).',
};
