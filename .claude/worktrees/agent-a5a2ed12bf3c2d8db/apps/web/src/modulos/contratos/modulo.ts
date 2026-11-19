import type { ModuloAtlas } from '../../app/modulos/contrato';

/** Contratos de integración (prompt maestro §8.4). Llega en F3 (internos y legado ICT) y F4 (portal). */
export const moduloContratos: ModuloAtlas = {
  id: 'contratos',
  nombre: 'Contratos de integración',
  descripcion: 'OpenAPI, AsyncAPI y proto versionados desde los repos, con diff, guías, paquetes y exportación.',
  icono: 'contratos',
  estado: 'proximamente',
  rutas: [{ path: '/contratos', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Inicio') }],
  navegacion: [{ etiqueta: 'Contratos', ruta: '/contratos', movil: 'mas', orden: 40 }],
  widgetsInicio: [
    {
      id: 'contratos-cambios',
      titulo: 'Contratos con cambios',
      orden: 30,
      permiso: 'atlas.contratos.ver_internos',
      cargar: () => import('./widgets/Cambios'),
    },
  ],
  busqueda: { proveedor: 'contratos', tipos: ['contrato', 'operacion'] },
  bandera: 'modulo.contratos',
  banderaPorDefecto: true,
};
