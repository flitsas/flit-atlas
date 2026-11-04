import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { enviarJson, pedir } from '../../app/api';
import { conRespaldoDerivado } from '../../pwa/sinConexion';
import { RECURSO_APLICATIVOS } from './recursos';

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

const sinTildes = (texto: string) => texto.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();

/** El filtro aplicado en el dispositivo sobre el listado guardado (sin conexión): igual al del servidor en lo esencial. */
export function filtrarListado(listado: ListadoAplicativos, filtro: FiltroCatalogo): ListadoAplicativos {
  const texto = sinTildes(filtro.texto?.trim() ?? '');
  const elementos = listado.elementos.filter(
    (a) =>
      (!filtro.grupo || a.grupo === filtro.grupo) &&
      (!filtro.estado || a.estado === filtro.estado) &&
      (!filtro.criticidad || a.criticidad === filtro.criticidad) &&
      (!filtro.tipo || a.tipo === filtro.tipo) &&
      (!texto || [a.nombre, a.slug, a.descripcion ?? '', ...a.etiquetas, ...a.tecnologias].some((c) => sinTildes(c).includes(texto))),
  );
  return { total: elementos.length, elementos };
}

const sinFiltros = (filtro: FiltroCatalogo) => Object.values(filtro).every((v) => typeof v !== 'string' || !v.trim());

/** En línea, el listado sin filtros refresca lo guardado; sin conexión, se filtra lo guardado (HU 2.12). */
export const listarAplicativos = async (filtro: FiltroCatalogo = {}) =>
  conRespaldoDerivado<ListadoAplicativos, ListadoAplicativos>(
    RECURSO_APLICATIVOS,
    async () => json<ListadoAplicativos>(await pedir(rutaListado(filtro))),
    (guardado) => filtrarListado(guardado, filtro),
    (datos) => (sinFiltros(filtro) ? datos : null),
  );

/** Sin conexión, la ficha se arma con lo que trae el listado guardado (sin repo, ambientes ni permisos de edición). */
export const obtenerFicha = async (slug: string) =>
  conRespaldoDerivado<FichaAplicativo, ListadoAplicativos>(
    RECURSO_APLICATIVOS,
    async () => json<FichaAplicativo>(await pedir(`${BASE}/${encodeURIComponent(slug)}`)),
    (guardado, guardadoEn) => {
      const aplicativo = guardado.elementos.find((a) => a.slug === slug);
      if (!aplicativo) return null;
      return {
        ...aplicativo,
        repo: null,
        rutaEnRepo: null,
        ambientes: [],
        hijos: guardado.elementos.filter((a) => a.padreId === aplicativo.id),
        permisos: { editar: false, gestionar: false },
        creadoEn: guardadoEn.toISOString(),
        actualizadoEn: guardadoEn.toISOString(),
      };
    },
  );
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
