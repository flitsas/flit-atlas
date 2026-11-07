import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, ErrorApi, pedir } from '../../app/api';
import type { TipoDiagrama } from '../../app/diagramas/VistaDiagrama';

/** Diagramas (HU 6.6 y 6.7, ATLAS-ADR-0054): `/api/v1/diagramas` (contracts/openapi/atlas.v1.yaml). */
export type { TipoDiagrama };
export type VisibilidadDiagrama = 'interno_negocio' | 'interno_ti' | 'restringido';

export interface DiagramaResumen {
  id: string;
  codigo: string;
  aplicativoId: string;
  tipo: TipoDiagrama;
  titulo: string;
  visibilidad: VisibilidadDiagrama;
  versionActual: number;
  actualizadoEn: string;
}

export interface ResumenVersion {
  numero: number;
  origen: 'creacion' | 'edicion' | 'restauracion';
  restauradaDe: number | null;
  titulo: string;
  visibilidad: VisibilidadDiagrama;
  nota: string;
  autor: { id: string; nombre: string };
  guardadaEn: string;
  huella: string;
}

export interface DetalleVersion {
  version: ResumenVersion;
  fuente: string;
}

export interface DiagramaDetalle {
  diagrama: DiagramaResumen;
  version: DetalleVersion | null;
  referencias: { aplicativoId: string; slug: string; nombre: string }[];
  puedeEditar: boolean;
}

export interface Comentario {
  id: string;
  versionNumero: number;
  nodoClave: string | null;
  persona: { id: string; nombre: string };
  texto: string;
  escritoEn: string;
}

export interface AplicativoRef {
  id: string;
  slug: string;
  nombre: string;
}

export const TEXTO_TIPO: Record<TipoDiagrama, string> = { lienzo: 'Lienzo', mermaid: 'Mermaid', bpmn: 'Proceso BPMN' };

export const TEXTO_VISIBILIDAD: Record<VisibilidadDiagrama, string> = {
  interno_negocio: 'Interno de negocio',
  interno_ti: 'Interno de TI',
  restringido: 'Restringido',
};

export const TEXTO_ORIGEN: Record<ResumenVersion['origen'], string> = { creacion: 'Creación', edicion: 'Edición', restauracion: 'Restauración' };

