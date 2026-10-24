import { pedir } from '../app/api';
import type { ModuloAtlas } from '../app/modulos/contrato';
import { type AccesoUsuario, banderaActiva, tienePermiso } from '../app/modulos/resolver';
import { depurarSinPermiso, leer } from './almacen';
import { guardarRecurso } from './sinConexion';

/**
 * Sincronización de los datos sin conexión (HU 2.12, ATLAS-ADR-0034). La carga el shell con `import()` cuando hay
 * sesión y red, así que no pesa en el bundle inicial. Primero borra lo guardado cuyo permiso la persona ya no tiene;
 * después guarda, de cada módulo activo, los recursos de su manifiesto (`offline.recursos`) para los que tiene permiso,
 * salvo los guardados hace menos de {@link FRESCURA_MS}. Lo que se guarda es lo que la API le devuelve a ella, sin
 * teléfonos. Un error de red o del servidor deja lo que había.
 */
export const FRESCURA_MS = 15 * 60_000;

export async function sincronizarSinConexion(
  modulos: readonly ModuloAtlas[],
  acceso: AccesoUsuario,
  ahora = Date.now(),
): Promise<{ guardados: number; borrados: number }> {
  const borrados = acceso.permisos ? await depurarSinPermiso(acceso.permisos).catch(() => 0) : 0;
  let guardados = 0;
  const pendientes = modulos
    .filter((m) => m.offline && banderaActiva(m, acceso.banderas))
    .flatMap((m) => m.offline!.recursos.map((r) => ({ ...r, ttlHoras: m.offline!.ttlHoras })))
    .filter((r) => tienePermiso(r.permiso, acceso.permisos));
  for (const recurso of pendientes) {
    try {
      const previo = await leer(recurso.clave, ahora);
      if (previo && ahora - previo.guardadoEn.getTime() < FRESCURA_MS) continue;
      const datos: unknown = await (await pedir(recurso.ruta())).json();
      await guardarRecurso(recurso, datos);
      guardados++;
    } catch {
      // Sin red o un 4xx/5xx: queda lo último guardado (si lo hay) hasta la próxima vez.
    }
  }
  return { guardados, borrados };
}
