import type { ModuloAtlas } from '../../app/modulos/contrato';

/** Catálogo de aplicativos (prompt maestro §8.1). Llega en F2; mientras tanto, página «Próximamente». */
export const moduloCatalogo: ModuloAtlas = {
  id: 'catalogo',
  nombre: 'Catálogo de aplicativos',
  descripcion: 'Inventario único de productos, módulos y servicios de FLIT, con su estado, repo, criticidad y responsables.',
  icono: 'catalogo',
  estado: 'proximamente',
  rutas: [{ path: '/catalogo', permiso: 'atlas.catalogo.ver', cargar: () => import('./paginas/Inicio') }],
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
  busqueda: { proveedor: 'catalogo', tipos: ['aplicativo', 'modulo', 'servicio'] },
  offline: { recursos: ['/api/v1/catalogo/aplicativos'], ttlHoras: 24 },
  bandera: 'modulo.catalogo',
  banderaPorDefecto: true,
};
