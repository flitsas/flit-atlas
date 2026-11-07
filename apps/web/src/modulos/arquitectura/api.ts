import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { pedir } from '../../app/api';

/** Arquitectura (HU 6.3 a 6.5, ATLAS-ADR-0056): `/api/v1/arquitectura` (contracts/openapi/atlas.v1.yaml). */
export type Canal = 'dev' | 'qa' | 'pdn';
export const CANALES: readonly Canal[] = ['pdn', 'qa', 'dev'];
export const TEXTO_CANAL: Record<Canal, string> = { pdn: 'Producción', qa: 'QA', dev: 'Desarrollo' };

export type Categoria = 'landscape' | 'contexto' | 'contenedores' | 'componentes' | 'dinamica' | 'despliegue' | 'otra';
export const ORDEN_CATEGORIAS: readonly Categoria[] = ['landscape', 'contexto', 'contenedores', 'componentes', 'dinamica', 'despliegue', 'otra'];
export const TEXTO_CATEGORIA: Record<Categoria, string> = {
  landscape: 'Landscape',
  contexto: 'Contexto',
  contenedores: 'Contenedores',
  componentes: 'Componentes',
  dinamica: 'Dinámicas',
  despliegue: 'Despliegue',
  otra: 'Otras',
};

export interface Publicacion {
  id: string;
  tipo: 'modelo' | 'despliegue';
  canal: Canal;
  cluster: string;
  rama: string;
  commitSha: string;
  version: string | null;
  estado: 'borrador' | 'publicado';
  visibilidad: 'interno_ti' | 'restringido';
  herramientaVersion: string | null;
  publicadoEn: string;
}

export interface EnlaceAplicativo {
  slug: string;
  nombre: string;
  ruta: string;
}

export interface Proyecto {
  codigo: string;
  aplicativo: EnlaceAplicativo | null;
  canales: { canal: Canal; publicacion: Publicacion }[];
}

export interface Vista {
  id: string;
  tipo: 'estatica' | 'dinamica' | 'despliegue';
  categoria: Categoria;
  titulo: string;
}

export interface DetallePublicacion {
  proyecto: string;
  publicacion: Publicacion;
  vistas: Vista[];
}

export interface FichaElemento {
  id: string;
  kind: string;
  titulo: string;
  descripcion: string | null;
  tecnologia: string | null;
  etiquetas: string[];
  metadatos: Record<string, string[]>;
  aplicativo: EnlaceAplicativo | null;
  contratos: { ruta: string; tipo: string; nombre: string | null; ultimaVersion: string | null; rutaAtlas: string | null }[];
  adr: { codigo: string; rutaAtlas: string }[];
  enlaces: { url: string; titulo: string | null }[];
  driftAbierto: number | null;
}

export interface Snapshot {
  ambiente: Canal;
  gitopsSha: string | null;
  generadoEn: string | null;
  cluster: {
    id: string;
    raiz: string | null;
    namespaces: {
      nombre: string;
      application: string | null;
      ingressHosts: string[];
      workloads: { kind: string; nombre: string; replicas: number | null; imagenes: { imagen: string; digest: string | null }[] }[];
    }[];
  };
  fueraDeK3s: { nombre: string; metodo: string | null; elementoModelo: string | null; verificadoEn: string | null }[] | null;
}

export interface Diff {
  altas: { tipo: string; id: string; detalle: string | null }[];
  bajas: { tipo: string; id: string; detalle: string | null }[];
  cambios: { tipo: string; id: string; detalle: string | null }[];
}

export interface EstadoCapa {
  estado: 'con_datos' | 'sin_datos';
  motivo: string | null;
}

export type Comparacion = 'con_drift' | 'sin_drift' | 'sin_datos';

export interface EstadoAmbiente {
  ambiente: Canal;
  modelo: EstadoCapa;
  gitops: EstadoCapa;
  argo: EstadoCapa;
  clusteres: { cluster: string; estado: 'con_datos' | 'sin_datos'; motivo: string | null; leidaEn: string | null }[];
  modeloGitops: Comparacion;
  gitopsArgo: Comparacion;
}

export interface Hallazgo {
  id: string;
  capa: 'modelo_gitops' | 'gitops_argo';
  ambiente: Canal;
  cluster: string | null;
  codigo: string;
  elementoId: string | null;
  sujeto: string | null;
  descripcion: string;
  severidad: 'media' | 'alta';
  estado: 'abierto' | 'resuelto';
  detectadoEn: string;
  resueltoEn: string | null;
}

export interface PanelDrift {
  ambientes: EstadoAmbiente[];
  hallazgos: Hallazgo[];
}

const RUTA = '/api/v1/arquitectura';

async function json<T>(ruta: string): Promise<T> {
  return (await pedir(ruta)).json() as Promise<T>;
}

export const useProyectos = () => useQuery({ queryKey: ['arquitectura', 'proyectos'], queryFn: () => json<Proyecto[]>(`${RUTA}/proyectos`) });

