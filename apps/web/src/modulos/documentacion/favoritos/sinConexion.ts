import { ErrorApi, pedir } from '../../../app/api';
import type { AccesoSincronizacion } from '../../../app/modulos/contrato';
import { clavesConPrefijo, leer, olvidar } from '../../../pwa/almacen';
import { bytes, guardarRecurso, type RecursoGuardado } from '../../../pwa/sinConexion';

/**
 * Favoritos sin conexión (HU 5.4, prompt maestro §11, ATLAS-ADR-0034), como los de contratos: cada favorito guarda en IndexedDB
 * el documento con su versión vigente y la lista de versiones, tal como el servidor se los dio a la persona, con un TTL de 72
 * horas y el permiso `atlas.docs.ver`. Se borra al cerrar sesión (la base entera), al quitar el favorito, al perder el permiso
 * (`depurarSinPermiso` del shell) y cuando, al resincronizar, el servidor ya no lo devuelve o responde 403/404.
 */
export const TTL_HORAS = 72;
export const PREFIJO = 'documentacion.fav:';
export const PERMISO = 'atlas.docs.ver';
export const RUTA_FAVORITOS = '/api/v1/documentacion/favoritos';
export const TOPE_RECURSO_BYTES = 2 * 1024 * 1024;
export const TOPE_TOTAL_BYTES = 20 * 1024 * 1024;
/** Lo guardado hace menos de esto no se vuelve a pedir al sincronizar. */
export const FRESCURA_MS = 15 * 60_000;

export interface FavoritoDocumento {
  id: string;
  documentoId: string;
  titulo: string;
  tipo: string;
  versionVigente: number | null;
  ruta: string;
  marcadoEn: string;
}

export function recurso(ruta: string): RecursoGuardado {
  return { clave: `${PREFIJO}${ruta}`, permiso: PERMISO, ttlHoras: TTL_HORAS };
}

const enc = encodeURIComponent;
export const rutaDocumento = (id: string) => `/api/v1/documentacion/documentos/${enc(id)}`;
export const rutaVersiones = (id: string) => `${rutaDocumento(id)}/versiones`;

interface Cuenta {
  total: number;
  guardados: number;
}

/** Guarda en el dispositivo lo que muestra un favorito. Si el servidor dice que ya no lo ve (403/404), borra lo que tuviera. */
export async function guardarFavorito(id: string, cuenta: Cuenta = { total: 0, guardados: 0 }, ahora = Date.now()): Promise<'guardado' | 'sin_acceso' | 'otro'> {
  for (const ruta of [rutaDocumento(id), rutaVersiones(id)]) {
    const previo = await leer(recurso(ruta).clave, ahora).catch(() => null);
    if (previo && ahora - previo.guardadoEn.getTime() < FRESCURA_MS) {
      cuenta.total += bytes(previo.datos);
      continue;
    }
    let datos: unknown;
    try {
      datos = await (await pedir(ruta)).json();
    } catch (error) {
      if (error instanceof ErrorApi && (error.estado === 403 || error.estado === 404)) {
        await olvidarFavorito(id);
        return 'sin_acceso';
      }
      return 'otro';
    }
    const tamano = bytes(datos);
    if (tamano > TOPE_RECURSO_BYTES || cuenta.total + tamano > TOPE_TOTAL_BYTES) return 'otro';
    await guardarRecurso(recurso(ruta), datos);
    cuenta.total += tamano;
    cuenta.guardados++;
  }
  return 'guardado';
}

export async function olvidarFavorito(id: string): Promise<void> {
  await Promise.all([rutaDocumento(id), rutaVersiones(id)].map((ruta) => olvidar(recurso(ruta).clave).catch(() => undefined)));
}

/** Cuándo se guardó en este dispositivo, o `null` si no está guardado. */
export async function guardadoEn(id: string): Promise<Date | null> {
  return (await leer(recurso(rutaDocumento(id)).clave).catch(() => null))?.guardadoEn ?? null;
}

/**
 * La llama el shell con red y sesión: guarda lo de cada favorito (del más reciente al más antiguo, hasta el tope) y borra lo
 * que ya no es de ninguno. Sin red, o si la lista falla, no borra nada.
 */
export async function sincronizar(acceso: AccesoSincronizacion, ahora = Date.now()): Promise<{ guardados: number; borrados: number }> {
  if (!(acceso.permisos ?? []).includes(PERMISO)) return { guardados: 0, borrados: 0 };
  let lista: FavoritoDocumento[];
  try {
    lista = (await (await pedir(RUTA_FAVORITOS)).json()) as FavoritoDocumento[];
  } catch {
    return { guardados: 0, borrados: 0 };
  }
  await guardarRecurso(recurso(RUTA_FAVORITOS), lista);
  const cuenta: Cuenta = { total: 0, guardados: 0 };
  const vigentes = new Set<string>([RUTA_FAVORITOS]);
  for (const f of lista) {
    if ((await guardarFavorito(f.documentoId, cuenta, ahora)) !== 'sin_acceso') {
      vigentes.add(rutaDocumento(f.documentoId));
      vigentes.add(rutaVersiones(f.documentoId));
    }
  }
  const sobrantes = (await clavesConPrefijo(PREFIJO).catch(() => [] as string[])).filter((c) => !vigentes.has(c.slice(PREFIJO.length)));
  await Promise.all(sobrantes.map((c) => olvidar(c).catch(() => undefined)));
  return { guardados: cuenta.guardados, borrados: sobrantes.length };
}
