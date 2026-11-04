import type { ModuloAtlas } from '../../app/modulos/contrato';
import { RECURSOS_SIN_CONEXION, TTL_HORAS } from './recursos';

/**
 * Catálogo de aplicativos (prompt maestro §8.1, HU 2.1, ATLAS-ADR-0028): listado con búsqueda y filtros y la ficha de
 * cada aplicativo, con las pestañas que aportan los demás módulos (`pestanasFicha` en su manifiesto).
 */
export const moduloCatalogo: ModuloAtlas = {
  id: 'catalogo',
  nombre: 'Catálogo de aplicativos',
  descripcion: 'Inventario único de productos, módulos y servicios de FLIT, con su estado, repo, criticidad y responsables.',
  icono: 'catalogo',
  estado: 'disponible',
  // Una sola ruta: el listado (/catalogo) y la ficha (/catalogo/<slug>) se reparten dentro de la página.
  rutas: [{ path: '/catalogo', permiso: 'atlas.catalogo.ver', cargar: () => import('./paginas/Catalogo') }],
  navegacion: [{ etiqueta: 'Catálogo', ruta: '/catalogo', movil: 'mas', orden: 20 }],
  widgetsInicio: [
    {
      id: 'catalogo-aplicativos',
      titulo: 'Aplicativos de FLIT',
      orden: 40,
      permiso: 'atlas.catalogo.ver',
      cargar: () => import('./widgets/Aplicativos'),
    },
  ],
  // El proveedor `catalogo` del servidor (BusquedaCatalogo) entrega estos tipos.
  busqueda: { proveedor: 'catalogo', tipos: ['producto', 'modulo', 'servicio'] },
  // HU 2.12: el listado completo, sin conexión (ATLAS-ADR-0034).
  offline: { recursos: RECURSOS_SIN_CONEXION, ttlHoras: TTL_HORAS },
  bandera: 'modulo.catalogo',
  banderaPorDefecto: true,
};
