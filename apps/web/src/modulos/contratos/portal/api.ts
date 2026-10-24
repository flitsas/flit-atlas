import { useQuery } from '@tanstack/react-query';

import { pedir } from '../../../app/api';
import type { TipoContrato } from '../api';

/**
 * Portal de clientes (HU 4.4, ATLAS-ADR-0043): `/api/v1/portal` (contracts/openapi/atlas.v1.yaml, etiqueta «Portal de
 * clientes»). Todo lo recorta el servidor: la organización de la sesión, el alcance de la persona y la vigencia de la
 * compartición. La PWA nunca recibe el spec completo, solo el podado.
 */
export type NivelPortal = 'resumen' | 'tecnico';

export interface AplicativoDelPortal {
  id: string;
  slug: string | null;
  nombre: string | null;
}

export interface ContratoCompartido {
  contratoId: string;
  codigo: string;
  nombre: string;
  tipo: TipoContrato;
  aplicativo: AplicativoDelPortal;
  versionId: string;
  version: string;
  estadoVersion: 'vigente' | 'deprecada' | 'retirada';
  retiroEn: string | null;
  nivel: NivelPortal;
  operaciones: number;
  guias: number;
}

export interface PaqueteCompartido {
  id: string;
  nombre: string;
  descripcion: string | null;
  revision: number;
  nivel: NivelPortal;
  puedeExportar: boolean;
  puedeSandbox: boolean;
  vigenteHasta: string | null;
  contratos: ContratoCompartido[];
}

export interface AvisoDelPortal {
  tipo: 'deprecada' | 'retirada';
  paqueteId: string;
  paquete: string;
  contratoId: string;
  contrato: string;
  version: string;
  retiroEn: string | null;
  mensaje: string;
}

export interface DocumentoDelAplicativo {
  id: string;
  titulo: string;
  tipo: string;
  ruta: string;
}

export interface AplicativoCompartido {
  aplicativo: AplicativoDelPortal;
  paquetes: string[];
  contratos: number;
  documentos: DocumentoDelAplicativo[];
}

export interface InicioPortal {
  organizacion: { id: string; nombre: string };
  aplicativos: AplicativoCompartido[];
  paquetes: PaqueteCompartido[];
  avisos: AvisoDelPortal[];
}

export interface OperacionCompartida {
  clave: string;
  metodo: string | null;
  ruta: string | null;
  operationId: string | null;
  resumen: string | null;
  etiquetas: string[];
}

export interface GuiaCompartida {
  id: string;
  titulo: string;
  operacion: string | null;
  revision: number;
}

export interface ContratoDelPaquete {
  contrato: ContratoCompartido;
  operaciones: OperacionCompartida[];
  guias: GuiaCompartida[];
}

export interface DetallePaquetePortal {
  paquete: PaqueteCompartido;
  contratos: ContratoDelPaquete[];
  avisos: AvisoDelPortal[];
}

export interface GuiaPortal {
  id: string;
  paqueteId: string;
  contratoId: string;
  titulo: string;
  operacion: string | null;
  revision: number;
  contenido: string;
}

export interface CambioPortal {
  id: string;
  texto: string;
  nivel: number;
  metodo: string | null;
  ruta: string | null;
}

export interface CambiosPortal {
  contratoId: string;
  version: string;
  base: string | null;
  elementos: CambioPortal[];
}

export interface ResultadoPortal {
  tipo: 'paquete' | 'contrato' | 'operacion';
  titulo: string;
  detalle: string | null;
  paqueteId: string;
  contratoId: string | null;
  ruta: string;
}

const BASE = '/api/v1/portal';
const paquete = (id: string) => `${BASE}/paquetes/${encodeURIComponent(id)}`;

export const CLAVE_PORTAL = ['portal'] as const;

export async function obtenerInicio(): Promise<InicioPortal> {
  return (await (await pedir(`${BASE}/inicio`)).json()) as InicioPortal;
}

export async function obtenerPaquetePortal(id: string): Promise<DetallePaquetePortal> {
  return (await (await pedir(paquete(id))).json()) as DetallePaquetePortal;
}

/** El spec podado, como texto (YAML o JSON, el formato en que se publicó). */
export async function obtenerSpecPodado(paqueteId: string, contratoId: string): Promise<string> {
  return (await pedir(`${paquete(paqueteId)}/contratos/${encodeURIComponent(contratoId)}/spec`)).text();
}

export async function obtenerCambios(paqueteId: string, contratoId: string): Promise<CambiosPortal> {
  return (await (await pedir(`${paquete(paqueteId)}/contratos/${encodeURIComponent(contratoId)}/cambios`)).json()) as CambiosPortal;
}

export async function obtenerGuiaPortal(paqueteId: string, guiaId: string): Promise<GuiaPortal> {
  return (await (await pedir(`${paquete(paqueteId)}/guias/${encodeURIComponent(guiaId)}`)).json()) as GuiaPortal;
}

export async function buscarEnPortal(texto: string): Promise<ResultadoPortal[]> {
  return (await (await pedir(`${BASE}/buscar?q=${encodeURIComponent(texto)}`)).json()) as ResultadoPortal[];
}

// Lo compartido cambia poco, pero una revocación o un vencimiento deben notarse pronto: un minuto de caché.
const FRESCO = 60_000;

export function useInicioPortal() {
  return useQuery({ queryKey: [...CLAVE_PORTAL, 'inicio'], queryFn: obtenerInicio, staleTime: FRESCO });
}

export function usePaquetePortal(id: string) {
  return useQuery({ queryKey: [...CLAVE_PORTAL, 'paquete', id], queryFn: () => obtenerPaquetePortal(id), staleTime: FRESCO });
}

export function useSpecPodado(paqueteId: string, contratoId: string, habilitado: boolean) {
  return useQuery({
    queryKey: [...CLAVE_PORTAL, 'spec', paqueteId, contratoId],
    queryFn: () => obtenerSpecPodado(paqueteId, contratoId),
    enabled: habilitado,
    staleTime: FRESCO,
  });
}

export function useCambiosPortal(paqueteId: string, contratoId: string, habilitado: boolean) {
  return useQuery({
    queryKey: [...CLAVE_PORTAL, 'cambios', paqueteId, contratoId],
    queryFn: () => obtenerCambios(paqueteId, contratoId),
    enabled: habilitado,
    staleTime: FRESCO,
  });
}

export function useGuiaPortal(paqueteId: string, guiaId: string) {
  return useQuery({ queryKey: [...CLAVE_PORTAL, 'guia', paqueteId, guiaId], queryFn: () => obtenerGuiaPortal(paqueteId, guiaId), staleTime: FRESCO });
}

export function useBusquedaPortal(texto: string) {
  const limpio = texto.trim();
  return useQuery({
    queryKey: [...CLAVE_PORTAL, 'buscar', limpio],
    queryFn: () => buscarEnPortal(limpio),
    enabled: limpio.length >= 2,
    staleTime: FRESCO,
  });
}
