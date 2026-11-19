import { type DBSchema, deleteDB, openDB } from 'idb';

/**
 * Caché sin conexión en IndexedDB (prompt maestro §11): cada recurso con su TTL. Solo se guarda lo que los permisos
 * de la persona permiten, y todo se borra al cerrar sesión (`borrarDatosLocales`).
 */
const BASE = 'atlas';
const VERSION = 1;

interface EsquemaAtlas extends DBSchema {
  recursos: {
    key: string;
    value: { clave: string; datos: unknown; guardadoEn: number; venceEn: number };
  };
}

export interface Guardado<T> {
  datos: T;
  guardadoEn: Date;
}

const disponible = () => typeof indexedDB !== 'undefined';

function abrir() {
  return openDB<EsquemaAtlas>(BASE, VERSION, {
    upgrade(base) {
      base.createObjectStore('recursos', { keyPath: 'clave' });
    },
  });
}

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

/** Cierre de sesión: borra IndexedDB y las cachés del service worker de este origen. */
export async function borrarDatosLocales(): Promise<void> {
  if (disponible()) {
    await deleteDB(BASE);
  }
  if (typeof caches !== 'undefined') {
    const nombres = await caches.keys();
    await Promise.all(nombres.map((nombre) => caches.delete(nombre)));
  }
}
