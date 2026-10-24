import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../../app/api';

/**
 * Guías de integración con revisiones (HU 3.7, ATLAS-ADR-0040): `/api/v1/contratos/{id}/guias` y
 * `/api/v1/contratos/guias/{guiaId}` (contracts/openapi/atlas.v1.yaml, etiqueta «Contratos»).
 */
export type Visibilidad = 'interna' | 'compartible';

export interface PersonaRef {
  id: string;
  nombre: string;
}

export interface GuiaResumen {
  id: string;
  contratoId: string;
  titulo: string;
  operacion: string | null;
  aplicaDesde: string | null;
  aplicaHasta: string | null;
  visibilidad: Visibilidad;
  estado: 'borrador' | 'publicada';
  revisionActual: number;
  revisionPublicada: number | null;
  publicadaEn: string | null;
  publicadaPor: PersonaRef | null;
  aprobadaPor: PersonaRef | null;
  actualizadaEn: string;
}

export interface ListadoGuias {
  contratoId: string;
  puedeEditar: boolean;
  puedeAprobar: boolean;
  elementos: GuiaResumen[];
}

export interface RevisionResumen {
  numero: number;
  origen: 'creacion' | 'edicion' | 'restauracion';
  restauradaDe: number | null;
  titulo: string;
  visibilidad: Visibilidad;
  nota: string;
  autor: PersonaRef;
  guardadaEn: string;
  huella: string;
  publicada: boolean;
}

export interface RevisionDetalle extends RevisionResumen {
  contenido: string;
  operacion: string | null;
  aplicaDesde: string | null;
  aplicaHasta: string | null;
}

export interface GuiaDetalle {
  guia: GuiaResumen;
  revision: RevisionDetalle;
  puedeEditar: boolean;
  puedeAprobar: boolean;
}

export interface CamposGuia {
  titulo: string;
  contenido: string;
  operacion: string;
  aplicaDesde: string;
  aplicaHasta: string;
  visibilidad: Visibilidad;
  nota: string;
}

export const TEXTO_VISIBILIDAD: Record<Visibilidad, string> = { interna: 'Interna', compartible: 'Compartible con clientes' };

export const TEXTO_ORIGEN: Record<RevisionResumen['origen'], string> = { creacion: 'Creación', edicion: 'Edición', restauracion: 'Restauración' };

const base = (guiaId: string) => `/api/v1/contratos/guias/${encodeURIComponent(guiaId)}`;

export const CLAVE_GUIAS = ['contratos', 'guias'] as const;

export async function listarGuias(contratoId: string, version?: string): Promise<ListadoGuias> {
  const consulta = version ? `?version=${encodeURIComponent(version)}` : '';
  return (await (await pedir(`/api/v1/contratos/${encodeURIComponent(contratoId)}/guias${consulta}`)).json()) as ListadoGuias;
}

export async function obtenerGuia(guiaId: string): Promise<GuiaDetalle> {
  return (await (await pedir(base(guiaId))).json()) as GuiaDetalle;
}

export async function listarRevisiones(guiaId: string): Promise<RevisionResumen[]> {
  return (await (await pedir(`${base(guiaId)}/revisiones`)).json()) as RevisionResumen[];
}

export async function obtenerRevision(guiaId: string, numero: number): Promise<RevisionDetalle> {
  return (await (await pedir(`${base(guiaId)}/revisiones/${numero}`)).json()) as RevisionDetalle;
}

/** Vacío = sin valor (operación, versiones): así lo espera la API. */
const cuerpo = (c: CamposGuia) => ({
  titulo: c.titulo,
  contenido: c.contenido,
  operacion: c.operacion.trim() || null,
  aplicaDesde: c.aplicaDesde.trim() || null,
  aplicaHasta: c.aplicaHasta.trim() || null,
  visibilidad: c.visibilidad,
  nota: c.nota,
});

export async function crearGuia(contratoId: string, campos: CamposGuia): Promise<GuiaDetalle> {
  return (await (await enviarJson(`/api/v1/contratos/${encodeURIComponent(contratoId)}/guias`, 'POST', cuerpo(campos))).json()) as GuiaDetalle;
}

export async function guardarGuia(guiaId: string, campos: CamposGuia, revisionBase: number): Promise<GuiaDetalle> {
  return (await (await enviarJson(base(guiaId), 'PUT', { ...cuerpo(campos), revisionBase })).json()) as GuiaDetalle;
}

export async function restaurarRevision(guiaId: string, numero: number, nota: string, revisionBase: number): Promise<GuiaDetalle> {
  return (await (await enviarJson(`${base(guiaId)}/revisiones/${numero}/restauracion`, 'POST', { nota, revisionBase })).json()) as GuiaDetalle;
}

export async function publicarGuia(guiaId: string, revision: number): Promise<GuiaDetalle> {
  return (await (await enviarJson(`${base(guiaId)}/publicacion`, 'POST', { revision })).json()) as GuiaDetalle;
}

export function useGuias(contratoId: string, version?: string) {
  return useQuery({ queryKey: [...CLAVE_GUIAS, 'lista', contratoId, version ?? ''], queryFn: () => listarGuias(contratoId, version), staleTime: 30_000 });
}

export function useGuia(guiaId: string | null) {
  return useQuery({ queryKey: [...CLAVE_GUIAS, 'detalle', guiaId], queryFn: () => obtenerGuia(guiaId!), enabled: guiaId !== null });
}

export function useRevisiones(guiaId: string) {
  return useQuery({ queryKey: [...CLAVE_GUIAS, 'revisiones', guiaId], queryFn: () => listarRevisiones(guiaId) });
}

export function useRevision(guiaId: string, numero: number | null) {
  return useQuery({
    queryKey: [...CLAVE_GUIAS, 'revision', guiaId, numero],
    queryFn: () => obtenerRevision(guiaId, numero!),
    enabled: numero !== null,
    // Las revisiones son inmutables.
    staleTime: Number.POSITIVE_INFINITY,
  });
}

/** Mutación que, al terminar, refresca la lista, el detalle y el historial. */
export function useCambioGuia<T>(accion: (datos: T) => Promise<GuiaDetalle>) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: accion,
    onSuccess: (detalle) => {
      cliente.setQueryData([...CLAVE_GUIAS, 'detalle', detalle.guia.id], detalle);
      void cliente.invalidateQueries({ queryKey: CLAVE_GUIAS });
    },
  });
}

/** Campos del editor a partir de una revisión (para editar sobre la última). */
export function camposDe(r: RevisionDetalle): CamposGuia {
  return {
    titulo: r.titulo,
    contenido: r.contenido,
    operacion: r.operacion ?? '',
    aplicaDesde: r.aplicaDesde ?? '',
    aplicaHasta: r.aplicaHasta ?? '',
    visibilidad: r.visibilidad,
    nota: '',
  };
}

export function textoVersiones(g: Pick<GuiaResumen, 'aplicaDesde' | 'aplicaHasta'>): string {
  if (!g.aplicaDesde && !g.aplicaHasta) return 'Todas las versiones';
  if (g.aplicaDesde && g.aplicaHasta) return `Versiones ${g.aplicaDesde} a ${g.aplicaHasta}`;
  return g.aplicaDesde ? `Desde la ${g.aplicaDesde}` : `Hasta la ${g.aplicaHasta}`;
}
