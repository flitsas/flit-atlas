import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../../app/api';
import type { PersonaRef } from './api';

/**
 * Comparticiones de paquetes con organizaciones (HU 4.3, ATLAS-ADR-0043): `/api/v1/contratos/paquetes/{id}/comparticiones`
 * y `/api/v1/contratos/organizaciones/{id}/comparticiones`. Compartir, cambiar y revocar piden un ingreso reciente
 * (`reautenticacion_requerida`).
 */
export type EstadoComparticion = 'vigente' | 'programada' | 'vencida' | 'revocada';

export interface Comparticion {
  id: string;
  paqueteId: string;
  paqueteNombre: string;
  paqueteEstado: 'borrador' | 'publicado' | 'archivado';
  organizacion: { id: string; nombre: string; tipo: 'cliente' | 'proveedor' | null; estado: 'activa' | 'suspendida' | null };
  revisionFijada: number | null;
  revisionQueVe: number | null;
  puedeExportar: boolean;
  puedeSandbox: boolean;
  vigenteDesde: string;
  vigenteHasta: string | null;
  estado: EstadoComparticion;
  otorgadoPor: PersonaRef;
  creadaEn: string;
  actualizadaEn: string;
  revocadaEn: string | null;
  puedeGestionar: boolean;
}

export interface ListadoComparticiones {
  puedeCompartir: boolean;
  elementos: Comparticion[];
}

export interface OrganizacionCompartible {
  id: string;
  nombre: string;
  tipo: 'cliente' | 'proveedor';
}

export interface SolicitudComparticion {
  organizacionId?: string;
  revisionFijada: number | null;
  puedeExportar: boolean;
  puedeSandbox: boolean;
  vigenteHasta: string | null;
}

export const TEXTO_ESTADO_COMPARTICION: Record<EstadoComparticion, string> = {
  vigente: 'Vigente',
  programada: 'Programada',
  vencida: 'Vencida',
  revocada: 'Revocada',
};

const rutaPaquete = (id: string) => `/api/v1/contratos/paquetes/${encodeURIComponent(id)}/comparticiones`;
const CLAVE = ['contratos', 'comparticiones'] as const;

/**
 * La fecha de un `<input type="date">` como el final de ese día en Bogotá (UTC−5, sin horario de verano): «hasta el 30»
 * incluye el 30.
 */
export function finDelDia(fecha: string): string {
  return `${fecha}T23:59:59-05:00`;
}

export async function listarComparticiones(paqueteId: string): Promise<ListadoComparticiones> {
  return (await (await pedir(rutaPaquete(paqueteId))).json()) as ListadoComparticiones;
}

export async function listarComparticionesDeOrganizacion(organizacionId: string): Promise<ListadoComparticiones> {
  return (await (await pedir(`/api/v1/contratos/organizaciones/${encodeURIComponent(organizacionId)}/comparticiones`)).json()) as ListadoComparticiones;
}

export async function listarOrganizacionesCompartibles(): Promise<OrganizacionCompartible[]> {
  return (await (await pedir('/api/v1/contratos/comparticiones/organizaciones')).json()) as OrganizacionCompartible[];
}

export async function compartir(paqueteId: string, solicitud: SolicitudComparticion): Promise<Comparticion> {
  return (await (await enviarJson(rutaPaquete(paqueteId), 'POST', solicitud)).json()) as Comparticion;
}

export async function cambiarComparticion(paqueteId: string, id: string, solicitud: SolicitudComparticion): Promise<Comparticion> {
  return (await (await enviarJson(`${rutaPaquete(paqueteId)}/${encodeURIComponent(id)}`, 'PUT', solicitud)).json()) as Comparticion;
}

export async function revocarComparticion(paqueteId: string, id: string): Promise<Comparticion> {
  return (await (await enviarJson(`${rutaPaquete(paqueteId)}/${encodeURIComponent(id)}`, 'DELETE')).json()) as Comparticion;
}

export function useComparticiones(paqueteId: string) {
  return useQuery({ queryKey: [...CLAVE, 'paquete', paqueteId], queryFn: () => listarComparticiones(paqueteId), staleTime: 30_000 });
}

export function useOrganizacionesCompartibles(habilitado: boolean) {
  return useQuery({ queryKey: [...CLAVE, 'organizaciones'], queryFn: listarOrganizacionesCompartibles, enabled: habilitado, staleTime: 60_000 });
}

export function useCambioComparticion<T>(paqueteId: string, accion: (datos: T) => Promise<Comparticion>) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: accion,
    onSuccess: () => void cliente.invalidateQueries({ queryKey: [...CLAVE, 'paquete', paqueteId] }),
  });
}