export function useVigente(proyecto: string, canal: Canal, en: string | null) {
  const parametros = new URLSearchParams({ canal });
  if (en) parametros.set('en', en);
  return useQuery({
    queryKey: ['arquitectura', 'vigente', proyecto, canal, en],
    queryFn: () => json<DetallePublicacion>(`${RUTA}/proyectos/${encodeURIComponent(proyecto)}/vigente?${parametros}`),
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export const useModelo = (publicacion: string | undefined) =>
  useQuery({
    queryKey: ['arquitectura', 'modelo', publicacion],
    queryFn: () => json<ModeloLikeC4>(`${RUTA}/publicaciones/${publicacion}/modelo`),
    enabled: Boolean(publicacion),
    // Una publicación es inmutable.
    staleTime: Number.POSITIVE_INFINITY,
  });

export const useFicha = (publicacion: string | undefined, elemento: string | null) =>
  useQuery({
    queryKey: ['arquitectura', 'ficha', publicacion, elemento],
    queryFn: () => json<FichaElemento>(`${RUTA}/publicaciones/${publicacion}/elementos/${encodeURIComponent(elemento!)}`),
    enabled: Boolean(publicacion && elemento),
    retry: false,
  });

export const useSnapshotsVigentes = (en: string | null) =>
  useQuery({
    queryKey: ['arquitectura', 'snapshots', en],
    queryFn: () => json<{ elementos: Publicacion[] }>(`${RUTA}/despliegue/vigentes${en ? `?en=${encodeURIComponent(en)}` : ''}`),
  });

export const useHistorialSnapshots = (ambiente: Canal | undefined, cluster: string | undefined) =>
  useQuery({
    queryKey: ['arquitectura', 'historial', ambiente, cluster],
    queryFn: () => json<{ elementos: Publicacion[] }>(`${RUTA}/despliegue/publicaciones?ambiente=${ambiente}&cluster=${encodeURIComponent(cluster!)}`),
    enabled: Boolean(ambiente && cluster),
  });

export const useSnapshot = (id: string | null) =>
  useQuery({
    queryKey: ['arquitectura', 'snapshot', id],
    queryFn: () => json<{ publicacion: Publicacion; snapshot: Snapshot }>(`${RUTA}/despliegue/publicaciones/${id}`),
    enabled: Boolean(id),
    staleTime: Number.POSITIVE_INFINITY,
  });

export const useDiffSnapshots = (a: string | null, b: string | null) =>
  useQuery({
    queryKey: ['arquitectura', 'diff', a, b],
    queryFn: () => json<Diff>(`${RUTA}/despliegue/diff/${a}/${b}`),
    enabled: Boolean(a && b),
    staleTime: Number.POSITIVE_INFINITY,
  });

export const usePanelDrift = (ambiente: Canal | null, estado: 'abierto' | 'resuelto') => {
  const parametros = new URLSearchParams({ estado });
  if (ambiente) parametros.set('ambiente', ambiente);
  return useQuery({ queryKey: ['arquitectura', 'drift', ambiente, estado], queryFn: () => json<PanelDrift>(`${RUTA}/drift?${parametros}`) });
};

/** Lo poco que el shell lee del JSON de LikeC4: los nodos de una vista, para la lista accesible. */
export interface ModeloLikeC4 {
  views?: Record<string, { nodes?: { id: string; title?: string; modelRef?: string; kind?: string }[] }>;
}

/** Lista de elementos de una vista, sin repetir: la alternativa accesible y legible al lienzo. */
export function elementosDeVista(modelo: ModeloLikeC4 | undefined, vista: string): { id: string; titulo: string }[] {
  const nodos = modelo?.views?.[vista]?.nodes ?? [];
  const vistos = new Map<string, string>();
  for (const nodo of nodos) {
    if (typeof nodo.modelRef === 'string' && !vistos.has(nodo.modelRef)) vistos.set(nodo.modelRef, nodo.title ?? nodo.modelRef);
  }
  return [...vistos].map(([id, titulo]) => ({ id, titulo })).sort((a, b) => a.titulo.localeCompare(b.titulo, 'es'));
}

export const TEXTO_MOTIVO: Record<string, string> = {
  sin_publicaciones: 'aún no se ha publicado ningún modelo en este ambiente',
  sin_snapshot: 'aún no hay un snapshot de gitops de este ambiente',
  sin_configurar: 'la lectura de Argo CD no está configurada en este clúster',
  sin_lectura: 'todavía no se ha leído Argo CD',
  lectura_vencida: 'la última lectura de Argo CD es muy vieja',
  sin_respuesta: 'Argo CD no respondió',
  respuesta_invalida: 'Argo CD respondió algo que no se pudo leer',
  url_invalida: 'la URL de Argo CD no es válida',
};

export const textoMotivo = (motivo: string | null): string =>
  motivo ? (TEXTO_MOTIVO[motivo] ?? (motivo.startsWith('http_') ? `Argo CD respondió ${motivo.slice(5)}` : motivo)) : 'no hay datos';
