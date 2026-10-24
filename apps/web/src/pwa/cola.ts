import { useSyncExternalStore } from 'react';

import { enviarJson, ErrorApi } from '../app/api';
import { abrir, type AccionEncolada, almacenDisponible } from './almacen';

/**
 * Cola de acciones hechas sin conexión (prompt maestro §11, ATLAS-ADR-0032). Una acción que cambia datos (p. ej.
 * solicitar un cambio de turno) lleva una llave de idempotencia; si no hay red se guarda en IndexedDB y se envía sola al
 * reconectar (evento `online`) o cuando la persona pulsa «Reintentar». El servidor reconoce la llave, así que reenviar
 * nunca duplica. Si el servidor la rechaza (4xx), queda con su error para que la persona la descarte.
 *
 * Sin IndexedDB (pruebas o navegadores que no lo dan) la cola vive en memoria mientras la pestaña esté abierta. Se borra
 * con el resto de datos locales al cerrar sesión (`borrarDatosLocales`).
 */
export type { AccionEncolada } from './almacen';

export type NuevaAccion = Omit<AccionEncolada, 'creadaEn' | 'intentos' | 'error'>;

export type ResultadoAccion<T> = { estado: 'enviada'; respuesta: T } | { estado: 'encolada'; accion: AccionEncolada };

const memoria = new Map<string, AccionEncolada>();
const oyentes = new Set<() => void>();
let instantanea: readonly AccionEncolada[] = [];
let escuchando = false;
let enviando: Promise<ResumenEnvio> | null = null;

export interface ResumenEnvio {
  enviadas: number;
  rechazadas: number;
  pendientes: number;
}

/** Llave de idempotencia nueva (uuid v4 del navegador). */
export const nuevaLlave = (): string => crypto.randomUUID();

async function todas(): Promise<AccionEncolada[]> {
  if (!almacenDisponible()) return [...memoria.values()];
  const base = await abrir();
  try {
    return await base.getAll('cola');
  } finally {
    base.close();
  }
}

async function poner(accion: AccionEncolada): Promise<void> {
  if (!almacenDisponible()) {
    memoria.set(accion.llave, accion);
    return;
  }
  const base = await abrir();
  try {
    await base.put('cola', accion);
  } finally {
    base.close();
  }
}

async function quitar(llave: string): Promise<void> {
  if (!almacenDisponible()) {
    memoria.delete(llave);
    return;
  }
  const base = await abrir();
  try {
    await base.delete('cola', llave);
  } finally {
    base.close();
  }
}

async function refrescar(): Promise<void> {
  instantanea = (await todas()).sort((a, b) => a.creadaEn - b.creadaEn);
  oyentes.forEach((avisar) => avisar());
}

/** Sin red `fetch` falla con `TypeError`; una respuesta del servidor (aunque sea error) no es «sin conexión». */
const esFallaDeRed = (error: unknown) => !(error instanceof ErrorApi);

/** El servidor la rechazó por algo que reintentar no arregla (4xx, salvo 408 y 429). */
const esRechazo = (error: unknown) => error instanceof ErrorApi && error.estado >= 400 && error.estado < 500 && ![408, 429].includes(error.estado);

async function cuerpoDe<T>(respuesta: Response): Promise<T> {
  const texto = await respuesta.text();
  return (texto ? JSON.parse(texto) : undefined) as T;
}

function asegurarEscucha() {
  if (escuchando || typeof window === 'undefined') return;
  escuchando = true;
  window.addEventListener('online', () => void enviarPendientes());
}

/**
 * Envía la acción si hay red; si no hay (o la red falla en el camino), la encola. Un error del servidor se lanza como
 * siempre (`ErrorApi`) para que la pantalla lo muestre.
 */
export async function ejecutarOEncolar<T>(accion: NuevaAccion): Promise<ResultadoAccion<T>> {
  asegurarEscucha();
  const encolar = async (): Promise<ResultadoAccion<T>> => {
    const guardada: AccionEncolada = { ...accion, creadaEn: Date.now(), intentos: 0 };
    await poner(guardada);
    await refrescar();
    return { estado: 'encolada', accion: guardada };
  };
  if (typeof navigator !== 'undefined' && !navigator.onLine) return encolar();
  try {
    return { estado: 'enviada', respuesta: await cuerpoDe<T>(await enviarJson(accion.ruta, accion.metodo, accion.cuerpo)) };
  } catch (error) {
    if (esFallaDeRed(error)) return encolar();
    throw error;
  }
}

/**
 * Envía en orden las acciones pendientes (sin error). Se detiene en la primera falla de red: las demás esperan a la
 * próxima conexión. Una sola ejecución a la vez.
 */
export function enviarPendientes(): Promise<ResumenEnvio> {
  enviando ??= (async () => {
    const resumen: ResumenEnvio = { enviadas: 0, rechazadas: 0, pendientes: 0 };
    try {
      for (const accion of (await todas()).sort((a, b) => a.creadaEn - b.creadaEn)) {
        if (accion.error) continue;
        try {
          await enviarJson(accion.ruta, accion.metodo, accion.cuerpo);
          await quitar(accion.llave);
          resumen.enviadas++;
        } catch (error) {
          if (esRechazo(error)) {
            await poner({ ...accion, intentos: accion.intentos + 1, error: (error as ErrorApi).mensajePara('El servidor rechazó la acción.') });
            resumen.rechazadas++;
            continue;
          }
          await poner({ ...accion, intentos: accion.intentos + 1 });
          break;
        }
      }
      resumen.pendientes = (await todas()).filter((a) => !a.error).length;
      return resumen;
    } finally {
      enviando = null;
      await refrescar();
    }
  })();
  return enviando;
}

/** Quita una acción de la cola sin enviarla (p. ej. una que el servidor rechazó). */
export async function descartar(llave: string): Promise<void> {
  await quitar(llave);
  await refrescar();
}

function suscribir(avisar: () => void) {
  asegurarEscucha();
  oyentes.add(avisar);
  void refrescar();
  return () => {
    oyentes.delete(avisar);
  };
}

/** Acciones en la cola (todas las de este dispositivo); el módulo filtra las suyas. */
export function useCola(): readonly AccionEncolada[] {
  return useSyncExternalStore(suscribir, () => instantanea, () => instantanea);
}

/** Solo para pruebas: vacía la cola en memoria. */
export async function vaciarCola(): Promise<void> {
  memoria.clear();
  await refrescar();
}
