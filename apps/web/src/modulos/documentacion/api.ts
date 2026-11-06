import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, ErrorApi, pedir } from '../../app/api';
import { conRespaldoGuardado } from '../../pwa/sinConexion';
import { recurso, rutaDocumento, rutaVersiones, TOPE_RECURSO_BYTES } from './favoritos/sinConexion';

/** Documentación viva (HU 5.1 y 5.4, ATLAS-ADR-0053): `/api/v1/documentacion` (contracts/openapi/atlas.v1.yaml). */
export type VisibilidadDocumento = 'publico_clientes' | 'interno_negocio' | 'interno_ti' | 'restringido';
export type EstadoDocumento = 'borrador' | 'en_revision' | 'publicado' | 'archivado';
export type TipoDocumento = 'manual_usuario' | 'guia_funcional' | 'guia_integracion' | 'runbook' | 'ficha_comercial' | 'adr' | 'nota_version';

export interface DocumentoResumen {
  id: string;
  aplicativoId: string;
  origen: 'repo' | 'atlas';
  tipo: TipoDocumento;
  titulo: string;
  visibilidad: VisibilidadDocumento;
  audiencias: string[];
  responsable: string;
  estado: EstadoDocumento;
  ruta: string | null;
  versionVigente: number | null;
  revisionActual: number;
  actualizadoEn: string;
}

export interface ResumenVersion {
  numero: number;
  titulo: string;
  visibilidad: VisibilidadDocumento;
  esBorrador: boolean;
  sha256: string;
  repositorio: string | null;
  rama: string | null;
  commitSha: string | null;
  revisionOrigen: number | null;
  publicadoEn: string;
}

export interface DetalleVersion {
  version: ResumenVersion;
  contenido: string;
}

export interface DocumentoDetalle {
  documento: DocumentoResumen;
  canales: { ambiente: string; version: number }[];
  version: DetalleVersion | null;
  puedeEditar: boolean;
  puedeAprobar: boolean;
}

export interface ResumenRevision {
  numero: number;
  origen: 'creacion' | 'edicion' | 'restauracion';
  restauradaDe: number | null;
  titulo: string;
  visibilidad: VisibilidadDocumento;
  nota: string;
  autorPersonaId: string;
  guardadaEn: string;
  huella: string;
}

export interface DetalleRevision {
  revision: ResumenRevision;
  tipo: TipoDocumento;
  contenido: string;
  audiencias: string[];
  responsable: string;
}

export interface CamposDocumento {
  aplicativoId: string | null;
  tipo: TipoDocumento;
  titulo: string;
  contenido: string;
  visibilidad: VisibilidadDocumento;
  audiencias: string[];
  responsable: string;
  nota: string;
}

export const TEXTO_TIPO: Record<TipoDocumento, string> = {
  manual_usuario: 'Manual de usuario',
  guia_funcional: 'Guía funcional',
  guia_integracion: 'Guía de integración',
  runbook: 'Runbook',
  ficha_comercial: 'Ficha comercial',
  adr: 'ADR',
  nota_version: 'Nota de versión',
};

export const TEXTO_VISIBILIDAD: Record<VisibilidadDocumento, string> = {
  publico_clientes: 'Público para clientes',
  interno_negocio: 'Interno de negocio',
  interno_ti: 'Interno de TI',
  restringido: 'Restringido',
};

export const TEXTO_ESTADO: Record<EstadoDocumento, string> = {
  borrador: 'Borrador',
  en_revision: 'En revisión',
  publicado: 'Publicado',
  archivado: 'Archivado',
};

export const TEXTO_ORIGEN_REVISION: Record<ResumenRevision['origen'], string> = { creacion: 'Creación', edicion: 'Edición', restauracion: 'Restauración' };

const RAIZ = '/api/v1/documentacion/documentos';
const de = (id: string) => `${RAIZ}/${encodeURIComponent(id)}`;
const leer = async <T,>(ruta: string) => (await (await pedir(ruta)).json()) as T;
const escribir = async <T,>(ruta: string, metodo: 'POST' | 'PUT', cuerpo: unknown) => (await (await enviarJson(ruta, metodo, cuerpo)).json()) as T;

export const CLAVE_DOCUMENTOS = ['documentacion'] as const;

export interface FiltrosDocumentos {
  aplicativo?: string;
  texto?: string;
  tipo?: string;
}

export function listarDocumentos(filtros: FiltrosDocumentos = {}) {
  const q = new URLSearchParams({ tamano: '100' });
  if (filtros.aplicativo) q.set('aplicativo', filtros.aplicativo);
  if (filtros.texto?.trim()) q.set('texto', filtros.texto.trim());
  if (filtros.tipo) q.set('tipo', filtros.tipo);
  return leer<{ total: number; elementos: DocumentoResumen[] }>(`${RAIZ}?${q}`);
}

export const useDocumentos = (filtros: FiltrosDocumentos = {}) =>
  useQuery({ queryKey: [...CLAVE_DOCUMENTOS, 'lista', filtros], queryFn: () => listarDocumentos(filtros), staleTime: 30_000 });

