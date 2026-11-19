import { useSyncExternalStore } from 'react';

import { ErrorApi } from '../app/api';
import { guardar, leer, olvidar } from './almacen';

/**
 * Datos sin conexión de los módulos (prompt maestro §11, HU 2.12, ATLAS-ADR-0034): lo que la API ya le devolvió a la
 * persona se guarda en IndexedDB (`almacen.ts`) con su TTL y el permiso que lo exige, y sin red se muestra con el aviso
 * «Sin conexión: datos del <fecha>». Solo lo cargan las páginas de los módulos (va en su chunk, no en el inicial).
 *
 * Nunca se guardan teléfonos, aunque la persona tenga `atlas.equipo.ver_contacto`: cualquier campo `telefono` se deja
 * vacío antes de guardar. Todo se borra al cerrar sesión, al cambiar de persona y, por recurso, al perder su permiso.
 */
export interface RecursoGuardado {
  /** Clave en el dispositivo (`soporte.turnos`); la misma en el manifiesto del módulo y en su cliente. */
  clave: string;
  /** Permiso que exige verlo; si la persona lo pierde, se borra. */
  permiso: string;
  ttlHoras: number;
}

/** Sin red, `fetch` falla con `TypeError`; una respuesta del servidor (aunque sea un error) no es «sin conexión». */
export const esFallaDeRed = (error: unknown): boolean =>
  !(error instanceof ErrorApi) && (error instanceof TypeError || (typeof navigator !== 'undefined' && !navigator.onLine));

const CAMPOS_DE_CONTACTO = new Set(['telefono', 'telefonos', 'celular', 'movil']);

/** Copia sin datos de contacto: los teléfonos no se guardan en el dispositivo (decisión de ATLAS-ADR-0034). */
export function sinContacto<T>(datos: T): T {
  const limpiar = (valor: unknown): unknown => {
    if (Array.isArray(valor)) return valor.map(limpiar);
    if (valor && typeof valor === 'object') {
      return Object.fromEntries(
        Object.entries(valor as Record<string, unknown>).map(([clave, v]) => [clave, CAMPOS_DE_CONTACTO.has(clave.toLowerCase()) ? null : limpiar(v)]),
      );
    }
    return valor;
  };
  return limpiar(datos) as T;
}

// --- De dónde salen los datos que se ven: la red o lo guardado ---

const desde = new Map<string, Date>();
const oyentes = new Set<() => void>();

function marcar(clave: string, guardadoEn: Date | null) {
  const antes = desde.get(clave)?.getTime() ?? null;
  if (guardadoEn) desde.set(clave, guardadoEn);
  else desde.delete(clave);
  if (antes !== (guardadoEn?.getTime() ?? null)) oyentes.forEach((avisar) => avisar());
}

function suscribir(avisar: () => void) {
  oyentes.add(avisar);
  return () => {
    oyentes.delete(avisar);
  };
}

/** La fecha de lo guardado si lo que se muestra de `clave` salió del dispositivo (sin conexión); `null` si salió de la red. */
export function useDatosGuardados(clave: string): Date | null {
  return useSyncExternalStore(
    suscribir,
    () => desde.get(clave) ?? null,
    () => null,
  );
}

/** Guarda la respuesta (sin teléfonos) con su TTL y su permiso. Un error del almacén no rompe la página. */
export async function guardarRecurso(recurso: RecursoGuardado, datos: unknown): Promise<void> {
  await guardar(recurso.clave, sinContacto(datos), recurso.ttlHoras, Date.now(), recurso.permiso).catch(() => undefined);
}

/**
 * Consulta en línea y guarda lo que llega; sin red, lo último guardado (marcado para el aviso). Un error del servidor
 * se lanza como siempre.
 */
export async function conRespaldo<T>(recurso: RecursoGuardado, consulta: () => Promise<T>): Promise<T> {
  try {
    const datos = await consulta();
    marcar(recurso.clave, null);
    await guardarRecurso(recurso, datos);
    return datos;
  } catch (error) {
    if (!esFallaDeRed(error)) throw error;
    const guardado = await leer<T>(recurso.clave).catch(() => null);
    if (!guardado) throw error;
    marcar(recurso.clave, guardado.guardadoEn);
    return guardado.datos;
  }
}

/**
 * Como {@link conRespaldo}, pero sin red arma la respuesta a partir de otro recurso guardado (p. ej. un mes del
 * calendario a partir de los tres meses guardados, o los responsables de un aplicativo a partir del reparto). Si
 * `derivar` no puede (devuelve `null`), se lanza la falla de red. Con `guardar`, la respuesta en línea también se guarda
 * como ese recurso.
 */
export async function conRespaldoDerivado<T, G>(
  recurso: RecursoGuardado,
  consulta: () => Promise<T>,
  derivar: (guardado: G, guardadoEn: Date) => T | null,
  guardarComo?: (datos: T) => G | null,
): Promise<T> {
  try {
    const datos = await consulta();
    marcar(recurso.clave, null);
    const comoRecurso = guardarComo?.(datos);
    if (comoRecurso) await guardarRecurso(recurso, comoRecurso);
    return datos;
  } catch (error) {
    if (!esFallaDeRed(error)) throw error;
    const guardado = await leer<G>(recurso.clave).catch(() => null);
    const derivado = guardado ? derivar(guardado.datos, guardado.guardadoEn) : null;
    if (!guardado || derivado === null) throw error;
    marcar(recurso.clave, guardado.guardadoEn);
    return derivado;
  }
}

/** Bytes (UTF-8) de lo que se guardaría: un texto tal cual, lo demás como JSON. */
export function bytes(datos: unknown): number {
  const texto = typeof datos === 'string' ? datos : JSON.stringify(datos ?? null);
  return new TextEncoder().encode(texto).length;
}

/**
 * Para lo que la persona eligió guardar (sus favoritos, HU 4.9): en línea consulta y, **solo si ya estaba guardado**,
 * lo refresca (una consulta cualquiera no llena el dispositivo; con `topeBytes`, lo que pase del tope no se guarda). Si
 * el servidor responde 403 o 404 (perdió el acceso: compartición revocada o vencida, fuera de su alcance), lo guardado se
 * borra. Sin red, responde con lo guardado y lo marca para el aviso «Sin conexión: datos del <fecha>».
 */
export async function conRespaldoGuardado<T>(recurso: RecursoGuardado, consulta: () => Promise<T>, topeBytes = Infinity): Promise<T> {
  try {
    const datos = await consulta();
    marcar(recurso.clave, null);
    const previo = await leer(recurso.clave).catch(() => null);
    if (previo && bytes(datos) <= topeBytes) await guardarRecurso(recurso, datos);
    return datos;
  } catch (error) {
    if (error instanceof ErrorApi && (error.estado === 403 || error.estado === 404)) {
      await olvidar(recurso.clave).catch(() => undefined);
      marcar(recurso.clave, null);
      throw error;
    }
    if (!esFallaDeRed(error)) throw error;
    const guardado = await leer<T>(recurso.clave).catch(() => null);
    if (!guardado) throw error;
    marcar(recurso.clave, guardado.guardadoEn);
    return guardado.datos;
  }
}

/** Solo para pruebas: olvida de dónde salieron los datos. */
export function reiniciarOrigenes(): void {
  desde.clear();
  oyentes.forEach((avisar) => avisar());
}
