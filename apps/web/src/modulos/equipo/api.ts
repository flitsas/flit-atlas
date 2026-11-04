import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { pedir } from '../../app/api';

/**
 * Cliente del directorio interno (`/api/v1/plataforma/directorio`, etiqueta «Directorio», ATLAS-ADR-0030): empleados
 * sincronizados a diario con Microsoft Graph. El teléfono solo llega con `atlas.equipo.ver_contacto`.
 */
export interface PersonaDirectorio {
  id: string;
  nombre: string;
  correo: string;
  cargo: string | null;
  area: string | null;
  estado: 'activa' | 'inactiva' | 'suspendida';
  /** URL de la foto, versionada por su hash; `null` sin foto. */
  foto: string | null;
  /** `null` sin teléfono o sin `atlas.equipo.ver_contacto`. */
  telefono: string | null;
  sincronizadaEn: string | null;
}

export interface ResultadoDirectorio {
  personas: PersonaDirectorio[];
  hayMas: boolean;
  veContacto: boolean;
}

export interface EstadoSincronizacion {
  id: string;
  disparo: 'programada' | 'manual';
  origen: string;
  resultado: 'exitosa' | 'fallida';
  inicio: string;
  fin: string | null;
  creadas: number;
  actualizadas: number;
  deshabilitadas: number;
  reactivadas: number;
  sinCambios: number;
  omitidas: number;
  retenidas: number;
  error: string | null;
}

export interface VistaSincronizacion {
  disponible: boolean;
  programada: boolean;
  origen: 'enlazadas' | 'grupo' | 'tenant';
  /** HH:mm, hora de Bogotá. */
  hora: string;
  proxima: string | null;
  enCurso: boolean;
  ultima: EstadoSincronizacion | null;
}

/** Mínimo de caracteres de la búsqueda que acepta el servidor. */
export const BUSQUEDA_MINIMA = 2;

const DIRECTORIO = '/api/v1/plataforma/directorio';
const SINCRONIZACION = `${DIRECTORIO}/sincronizacion`;

export async function listarDirectorio(q: string, inactivas: boolean, signal?: AbortSignal): Promise<ResultadoDirectorio> {
  const parametros = new URLSearchParams();
  if (q.trim()) parametros.set('q', q.trim());
  if (inactivas) parametros.set('inactivas', 'true');
  const consulta = parametros.toString();
  return (await (await pedir(consulta ? `${DIRECTORIO}?${consulta}` : DIRECTORIO, signal ? { signal } : {})).json()) as ResultadoDirectorio;
}

export async function obtenerSincronizacion(): Promise<VistaSincronizacion> {
  return (await (await pedir(SINCRONIZACION)).json()) as VistaSincronizacion;
}

/** Pide una corrida ya (202); corre en segundo plano en el servidor. */
export async function sincronizarAhora(): Promise<void> {
  await pedir(SINCRONIZACION, { method: 'POST' });
}

export function useDirectorio(q: string, inactivas: boolean) {
  // Sin texto se lista todo; con una sola letra no se consulta (el servidor la rechaza).
  const valida = q.trim().length === 0 || q.trim().length >= BUSQUEDA_MINIMA;
  return useQuery({
    queryKey: ['equipo', 'directorio', q.trim(), inactivas],
    queryFn: ({ signal }) => listarDirectorio(q, inactivas, signal),
    enabled: valida,
    staleTime: 5 * 60_000,
    placeholderData: keepPreviousData,
  });
}

export function useSincronizacion(habilitada: boolean, esperando: boolean) {
  return useQuery({
    queryKey: ['equipo', 'sincronizacion'],
    queryFn: obtenerSincronizacion,
    enabled: habilitada,
    // Mientras una corrida pedida no termina, se consulta cada 2 s.
    refetchInterval: esperando ? 2_000 : false,
  });
}

export function useSincronizarAhora() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: sincronizarAhora,
    onSettled: () => cliente.invalidateQueries({ queryKey: ['equipo', 'sincronizacion'] }),
  });
}
