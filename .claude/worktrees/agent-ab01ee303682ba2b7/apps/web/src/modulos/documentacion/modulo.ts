import type { ModuloAtlas } from '../../app/modulos/contrato';

/** Documentación técnica y funcional viva (prompt maestro §8.5). Llega en F5. */
export const moduloDocumentacion: ModuloAtlas = {
  id: 'documentacion',
  nombre: 'Documentación',
  descripcion: 'Manuales, guías y documentos funcionales versionados, publicados desde los repos o escritos en Atlas.',
  icono: 'documentos',
  estado: 'proximamente',
  rutas: [{ path: '/documentacion', permiso: 'atlas.docs.ver', cargar: () => import('./paginas/Inicio') }],
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
  busqueda: { proveedor: 'documentacion', tipos: ['documento'] },
  bandera: 'modulo.documentacion',
  banderaPorDefecto: true,
};
