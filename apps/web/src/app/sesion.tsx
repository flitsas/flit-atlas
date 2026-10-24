import { useQuery, useQueryClient } from '@tanstack/react-query';
import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo } from 'react';
import { useNavigate } from 'react-router';

import { borrarDatosDePersona, borrarDatosLocales, guardar, leer } from '../pwa/almacen';
import { desactivarPush } from '../pwa/push';
import { ErrorApi, obtenerBanderas, obtenerYo, salir, type Yo } from './api';
import { marcarSesion } from './errores';
import type { ModuloAtlas } from './modulos/contrato';
import { MODULOS_REGISTRADOS } from './modulos/registro';
import { type AccesoUsuario, type NavegacionResuelta, resolverNavegacion } from './modulos/resolver';
import { guardarTema, leerTema } from './tema';

export interface EstadoSesion {
  yo: Yo | null;
  /** La API no respondió por falta de red y se muestra lo último guardado en el dispositivo. */
  sinConexion: boolean;
  guardadoEn?: Date;
}

export const CLAVE_YO = ['plataforma', 'yo'] as const;
export const CLAVE_BANDERAS = ['plataforma', 'banderas'] as const;

const RECURSO_YO = 'plataforma.yo';
const RECURSO_BANDERAS = 'plataforma.banderas';
/** De quién es lo guardado en el dispositivo (sin vencimiento práctico): si entra otra persona, se borra todo (HU 2.12). */
const RECURSO_DUENO = 'plataforma.dueno';
const TTL_HORAS = 24;
const TTL_DUENO_HORAS = 24 * 365;

/**
 * Lo guardado en el dispositivo es de una sola persona (HU 2.12, ATLAS-ADR-0034): sin sesión (401) o con otra persona,
 * se borra (caché sin conexión y acciones sin enviar) antes de guardar nada de la nueva.
 */
async function asegurarDueno(yo: Yo | null): Promise<void> {
  const dueno = await leer<string>(RECURSO_DUENO).catch(() => null);
  if (dueno && dueno.datos !== yo?.personaId) await borrarDatosDePersona().catch(() => undefined);
  if (yo && dueno?.datos !== yo.personaId) await guardar(RECURSO_DUENO, yo.personaId, TTL_DUENO_HORAS).catch(() => undefined);
}

/** Sin red, `fetch` falla con `TypeError`; un 5xx del proxy no es «sin conexión». */
const esFallaDeRed = (error: unknown) => !(error instanceof ErrorApi) && (error instanceof TypeError || !navigator.onLine);

export async function cargarSesion(): Promise<EstadoSesion> {
  try {
    const yo = await obtenerYo();
    // Los errores de la PWA solo se reportan con sesión (`errores.ts`).
    marcarSesion(yo !== null);
    await asegurarDueno(yo);
    if (yo) await guardar(RECURSO_YO, yo, TTL_HORAS).catch(() => undefined);
    return { yo, sinConexion: false };
  } catch (error) {
    const guardado = esFallaDeRed(error) ? await leer<Yo>(RECURSO_YO).catch(() => null) : null;
    if (guardado) return { yo: guardado.datos, sinConexion: true, guardadoEn: guardado.guardadoEn };
    throw error;
  }
}

async function cargarBanderas(): Promise<Record<string, boolean>> {
  try {
    const banderas = await obtenerBanderas();
    await guardar(RECURSO_BANDERAS, banderas, TTL_HORAS).catch(() => undefined);
    return banderas;
  } catch {
    // Sin respuesta del servidor: las últimas conocidas o, si no hay, los valores por defecto de cada manifiesto.
    return (await leer<Record<string, boolean>>(RECURSO_BANDERAS).catch(() => null))?.datos ?? {};
  }
}

export function useEstadoSesion() {
  return useQuery({ queryKey: CLAVE_YO, queryFn: cargarSesion, staleTime: 60_000, retry: false });
}

interface ValorSesion {
  yo: Yo;
  sinConexion: boolean;
  guardadoEn?: Date;
  acceso: AccesoUsuario;
  modulos: readonly ModuloAtlas[];
  navegacion: NavegacionResuelta;
  cerrarSesion: () => Promise<void>;
}

const ContextoSesion = createContext<ValorSesion | null>(null);

export function ProveedorSesion({
  estado,
  modulos = MODULOS_REGISTRADOS,
  cargando,
  children,
}: {
  estado: EstadoSesion & { yo: Yo };
  modulos?: readonly ModuloAtlas[];
  cargando: ReactNode;
  children: ReactNode;
}) {
  const cliente = useQueryClient();
  const navegar = useNavigate();
  const banderas = useQuery({ queryKey: CLAVE_BANDERAS, queryFn: cargarBanderas, staleTime: 5 * 60_000 });

  const acceso = useMemo<AccesoUsuario>(
    () => ({ banderas: banderas.data ?? {}, permisos: estado.yo.permisos, tipoPrincipal: estado.yo.tipoPrincipal }),
    [banderas.data, estado.yo.permisos, estado.yo.tipoPrincipal],
  );
  const navegacion = useMemo(() => resolverNavegacion(modulos, acceso), [modulos, acceso]);

  // Datos sin conexión (HU 2.12): con red y sesión, se guarda lo de cada módulo según los permisos. Carga diferida.
  const listo = !estado.sinConexion && !banderas.isPending;
  useEffect(() => {
    if (!listo) return;
    void import('../pwa/sincronizacion').then((m) => m.sincronizarSinConexion(modulos, acceso)).catch(() => undefined);
  }, [listo, modulos, acceso]);

  /**
   * Cierre de sesión completo (§6.3): quita la suscripción push de este dispositivo, borra la caché sin conexión
   * (IndexedDB y cachés del service worker) y revoca la sesión en el servidor, que además envía `Clear-Site-Data`.
   */
  const cerrarSesion = useCallback(async () => {
    const tema = leerTema();
    await desactivarPush().catch(() => undefined);
    await borrarDatosLocales().catch(() => undefined);
    await salir();
    marcarSesion(false);
    // Clear-Site-Data también borra la preferencia de tema; no es un dato de la sesión y se conserva.
    guardarTema(tema);
    // Primero la sesión vacía (la raíz pasa al ingreso) y luego el resto de datos de la persona fuera de la memoria.
    cliente.setQueryData<EstadoSesion>(CLAVE_YO, { yo: null, sinConexion: false });
    cliente.removeQueries({ predicate: (consulta) => consulta.queryKey[1] !== CLAVE_YO[1] });
    void navegar('/', { replace: true });
  }, [cliente, navegar]);

  const valor = useMemo<ValorSesion>(
    () => ({ ...estado, acceso, modulos, navegacion, cerrarSesion }),
    [estado, acceso, modulos, navegacion, cerrarSesion],
  );

  if (banderas.isPending) return <>{cargando}</>;
  return <ContextoSesion.Provider value={valor}>{children}</ContextoSesion.Provider>;
}

/** La sesión si hay `ProveedorSesion` arriba; `null` en las páginas públicas y en pruebas de componentes sueltos. */
export function useSesionOpcional(): ValorSesion | null {
  return useContext(ContextoSesion);
}

export function useSesion(): ValorSesion {
  const valor = useContext(ContextoSesion);
  if (!valor) throw new Error('useSesion se usa dentro de ProveedorSesion.');
  return valor;
}