/** Lo mínimo para empezar un diagrama de cada tipo. */
export const FUENTE_INICIAL: Record<TipoDiagrama, string> = {
  lienzo: JSON.stringify({ nodos: [{ id: 'nodo-1', tipo: 'sistema', etiqueta: 'Nuevo sistema', x: 0, y: 0 }], relaciones: [] }),
  mermaid: 'flowchart TD\n  Inicio --> Fin\n',
  bpmn: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="Definiciones" targetNamespace="https://atlas.flitsas.com/bpmn">
  <bpmn:process id="Proceso" isExecutable="false">
    <bpmn:startEvent id="Inicio" name="Inicio" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="Dibujo">
    <bpmndi:BPMNPlane id="Plano" bpmnElement="Proceso">
      <bpmndi:BPMNShape id="Inicio_di" bpmnElement="Inicio">
        <dc:Bounds x="160" y="80" width="36" height="36" />
      </bpmndi:BPMNShape>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`,
};

const CLAVE = ['diagramas'] as const;

async function leer<T>(ruta: string): Promise<T> {
  return (await (await pedir(ruta)).json()) as T;
}

export const useDiagramas = (filtros: { aplicativo?: string; texto?: string; tipo?: TipoDiagrama } = {}) =>
  useQuery({
    queryKey: [...CLAVE, 'lista', filtros],
    queryFn: () => {
      const q = new URLSearchParams({ limite: '100' });
      if (filtros.aplicativo) q.set('aplicativo', filtros.aplicativo);
      if (filtros.texto) q.set('texto', filtros.texto);
      if (filtros.tipo) q.set('tipo', filtros.tipo);
      return leer<{ elementos: DiagramaResumen[]; total: number }>(`/api/v1/diagramas?${q}`);
    },
  });

export const useDiagrama = (id: string) =>
  useQuery({ queryKey: [...CLAVE, id], queryFn: () => leer<DiagramaDetalle>(`/api/v1/diagramas/${encodeURIComponent(id)}`) });

export const useVersiones = (id: string) =>
  useQuery({ queryKey: [...CLAVE, id, 'versiones'], queryFn: () => leer<ResumenVersion[]>(`/api/v1/diagramas/${encodeURIComponent(id)}/versiones`) });

export const useVersion = (id: string, numero: number | null) =>
  useQuery({
    queryKey: [...CLAVE, id, 'version', numero],
    queryFn: () => leer<DetalleVersion>(`/api/v1/diagramas/${encodeURIComponent(id)}/versiones/${numero}`),
    enabled: numero !== null,
  });

export const useComentarios = (id: string) =>
  useQuery({ queryKey: [...CLAVE, id, 'comentarios'], queryFn: () => leer<Comentario[]>(`/api/v1/diagramas/${encodeURIComponent(id)}/comentarios`) });

export const useAplicativos = () =>
  useQuery({
    queryKey: [...CLAVE, 'aplicativos'],
    queryFn: async () => (await leer<{ elementos: AplicativoRef[] }>('/api/v1/catalogo/aplicativos')).elementos,
    staleTime: 5 * 60_000,
  });

export interface CamposCrear {
  codigo: string;
  aplicativoId: string;
  tipo: TipoDiagrama;
  titulo: string;
  visibilidad: VisibilidadDiagrama;
  fuente: string;
  nota: string;
}

export interface CamposGuardar {
  titulo: string;
  visibilidad: VisibilidadDiagrama;
  fuente: string;
  nota: string;
  versionBase: number;
}

export async function crearDiagrama(campos: CamposCrear): Promise<DiagramaDetalle> {
  return (await (await enviarJson('/api/v1/diagramas', 'POST', campos)).json()) as DiagramaDetalle;
}

export async function guardarDiagrama(id: string, campos: CamposGuardar): Promise<DiagramaDetalle> {
  return (await (await enviarJson(`/api/v1/diagramas/${encodeURIComponent(id)}`, 'PUT', campos)).json()) as DiagramaDetalle;
}

export async function restaurarVersion(id: string, numero: number, nota: string, versionBase: number): Promise<DiagramaDetalle> {
  return (await (await enviarJson(`/api/v1/diagramas/${encodeURIComponent(id)}/versiones/${numero}/restauracion`, 'POST', { nota, versionBase })).json()) as DiagramaDetalle;
}

/** Descarga el SVG de una versión del lienzo (HU 6.8, ATLAS-ADR-0054 §7). No se muestra en el shell: va como archivo. */
export async function descargarSvg(id: string, numero: number, codigo: string): Promise<void> {
  const respuesta = await pedir(`/api/v1/diagramas/${encodeURIComponent(id)}/versiones/${numero}/svg`);
  const url = URL.createObjectURL(await respuesta.blob());
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = `${codigo}-v${numero}.svg`;
  enlace.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function useComentar(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: (cuerpo: { versionNumero: number; texto: string }) => enviarJson(`/api/v1/diagramas/${encodeURIComponent(id)}/comentarios`, 'POST', cuerpo),
    onSuccess: () => cliente.invalidateQueries({ queryKey: [...CLAVE, id, 'comentarios'] }),
  });
}

export function useCambioDiagrama() {
  const cliente = useQueryClient();
  return () => cliente.invalidateQueries({ queryKey: CLAVE });
}

/** El mensaje del servidor si lo dio; si no, uno general. Los errores de validación traen uno por campo. */
export function mensajeDeError(error: unknown): string {
  const general = 'No pudimos guardar el diagrama. Revisa tu conexión y vuelve a intentarlo.';
  if (!(error instanceof ErrorApi)) return general;
  const errores = Object.values((error.cuerpo?.errors ?? {}) as Record<string, string[]>).flat();
  return errores.length > 0 ? errores.join(' ') : error.mensajePara(general);
}
