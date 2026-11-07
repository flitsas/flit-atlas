import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Diagramas hechos a mano en Atlas (prompt maestro §8.6, ATLAS-ADR-0054). Leer pide `atlas.docs.ver`, igual que los documentos
 * donde se incrustan. Las rutas de edición también: el permiso de editar puede venir con alcance a un aplicativo (rol implícito
 * del responsable), así que lo decide la API (`puedeEditar`), que además responde 403.
 */
export const moduloDiagramas: ModuloAtlas = {
  id: 'diagramas',
  nombre: 'Diagramas',
  descripcion: 'Lienzos, flujos en Mermaid y procesos BPMN con versiones, comentarios y referencias al catálogo.',
  icono: 'diagramas',
  estado: 'disponible',
  rutas: [
    { path: '/diagramas', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Inicio') },
    { path: '/diagramas/nuevo', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Nuevo') },
    { path: '/diagramas/:id', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Detalle') },
    { path: '/diagramas/:id/editar', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Editar') },
  ],
  // Un externo no ve ningún diagrama (ATLAS-ADR-0054 §3): no se le ofrece una entrada vacía.
  navegacion: [{ etiqueta: 'Diagramas', ruta: '/diagramas', movil: 'mas', orden: 55, audiencia: 'empleados' }],
  pestanasFicha: [{ id: 'diagramas', titulo: 'Diagramas', orden: 40, permiso: 'atlas.docs.ver', cargar: () => import('./pestanas/Diagramas') }],
  busqueda: { proveedor: 'diagramas', tipos: ['diagrama'] },
  bandera: 'modulo.diagramas',
  banderaPorDefecto: true,
};