export function useDocumento(id: string, version?: number) {
  return useQuery({
    queryKey: [...CLAVE_DOCUMENTOS, 'detalle', id, version ?? 'vigente'],
    // La versión vigente se puede leer sin conexión si el documento es un favorito (HU 5.4).
    queryFn: () =>
      version
        ? leer<DocumentoDetalle>(`${de(id)}?version=${version}`)
        : conRespaldoGuardado(recurso(rutaDocumento(id)), () => leer<DocumentoDetalle>(de(id)), TOPE_RECURSO_BYTES),
    retry: (veces, error) => !(error instanceof ErrorApi && error.estado === 404) && veces < 2,
    networkMode: 'always',
  });
}

export const useVersiones = (id: string) =>
  useQuery({
    queryKey: [...CLAVE_DOCUMENTOS, 'versiones', id],
    queryFn: () => conRespaldoGuardado(recurso(rutaVersiones(id)), () => leer<ResumenVersion[]>(`${de(id)}/versiones`), TOPE_RECURSO_BYTES),
    networkMode: 'always',
  });

export const useVersion = (id: string, numero: number | null) =>
  useQuery({
    queryKey: [...CLAVE_DOCUMENTOS, 'version', id, numero],
    queryFn: () => leer<DetalleVersion>(`${de(id)}/versiones/${numero}`),
    enabled: numero !== null,
    // Las versiones son inmutables.
    staleTime: Number.POSITIVE_INFINITY,
  });

export const useRevisiones = (id: string, activa = true) =>
  useQuery({ queryKey: [...CLAVE_DOCUMENTOS, 'revisiones', id], queryFn: () => leer<ResumenRevision[]>(`${de(id)}/revisiones`), enabled: activa });

export const useRevision = (id: string, numero: number | null) =>
  useQuery({
    queryKey: [...CLAVE_DOCUMENTOS, 'revision', id, numero],
    queryFn: () => leer<DetalleRevision>(`${de(id)}/revisiones/${numero}`),
    enabled: numero !== null && numero > 0,
    staleTime: Number.POSITIVE_INFINITY,
  });

/** Aplicativos para nombrar y filtrar; quien no ve el catálogo (403) sigue leyendo sin nombres. */
export interface AplicativoRef {
  id: string;
  slug: string;
  nombre: string;
}

export const useAplicativos = () =>
  useQuery({
    queryKey: [...CLAVE_DOCUMENTOS, 'aplicativos'],
    queryFn: async () => (await leer<{ elementos: AplicativoRef[] }>('/api/v1/catalogo/aplicativos')).elementos,
    staleTime: 5 * 60_000,
    retry: false,
  });

export const crearDocumento = (c: CamposDocumento) => escribir<DocumentoDetalle>(RAIZ, 'POST', c);

export const guardarDocumento = (id: string, c: CamposDocumento, revisionBase: number) => escribir<DocumentoDetalle>(de(id), 'PUT', { ...c, revisionBase });

export const restaurarRevision = (id: string, numero: number, nota: string, revisionBase: number) =>
  escribir<DocumentoDetalle>(`${de(id)}/revisiones/${numero}/restauracion`, 'POST', { nota, revisionBase });

export type AccionEstado = 'envio-revision' | 'devolucion' | 'publicacion' | 'archivo';

export const cambiarEstado = (id: string, accion: AccionEstado, revision: number) => escribir<DocumentoDetalle>(`${de(id)}/${accion}`, 'POST', { revision });

/** Mutación que, al terminar, refresca lo del módulo. */
export function useCambioDocumento<T>(accion: (datos: T) => Promise<DocumentoDetalle>) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: accion,
    onSuccess: () => cliente.invalidateQueries({ queryKey: CLAVE_DOCUMENTOS }),
  });
}

const MENSAJES: Record<string, string> = {
  revision_desactualizada: 'Alguien guardó otra revisión mientras trabajabas: recarga el documento y vuelve a intentarlo.',
  sin_cambios: 'No hay cambios frente a la última revisión.',
  aprobacion_requerida: 'Lo público para clientes solo lo aprueba el responsable principal o secundario del aplicativo.',
  documento_de_repo: 'Este documento vive en su repo: se cambia allí, con un PR.',
  documento_archivado: 'El documento está archivado.',
  estado_invalido: 'El documento ya no está en ese estado: recárgalo.',
  ya_publicada: 'Esta revisión ya está publicada.',
};

/** Texto para la persona, incluido el detalle de la lista blanca de MDX (422 `mdx_invalido`). */
export function mensajeDeError(error: unknown, porDefecto: string): string {
  if (!(error instanceof ErrorApi)) return porDefecto;
  const conocido = error.codigo ? MENSAJES[error.codigo] : undefined;
  if (conocido) return conocido;
  const detalle = error.cuerpo?.errores;
  if (Array.isArray(detalle) && detalle.length > 0) return `${error.mensajePara(porDefecto)} ${detalle.join(' ')}`;
  return error.estado === 403 ? error.mensajePara('No tienes permiso para esta acción.') : error.mensajePara(porDefecto);
}
