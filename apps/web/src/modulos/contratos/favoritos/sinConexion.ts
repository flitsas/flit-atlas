import { ErrorApi, pedir } from '../../../app/api';
import type { AccesoSincronizacion } from '../../../app/modulos/contrato';
import { clavesConPrefijo, leer, olvidar } from '../../../pwa/almacen';
import { bytes, esFallaDeRed, guardarRecurso, type RecursoGuardado } from '../../../pwa/sinConexion';

/**
 * Favoritos sin conexión (HU 4.9, prompt maestro §11, ATLAS-ADR-0034). Cada favorito guarda en IndexedDB las respuestas
 * de la API que su pantalla necesita, tal como el servidor se las dio a la persona (ya recortadas a su organización, su
 * alcance y su rol: en el portal, el spec **podado**), con el TTL de 72 horas y el permiso que exige cada una:
 *
 * - Contrato interno: el contrato, sus versiones, el spec y el índice de operaciones de la última versión.
 * - Paquete compartido: el detalle del paquete y el spec podado (y los cambios) de cada contrato con detalle técnico.
 * - Contrato compartido: el detalle del paquete, su spec podado y sus cambios.
 * - Guía compartida: el detalle del paquete y la guía.
 *
 * Se borra al cerrar sesión (la base entera), al quitar el favorito (lo que ningún otro favorito usa), al perder el permiso
 * (`depurarSinPermiso` del shell) y cuando, al resincronizar, el servidor ya no lo devuelve o responde 403/404. Topes:
 * {@link TOPE_RECURSO_BYTES} por respuesta y {@link TOPE_TOTAL_BYTES} en total; lo que no cabe queda solo en línea.
 */
export const TTL_HORAS = 72;
export const PREFIJO = 'contratos.fav:';
export const TOPE_RECURSO_BYTES = 5 * 1024 * 1024;
export const TOPE_TOTAL_BYTES = 30 * 1024 * 1024;
/** Lo guardado hace menos de esto no se vuelve a pedir al sincronizar (como los recursos de los módulos). */
export const FRESCURA_MS = 15 * 60_000;

export const PERMISO_PORTAL = 'atlas.portal.ver';
export const PERMISO_INTERNO = 'atlas.contratos.ver_internos';

export const RUTA_FAVORITOS_PORTAL = '/api/v1/portal/favoritos';
export const RUTA_FAVORITOS_INTERNOS = '/api/v1/contratos/favoritos';

export type TipoFavoritoPortal = 'paquete' | 'contrato' | 'guia';

export interface FavoritoPortal {
  id: string;
  tipo: TipoFavoritoPortal;
  paqueteId: string;
  contratoId: string | null;
  guiaId: string | null;
  titulo: string;
  detalle: string | null;
  ruta: string;
  marcadoEn: string;
}

export interface FavoritoInterno {
  id: string;
  contratoId: string;
  codigo: string;
  nombre: string;
  tipo: string;
  aplicativo: { id: string; slug: string | null; nombre: string | null };
  version: string | null;
  ruta: string;
  marcadoEn: string;
}

/** Lo que se marca: algo compartido en el portal o un contrato interno. */
export type ObjetivoFavorito =
  | { ambito: 'portal'; tipo: TipoFavoritoPortal; paqueteId: string; contratoId?: string; guiaId?: string }
  | { ambito: 'interno'; contratoId: string };

/** El recurso guardado de una ruta de la API, con su permiso. */
export function recurso(ruta: string, permiso: string): RecursoGuardado {
  return { clave: `${PREFIJO}${ruta}`, permiso, ttlHoras: TTL_HORAS };
}

