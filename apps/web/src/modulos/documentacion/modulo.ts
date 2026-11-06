import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Documentación técnica y funcional viva (prompt maestro §8.5, ATLAS-ADR-0053). Las rutas de edición piden solo
 * `atlas.docs.ver`: el permiso de editar puede venir con alcance a un aplicativo (rol implícito del responsable), así que lo
 * decide la API (`puedeEditar`), que además responde 403.
 */
export const moduloDocumentacion: ModuloAtlas = {
  id: 'documentacion',
  nombre: 'Documentación',
  descripcion: 'Manuales, guías y documentos funcionales versionados, publicados desde los repos o escritos en Atlas.',
  icono: 'documentos',
  estado: 'disponible',
  rutas: [
    { path: '/documentacion', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Inicio') },
    { path: '/documentacion/nuevo', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Nuevo') },
    { path: '/documentacion/:id', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Lector') },
    { path: '/documentacion/:id/comparar', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Comparar') },
    { path: '/documentacion/:id/editar', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Editor') },
  ],
  navegacion: [{ etiqueta: 'Documentación', ruta: '/documentacion', movil: 'mas', orden: 50 }],
  widgetsInicio: [
    {
      id: 'documentacion-recientes',
      titulo: 'Documentos recientes',
      orden: 35,
      permiso: 'atlas.docs.ver',
      cargar: () => import('./widgets/Recientes'),
    },
  ],
  pestanasFicha: [{ id: 'documentos', titulo: 'Documentos', orden: 30, permiso: 'atlas.docs.ver', cargar: () => import('./pestanas/Documentos') }],
  busqueda: { proveedor: 'documentacion', tipos: ['documento'] },
  bandera: 'modulo.documentacion',
  banderaPorDefecto: true,
};
