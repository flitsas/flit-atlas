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
    value: { clave: string; datos: unknown; guardadoEn: number; venceEn: number };
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

export async function guardar(clave: string, datos: unknown, ttlHoras: number, ahora = Date.now()): Promise<void> {
  if (!disponible()) return;
  const base = await abrir();
  await base.put('recursos', { clave, datos, guardadoEn: ahora, venceEn: ahora + ttlHoras * 3_600_000 });
  base.close();
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

export async function olvidar(clave: string): Promise<void> {
  if (!disponible()) return;
  const base = await abrir();
  await base.delete('recursos', clave);
  base.close();
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