const enc = encodeURIComponent;
export const rutaPaquete = (paqueteId: string) => `/api/v1/portal/paquetes/${enc(paqueteId)}`;
export const rutaSpecPodado = (paqueteId: string, contratoId: string) => `${rutaPaquete(paqueteId)}/contratos/${enc(contratoId)}/spec`;
export const rutaCambios = (paqueteId: string, contratoId: string) => `${rutaPaquete(paqueteId)}/contratos/${enc(contratoId)}/cambios`;
export const rutaGuia = (paqueteId: string, guiaId: string) => `${rutaPaquete(paqueteId)}/guias/${enc(guiaId)}`;
export const rutaContrato = (contratoId: string) => `/api/v1/contratos/${enc(contratoId)}`;
export const rutaVersiones = (contratoId: string) => `${rutaContrato(contratoId)}/versiones`;
export const rutaSpec = (contratoId: string, version: string) => `${rutaVersiones(contratoId)}/${enc(version)}/spec`;
export const rutaOperaciones = (contratoId: string, version: string) => `${rutaVersiones(contratoId)}/${enc(version)}/operaciones`;

/** La ruta de la API que identifica al objetivo (la que muestra «disponible sin conexión»). */
export function rutaPrincipal(objetivo: ObjetivoFavorito): string {
  if (objetivo.ambito === 'interno') return rutaContrato(objetivo.contratoId);
  if (objetivo.tipo === 'contrato') return rutaSpecPodado(objetivo.paqueteId, objetivo.contratoId!);
  if (objetivo.tipo === 'guia') return rutaGuia(objetivo.paqueteId, objetivo.guiaId!);
  return rutaPaquete(objetivo.paqueteId);
}

export function objetivoDePortal(f: FavoritoPortal): ObjetivoFavorito {
  return { ambito: 'portal', tipo: f.tipo, paqueteId: f.paqueteId, contratoId: f.contratoId ?? undefined, guiaId: f.guiaId ?? undefined };
}

export function mismoObjetivo(a: ObjetivoFavorito, b: ObjetivoFavorito): boolean {
  return rutaPrincipal(a) === rutaPrincipal(b);
}

interface DetalleMinimo {
  contratos: Array<{ contrato: { contratoId: string; tipo: string; nivel: string; estadoVersion: string } }>;
}

type Resultado = 'guardado' | 'grande' | 'sin_acceso' | 'sin_red' | 'error';

interface Cuenta {
  total: number;
  guardados: number;
}

/** Pide una ruta y la guarda con su permiso si cabe; lo guardado hace poco se reutiliza sin pedirlo. */
async function guardarRuta(ruta: string, permiso: string, como: 'json' | 'texto', cuenta: Cuenta, ahora: number): Promise<{ resultado: Resultado; datos?: unknown }> {
  const previo = await leer(recurso(ruta, permiso).clave, ahora).catch(() => null);
  if (previo && ahora - previo.guardadoEn.getTime() < FRESCURA_MS) {
    cuenta.total += bytes(previo.datos);
    return { resultado: 'guardado', datos: previo.datos };
  }
  let datos: unknown;
  try {
    const respuesta = await pedir(ruta);
    datos = como === 'texto' ? await respuesta.text() : await respuesta.json();
  } catch (error) {
    if (error instanceof ErrorApi && (error.estado === 403 || error.estado === 404)) return { resultado: 'sin_acceso' };
    return { resultado: esFallaDeRed(error) ? 'sin_red' : 'error' };
  }
  const tamano = bytes(datos);
  if (tamano > TOPE_RECURSO_BYTES || cuenta.total + tamano > TOPE_TOTAL_BYTES) return { resultado: 'grande', datos };
  await guardarRecurso(recurso(ruta, permiso), datos);
  cuenta.total += tamano;
  cuenta.guardados++;
  return { resultado: 'guardado', datos };
}

/**
 * Guarda en el dispositivo lo que muestra un favorito y devuelve sus rutas guardadas (las que siguen siendo suyas). Si el
 * servidor dice que ya no lo ve (403/404), borra lo que tuviera.
 */
