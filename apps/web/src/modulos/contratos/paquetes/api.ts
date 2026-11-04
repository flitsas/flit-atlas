import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../../app/api';
import type { OperacionIndice, TipoContrato } from '../api';

/**
 * Paquetes de contratos con revisiones (HU 4.1, ATLAS-ADR-0041): `/api/v1/contratos/paquetes`
 * (contracts/openapi/atlas.v1.yaml, etiqueta «Contratos»).
 */
export type EstadoPaquete = 'borrador' | 'publicado' | 'archivado';

export interface PersonaRef {
  id: string;
  nombre: string;
}

export interface AplicativoDelContrato {
  id: string;
  slug: string | null;
  nombre: string | null;
}

export interface PaqueteResumen {
  id: string;
  nombre: string;
  descripcion: string | null;
  estado: EstadoPaquete;
  revisionActual: number;
  revisionPublicada: number | null;
  cambiosSinPublicar: boolean;
  publicadaEn: string | null;
  publicadaPor: PersonaRef | null;
  contratos: string[];
  operaciones: number;
  actualizadoEn: string;
  puedeGestionar: boolean;
}

export interface ListadoPaquetes {
  puedeCrear: boolean;
  elementos: PaqueteResumen[];
}

export interface GuiaEnPaquete {
  guiaId: string;
  titulo: string | null;
  revision: number;
}

export interface EntradaPaquete {
  contratoId: string;
  codigo: string | null;
  nombre: string | null;
  tipo: TipoContrato | null;
  visibilidad: 'interna' | 'compartible' | null;
  aplicativo: AplicativoDelContrato | null;
  versionId: string;
  version: string | null;
  estadoVersion: 'vigente' | 'deprecada' | 'retirada' | null;
  operaciones: string[];
  guias: GuiaEnPaquete[];
}

export interface RevisionPaqueteResumen {
  numero: number;
  nombre: string;
  nota: string;
  autor: PersonaRef;
  guardadaEn: string;
  huella: string;
  contratos: number;
  operaciones: number;
  guias: number;
  vigente: boolean;
}

export interface RevisionPaqueteDetalle {
  numero: number;
  nombre: string;
  descripcion: string | null;
  definicion: EntradaPaquete[];
  nota: string;
  autor: PersonaRef;
  guardadaEn: string;
  huella: string;
  vigente: boolean;
}

export interface PaqueteDetalle {
  paquete: PaqueteResumen;
  revision: RevisionPaqueteDetalle;
  revisiones: RevisionPaqueteResumen[];
}

export interface VersionElegible {
  id: string;
  version: string;
  estado: 'vigente' | 'deprecada';
  retiroEn: string | null;
  publicadoEn: string;
  operaciones: number;
  publicas: number;
}

export interface GuiaElegible {
  id: string;
  titulo: string;
  operacion: string | null;
  revisionPublicada: number;
  aplicaDesde: string | null;
  aplicaHasta: string | null;
}

export interface ContratoElegible {
  id: string;
  codigo: string;
  nombre: string;
  tipo: TipoContrato;
  aplicativo: AplicativoDelContrato;
  versiones: VersionElegible[];
  guias: GuiaElegible[];
}

export interface OperacionesParaPaquete {
  contratoId: string;
  versionId: string;
  version: string;
  total: number;
  etiquetas: string[];
  elementos: OperacionIndice[];
}

/** Lo que se manda al guardar una revisión. */
export interface SolicitudPaquete {
  nombre: string;
  descripcion: string | null;
  definicion: Array<{ contratoId: string; versionId: string; operaciones: string[]; guias: Array<{ guiaId: string; revision: number | null }> }>;
  nota: string;
  revisionBase?: number;
}

export const TEXTO_ESTADO: Record<EstadoPaquete, string> = { borrador: 'Borrador', publicado: 'Publicado', archivado: 'Archivado' };

const BASE = '/api/v1/contratos/paquetes';
const rutaPaquete = (id: string) => `${BASE}/${encodeURIComponent(id)}`;

export const CLAVE_PAQUETES = ['contratos', 'paquetes'] as const;

export async function listarPaquetes(filtro: { estado?: EstadoPaquete; contrato?: string } = {}): Promise<ListadoPaquetes> {
  const parametros = new URLSearchParams();
  if (filtro.estado) parametros.set('estado', filtro.estado);
  if (filtro.contrato) parametros.set('contrato', filtro.contrato);
  const consulta = parametros.toString();
  return (await (await pedir(`${BASE}${consulta ? `?${consulta}` : ''}`)).json()) as ListadoPaquetes;
}

export async function obtenerPaquete(id: string): Promise<PaqueteDetalle> {
  return (await (await pedir(rutaPaquete(id))).json()) as PaqueteDetalle;
}

