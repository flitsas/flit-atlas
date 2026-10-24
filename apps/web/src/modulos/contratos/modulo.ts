import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Contratos de integración (prompt maestro §8.4, ATLAS-ADR-0035). Desde la HU 3.2: la lista de contratos publicados
 * desde la CI y sus versiones. La pantalla completa del contrato (HU 3.6), el consolidado por ambiente (HU 3.4) y el
 * portal (F4) llegan después.
 */
export const moduloContratos: ModuloAtlas = {
  id: 'contratos',
  nombre: 'Contratos de integración',
  descripcion: 'OpenAPI, AsyncAPI y proto versionados desde los repos, con diff, guías, paquetes y exportación.',
  icono: 'contratos',
  estado: 'disponible',
  rutas: [
    { path: '/contratos', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Contratos') },
    // HU 3.7: enlace directo a una guía; la pestaña «Guías» de la pantalla del contrato (HU 3.6) monta el mismo componente.
    { path: '/contratos/:contratoId/guias/:guiaId', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Guia') },
  ],
  navegacion: [{ etiqueta: 'Contratos', ruta: '/contratos', movil: 'mas', orden: 40 }],
  widgetsInicio: [
    {
      id: 'contratos-cambios',
      titulo: 'Contratos publicados',
      orden: 30,
      permiso: 'atlas.contratos.ver_internos',
      cargar: () => import('./widgets/Cambios'),
    },
  ],
  // El proveedor `contratos` del servidor (BusquedaContratos) entrega estos tipos.
  busqueda: { proveedor: 'contratos', tipos: ['contrato', 'operacion'] },
  bandera: 'modulo.contratos',
  banderaPorDefecto: true,
};
