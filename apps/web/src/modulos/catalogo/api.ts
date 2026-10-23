import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../app/api';

/** Contrato de `/api/v1/catalogo` (contracts/openapi/atlas.v1.yaml, etiqueta «Catálogo», ATLAS-ADR-0028). */
export type Tipo = 'producto' | 'modulo' | 'servicio';
export type Grupo = 'flit2' | 'flito' | 'satelite' | 'legado' | 'infraestructura';
export type Estado = 'planeado' | 'activo' | 'en_retiro' | 'retirado';
export type Criticidad = 'alta' | 'media' | 'baja';
export type Ambiente = 'dev' | 'qa' | 'pdn';

export interface Referencia {
  id: string;
  slug: string;
  nombre: string;
}

export interface AplicativoResumen {
  id: string;
  slug: string;
  nombre: string;
  tipo: Tipo;
  grupo: Grupo;
  estado: Estado;
  criticidad: Criticidad;
  manejaDatosPersonales: boolean;
  padreId: string | null;
  descripcion: string | null;
  tecnologias: string[];
  etiquetas: string[];
  ancestros: Referencia[];
  hijos: number;
}

export interface ListadoAplicativos {
  total: number;
  elementos: AplicativoResumen[];
}

export interface FichaAplicativo extends Omit<AplicativoResumen, 'hijos'> {
  repo: string | null;
  rutaEnRepo: string | null;
  ambientes: Ambiente[];
  hijos: AplicativoResumen[];
  permisos: { editar: boolean; gestionar: boolean };
  creadoEn: string;
  actualizadoEn: string;
}

export interface SolicitudAplicativo {
  slug: string;
  nombre: string;
  tipo: Tipo;
  padreId: string | null;
  grupo: Grupo | null;
  descripcion: string | null;
  estado: Estado;
  criticidad: Criticidad;
  manejaDatosPersonales: boolean;
  repo: string | null;
  rutaEnRepo: string | null;
  tecnologias: string[];
  ambientes: Ambiente[];
  etiquetas: string[];
}

export interface FiltroCatalogo {
  texto?: string;
  grupo?: Grupo;
  estado?: Estado;
  criticidad?: Criticidad;
  tipo?: Tipo;
}

const BASE = '/api/v1/catalogo/aplicativos';
const CLAVE = ['catalogo', 'aplicativos'] as const;

const json = async <T>(respuesta: Response) => (await respuesta.json()) as T;

export function rutaListado(filtro: FiltroCatalogo = {}): string {
  const parametros = new URLSearchParams();
  for (const [clave, valor] of Object.entries(filtro)) {
    if (typeof valor === 'string' && valor.trim()) parametros.set(clave, valor.trim());
  }
  const consulta = parametros.toString();
  return consulta ? `${BASE}?${consulta}` : BASE;
}

export const listarAplicativos = async (filtro: FiltroCatalogo = {}) => json<ListadoAplicativos>(await pedir(rutaListado(filtro)));
export const obtenerFicha = async (slug: string) => json<FichaAplicativo>(await pedir(`${BASE}/${encodeURIComponent(slug)}`));
export const crearAplicativo = async (s: SolicitudAplicativo) => json<FichaAplicativo>(await enviarJson(BASE, 'POST', s));
export const actualizarAplicativo = async (id: string, s: SolicitudAplicativo) =>
  json<FichaAplicativo>(await enviarJson(`${BASE}/${encodeURIComponent(id)}`, 'PUT', s));
export const eliminarAplicativo = async (id: string) => {
  await enviarJson(`${BASE}/${encodeURIComponent(id)}`, 'DELETE');
};

export function useAplicativos(filtro: FiltroCatalogo = {}) {
  return useQuery({ queryKey: [...CLAVE, 'lista', filtro], queryFn: () => listarAplicativos(filtro), staleTime: 60_000 });
}

export function useFicha(slug: string) {
  return useQuery({ queryKey: [...CLAVE, 'ficha', slug], queryFn: () => obtenerFicha(slug), staleTime: 60_000, retry: false });
}

/** Al crear, editar o eliminar se refresca todo el catálogo (listados y fichas): es pequeño. */
export function useCambiosCatalogo() {
  const cliente = useQueryClient();
  const refrescar = () => cliente.invalidateQueries({ queryKey: CLAVE });
  return {
    crear: useMutation({ mutationFn: crearAplicativo, onSuccess: refrescar }),
    actualizar: useMutation({
      mutationFn: ({ id, solicitud }: { id: string; solicitud: SolicitudAplicativo }) => actualizarAplicativo(id, solicitud),
      onSuccess: refrescar,
    }),
    eliminar: useMutation({ mutationFn: eliminarAplicativo, onSuccess: refrescar }),
  };
}