export async function guardarFavorito(
  objetivo: ObjetivoFavorito,
  cuenta: Cuenta = { total: 0, guardados: 0 },
  ahora = Date.now(),
  version?: string | null,
): Promise<{ rutas: string[]; resultado: Resultado }> {
  const permiso = objetivo.ambito === 'portal' ? PERMISO_PORTAL : PERMISO_INTERNO;
  const piezas: Array<[string, 'json' | 'texto']> = [];
  let primera: Awaited<ReturnType<typeof guardarRuta>>;
  if (objetivo.ambito === 'interno') {
    primera = await guardarRuta(rutaContrato(objetivo.contratoId), permiso, 'json', cuenta, ahora);
    const ultima = version ?? (primera.datos as { ultimaVersion?: { version?: string } | null } | undefined)?.ultimaVersion?.version;
    piezas.push([rutaVersiones(objetivo.contratoId), 'json']);
    if (ultima) piezas.push([rutaSpec(objetivo.contratoId, ultima), 'texto'], [rutaOperaciones(objetivo.contratoId, ultima), 'json']);
  } else {
    primera = await guardarRuta(rutaPaquete(objetivo.paqueteId), permiso, 'json', cuenta, ahora);
    const contratos = ((primera.datos as DetalleMinimo | undefined)?.contratos ?? [])
      .map((c) => c.contrato)
      .filter((c) => c.nivel === 'tecnico' && c.estadoVersion !== 'retirada');
    const deUno = (contratoId: string) => {
      const c = contratos.find((x) => x.contratoId === contratoId);
      if (!c) return;
      piezas.push([rutaSpecPodado(objetivo.paqueteId, contratoId), 'texto']);
      if (c.tipo === 'openapi') piezas.push([rutaCambios(objetivo.paqueteId, contratoId), 'json']);
    };
    if (objetivo.tipo === 'paquete') contratos.forEach((c) => deUno(c.contratoId));
    else if (objetivo.tipo === 'contrato') deUno(objetivo.contratoId!);
    else piezas.push([rutaGuia(objetivo.paqueteId, objetivo.guiaId!), 'json']);
  }
  const principal = objetivo.ambito === 'interno' ? rutaContrato(objetivo.contratoId) : rutaPaquete(objetivo.paqueteId);
  if (primera.resultado === 'sin_acceso') {
    await olvidarRutas([principal, ...piezas.map(([r]) => r)]);
    return { rutas: [], resultado: 'sin_acceso' };
  }
  if (primera.resultado !== 'guardado') return { rutas: primera.resultado === 'sin_red' ? [principal] : [], resultado: primera.resultado };
  const rutas = [principal];
  let resultado: Resultado = 'guardado';
  for (const [ruta, como] of piezas) {
    const { resultado: r } = await guardarRuta(ruta, permiso, como, cuenta, ahora);
    if (r === 'guardado' || r === 'sin_red') rutas.push(ruta);
    if (r === 'sin_acceso') {
      await olvidar(recurso(ruta, permiso).clave).catch(() => undefined);
      // Si lo que se perdió es lo que identifica al favorito (su spec o su guía), el favorito ya no se ve.
      if (ruta === rutaPrincipal(objetivo)) resultado = 'sin_acceso';
    }
    if (r === 'grande' && resultado === 'guardado') resultado = 'grande';
  }
  return { rutas, resultado };
}

async function olvidarRutas(rutas: readonly string[]): Promise<number> {
  const claves = new Set(rutas.map((r) => `${PREFIJO}${r}`));
  const guardadas = (await clavesConPrefijo(PREFIJO).catch(() => [] as string[])).filter((c) => claves.has(c));
  await Promise.all(guardadas.map((c) => olvidar(c).catch(() => undefined)));
  return guardadas.length;
}

/** Lista de favoritos del servidor; `null` sin red o con un error (no se toca nada de lo guardado). */
async function listar<T>(ruta: string, permiso: string): Promise<T[] | null> {
  try {
    const lista = (await (await pedir(ruta)).json()) as T[];
    await guardarRecurso(recurso(ruta, permiso), lista);
    return lista;
  } catch {
    return null;
  }
}

/**
 * Sincronización de los favoritos (la llama el shell con red y sesión, HU 2.12 y 4.9): pide la lista de cada ámbito que la
 * persona puede ver, guarda lo de cada favorito (del más reciente al más antiguo, hasta el tope total) y borra lo guardado
 * que ya no es de ningún favorito. Sin red no borra nada.
 */