export async function obtenerRevisionPaquete(id: string, numero: number): Promise<RevisionPaqueteDetalle> {
  return (await (await pedir(`${rutaPaquete(id)}/revisiones/${numero}`)).json()) as RevisionPaqueteDetalle;
}

export async function listarElegibles(): Promise<ContratoElegible[]> {
  return ((await (await pedir(`${BASE}/elegibles`)).json()) as { elementos: ContratoElegible[] }).elementos;
}

export async function listarOperacionesParaPaquete(versionId: string, filtro: { etiqueta?: string; soloPublicas?: boolean }): Promise<OperacionesParaPaquete> {
  const parametros = new URLSearchParams();
  if (filtro.etiqueta) parametros.set('etiqueta', filtro.etiqueta);
  if (filtro.soloPublicas) parametros.set('soloPublicas', 'true');
  const consulta = parametros.toString();
  return (await (await pedir(`${BASE}/versiones/${encodeURIComponent(versionId)}/operaciones${consulta ? `?${consulta}` : ''}`)).json()) as OperacionesParaPaquete;
}

export async function crearPaquete(solicitud: SolicitudPaquete): Promise<PaqueteDetalle> {
  return (await (await enviarJson(BASE, 'POST', solicitud)).json()) as PaqueteDetalle;
}

export async function revisarPaquete(id: string, solicitud: SolicitudPaquete): Promise<PaqueteDetalle> {
  return (await (await enviarJson(`${rutaPaquete(id)}/revisiones`, 'POST', solicitud)).json()) as PaqueteDetalle;
}

export async function publicarPaquete(id: string, revision: number): Promise<PaqueteDetalle> {
  return (await (await enviarJson(`${rutaPaquete(id)}/publicacion`, 'POST', { revision })).json()) as PaqueteDetalle;
}

export async function fijarRevision(id: string, numero: number): Promise<PaqueteDetalle> {
  return (await (await enviarJson(`${rutaPaquete(id)}/revisiones/${numero}/fijacion`, 'POST', {})).json()) as PaqueteDetalle;
}

export async function archivarPaquete(id: string): Promise<PaqueteDetalle> {
  return (await (await enviarJson(`${rutaPaquete(id)}/archivo`, 'POST', {})).json()) as PaqueteDetalle;
}

export function usePaquetes(filtro: { estado?: EstadoPaquete; contrato?: string } = {}) {
  return useQuery({ queryKey: [...CLAVE_PAQUETES, 'lista', filtro], queryFn: () => listarPaquetes(filtro), staleTime: 30_000 });
}

export function usePaquete(id: string) {
  return useQuery({ queryKey: [...CLAVE_PAQUETES, 'detalle', id], queryFn: () => obtenerPaquete(id) });
}

export function useRevisionPaquete(id: string, numero: number | null) {
  return useQuery({
    queryKey: [...CLAVE_PAQUETES, 'revision', id, numero],
    queryFn: () => obtenerRevisionPaquete(id, numero!),
    enabled: numero !== null,
    // La revisión es inmutable; lo que cambia (estado de la versión, vigente) se recarga con el detalle.
    staleTime: 60_000,
  });
}

export function useElegibles() {
  return useQuery({ queryKey: [...CLAVE_PAQUETES, 'elegibles'], queryFn: listarElegibles, staleTime: 60_000 });
}

export function useOperacionesParaPaquete(versionId: string, filtro: { etiqueta?: string; soloPublicas?: boolean }) {
  return useQuery({
    queryKey: [...CLAVE_PAQUETES, 'operaciones', versionId, filtro.etiqueta ?? '', !!filtro.soloPublicas],
    queryFn: () => listarOperacionesParaPaquete(versionId, filtro),
    // El índice de una versión es inmutable.
    staleTime: Number.POSITIVE_INFINITY,
    // Al cambiar el filtro se ve la lista anterior mientras llega la nueva (nunca la de otra versión).
    placeholderData: (anterior) => (anterior?.versionId === versionId ? anterior : undefined),
  });
}

/** Mutación que, al terminar, deja el detalle en la caché y refresca la lista y el historial. */
export function useCambioPaquete<T>(accion: (datos: T) => Promise<PaqueteDetalle>) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: accion,
    onSuccess: (detalle) => {
      cliente.setQueryData([...CLAVE_PAQUETES, 'detalle', detalle.paquete.id], detalle);
      void cliente.invalidateQueries({ queryKey: [...CLAVE_PAQUETES, 'lista'] });
      void cliente.invalidateQueries({ queryKey: [...CLAVE_PAQUETES, 'revision', detalle.paquete.id] });
    },
  });
}
