import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Contratos de integración (prompt maestro §8.4, ATLAS-ADR-0035). Desde la HU 3.2: la lista de contratos publicados
 * desde la CI y sus versiones; desde la HU 3.4, el consolidado por ambiente (versión en dev, qa y pdn, desfases y
 * breaking pendientes). La pantalla completa del contrato (HU 3.6) y el portal (F4) llegan después.
 */
export const moduloContratos: ModuloAtlas = {
  id: 'contratos',
  nombre: 'Contratos de integración',
  descripcion: 'OpenAPI, AsyncAPI y proto versionados desde los repos, con diff, guías, paquetes y exportación.',
  icono: 'contratos',
  estado: 'disponible',
  rutas: [
    { path: '/contratos', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Contratos') },
    { path: '/contratos/consolidado', permiso: 'atlas.contratos.ver_internos', cargar: () => import('./paginas/Consolidado') },
  ],
  navegacion: [
    { etiqueta: 'Contratos', ruta: '/contratos', movil: 'mas', orden: 40 },
    { etiqueta: 'Consolidado', ruta: '/contratos/consolidado', movil: 'mas', orden: 41 },
  ],
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