export async function sincronizar(acceso: AccesoSincronizacion, ahora = Date.now()): Promise<{ guardados: number; borrados: number }> {
  const permisos = acceso.permisos ?? [];
  const cuenta: Cuenta = { total: 0, guardados: 0 };
  const vigentes = new Set<string>();
  let completo = true;
  if (permisos.includes(PERMISO_PORTAL)) {
    const lista = await listar<FavoritoPortal>(RUTA_FAVORITOS_PORTAL, PERMISO_PORTAL);
    if (lista === null) completo = false;
    else {
      vigentes.add(RUTA_FAVORITOS_PORTAL);
      for (const f of lista) (await guardarFavorito(objetivoDePortal(f), cuenta, ahora)).rutas.forEach((r) => vigentes.add(r));
    }
  }
  if (permisos.includes(PERMISO_INTERNO)) {
    const lista = await listar<FavoritoInterno>(RUTA_FAVORITOS_INTERNOS, PERMISO_INTERNO);
    if (lista === null) completo = false;
    else {
      vigentes.add(RUTA_FAVORITOS_INTERNOS);
      for (const f of lista) {
        (await guardarFavorito({ ambito: 'interno', contratoId: f.contratoId }, cuenta, ahora, f.version)).rutas.forEach((r) => vigentes.add(r));
      }
    }
  }
  if (!completo) return { guardados: cuenta.guardados, borrados: 0 };
  // Lo que ya no es de ningún favorito (lo quitó en otro dispositivo, perdió el acceso o el permiso) se borra.
  const sobrantes = (await clavesConPrefijo(PREFIJO).catch(() => [] as string[])).filter((c) => !vigentes.has(c.slice(PREFIJO.length)));
  await Promise.all(sobrantes.map((c) => olvidar(c).catch(() => undefined)));
  return { guardados: cuenta.guardados, borrados: sobrantes.length };
}

/** Si una ruta guardada es de lo que muestra un favorito (para no borrar lo que otro favorito usa). */
export function pertenece(ruta: string, o: ObjetivoFavorito): boolean {
  if (o.ambito === 'interno') return ruta === rutaContrato(o.contratoId) || ruta.startsWith(`${rutaContrato(o.contratoId)}/`);
  if (ruta === rutaPaquete(o.paqueteId)) return true;
  if (o.tipo === 'paquete') return ruta.startsWith(`${rutaPaquete(o.paqueteId)}/contratos/`);
  if (o.tipo === 'contrato') return ruta.startsWith(`${rutaPaquete(o.paqueteId)}/contratos/${enc(o.contratoId!)}/`);
  return ruta === rutaGuia(o.paqueteId, o.guiaId!);
}

/**
 * Al quitar un favorito: borra lo suyo que ningún otro favorito usa (p. ej. el detalle del paquete se queda si otro
 * contrato del mismo paquete sigue marcado). `restantes` son los favoritos que quedan.
 */
export async function olvidarFavorito(objetivo: ObjetivoFavorito, restantes: readonly ObjetivoFavorito[]): Promise<number> {
  const guardadas = (await clavesConPrefijo(PREFIJO).catch(() => [] as string[])).map((c) => c.slice(PREFIJO.length));
  const propias = guardadas.filter((ruta) => pertenece(ruta, objetivo) && !restantes.some((o) => pertenece(ruta, o)));
  await Promise.all(propias.map((ruta) => olvidar(`${PREFIJO}${ruta}`).catch(() => undefined)));
  return propias.length;
}

/** Cuándo se guardó en este dispositivo lo principal de un objetivo, o `null` si no está guardado. */
export async function guardadoEn(objetivo: ObjetivoFavorito): Promise<Date | null> {
  const permiso = objetivo.ambito === 'portal' ? PERMISO_PORTAL : PERMISO_INTERNO;
  return (await leer(recurso(rutaPrincipal(objetivo), permiso).clave).catch(() => null))?.guardadoEn ?? null;
}
