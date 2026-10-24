import { type DBSchema, deleteDB, openDB } from 'idb';

/**
 * Caché sin conexión en IndexedDB (prompt maestro §11): cada recurso con su TTL. Solo se guarda lo que los permisos
 * de la persona permiten, y todo se borra al cerrar sesión (`borrarDatosLocales`).
 */
const BASE = 'atlas';
/** 2: la cola de acciones sin conexión (`cola.ts`, ATLAS-ADR-0032). */
const VERSION = 2;

/** Acción que cambia datos, hecha sin conexión y pendiente de enviar (ATLAS-ADR-0032). */
export interface AccionEncolada {
  /** Llave de idempotencia de la acción: el servidor la reconoce si llega dos veces. */
  llave: string;
  /** Módulo dueño (`soporte`), para mostrar solo sus pendientes. */
  modulo: string;
  /** Texto para la persona («Solicitar el cambio del turno del 24/10/2026»). */
  descripcion: string;
  ruta: string;
  metodo: 'POST' | 'PUT' | 'DELETE';
  cuerpo: unknown;
  /** Recurso al que se refiere (p. ej. el id del turno o de la solicitud), para marcarlo «pendiente de enviar». */
  recurso?: string;
  creadaEn: number;
  intentos: number;
  /** Mensaje del servidor si la rechazó (4xx): ya no se reintenta y la persona la descarta. */
  error?: string;
}

interface EsquemaAtlas extends DBSchema {
  recursos: {
    key: string;
    /** `permiso`: el que exige el recurso; al perderlo, se borra (HU 2.12). */
    value: { clave: string; datos: unknown; guardadoEn: number; venceEn: number; permiso?: string };
  };
  cola: {
    key: string;
    value: AccionEncolada;
  };
}

export interface Guardado<T> {
  datos: T;
  guardadoEn: Date;
}

const disponible = () => typeof indexedDB !== 'undefined';

/** Abre la base del dispositivo; la comparten la caché y la cola (una sola versión para las dos). */
export function abrir() {
  return openDB<EsquemaAtlas>(BASE, VERSION, {
    upgrade(base, anterior) {
      if (anterior < 1) base.createObjectStore('recursos', { keyPath: 'clave' });
      if (anterior < 2) base.createObjectStore('cola', { keyPath: 'llave' });
    },
  });
}

export const almacenDisponible = disponible;

export async function guardar(clave: string, datos: unknown, ttlHoras: number, ahora = Date.now(), permiso?: string): Promise<void> {
  if (!disponible()) return;
  const base = await abrir();
  await base.put('recursos', { clave, datos, guardadoEn: ahora, venceEn: ahora + ttlHoras * 3_600_000, permiso });
  base.close();
}

/** Borra los recursos guardados cuyo permiso la persona ya no tiene (§11: «al perder el permiso»). Devuelve cuántos. */
export async function depurarSinPermiso(permisos: readonly string[]): Promise<number> {
  if (!disponible()) return 0;
  const base = await abrir();
  try {
    const sinPermiso = (await base.getAll('recursos')).filter((r) => r.permiso !== undefined && !permisos.includes(r.permiso));
    await Promise.all(sinPermiso.map((r) => base.delete('recursos', r.clave)));
    return sinPermiso.length;
  } finally {
    base.close();
  }
}

/** El recurso guardado, o `null` si no existe o ya venció (en ese caso se borra). */
export async function leer<T>(clave: string, ahora = Date.now()): Promise<Guardado<T> | null> {
  if (!disponible()) return null;
  const base = await abrir();
  try {
    const registro = await base.get('recursos', clave);
    if (!registro) return null;
    if (registro.venceEn <= ahora) {
      await base.delete('recursos', clave);
      return null;
    }
    return { datos: registro.datos as T, guardadoEn: new Date(registro.guardadoEn) };
  } finally {
    base.close();
  }
}

/** Claves guardadas que empiezan por `prefijo` (p. ej. lo de los favoritos, HU 4.9). */
export async function clavesConPrefijo(prefijo: string): Promise<string[]> {
  if (!disponible()) return [];
  const base = await abrir();
  try {
    return await base.getAllKeys('recursos', IDBKeyRange.bound(prefijo, `${prefijo}￿`));
  } finally {
    base.close();
  }
}

export async function olvidar(clave: string): Promise<void> {
  if (!disponible()) return;
  const base = await abrir();
  await base.delete('recursos', clave);
  base.close();
}

/**
 * Sin sesión o con otra persona en el dispositivo (HU 2.12): borra IndexedDB (caché y acciones sin enviar de quien
 * estaba). A diferencia de `borrarDatosLocales`, deja la precarga del shell para poder abrir Atlas sin conexión.
 */
export async function borrarDatosDePersona(): Promise<void> {
  if (disponible()) await deleteDB(BASE);
}

/**
 * Cierre de sesión: borra IndexedDB (caché y acciones sin enviar: no deben quedar en un dispositivo sin sesión) y las
 * cachés del service worker de este origen.
 */
export async function borrarDatosLocales(): Promise<void> {
  if (disponible()) {
    await deleteDB(BASE);
  }
  if (typeof caches !== 'undefined') {
    const nombres = await caches.keys();
    await Promise.all(nombres.map((nombre) => caches.delete(nombre)));
  }
}
