import { SinAcceso, NoEncontrada } from '../paginas/Estados';
import { useSesion } from '../sesion';
import type { ModuloAtlas, RutaModulo } from './contrato';
import { diferido } from './diferidos';
import { banderaActiva, tienePermiso } from './resolver';

/**
 * Página de un módulo. Las rutas de todos los módulos registrados existen en el enrutador; aquí se decide si se
 * muestran: con la bandera apagada la ruta no existe (404) y sin el permiso se explica que no hay acceso. El servidor
 * aplica la misma regla en sus endpoints.
 */
export function RutaDeModulo({ modulo, ruta }: { modulo: ModuloAtlas; ruta: RutaModulo }) {
  const { acceso } = useSesion();
  if (!banderaActiva(modulo, acceso.banderas)) return <NoEncontrada />;
  if (!tienePermiso(ruta.permiso, acceso.permisos)) return <SinAcceso modulo={modulo.nombre} />;
  const Pagina = diferido(ruta.cargar);
  return <Pagina />;
}
