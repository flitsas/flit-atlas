import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Equipo y responsables (prompt maestro §8.2). Desde la HU 2.2 trae el directorio interno sincronizado con Graph
 * (ATLAS-ADR-0030); los responsables llegan con la HU 2.3.
 */
export const moduloEquipo: ModuloAtlas = {
  id: 'equipo',
  nombre: 'Equipo y responsables',
  descripcion: 'Quién es responsable principal y secundario de cada producto y módulo, su carga, su historial y su traspaso.',
  icono: 'equipo',
  estado: 'disponible',
  rutas: [{ path: '/equipo', permiso: 'atlas.equipo.ver', cargar: () => import('./paginas/Equipo') }],
  navegacion: [{ etiqueta: 'Equipo', ruta: '/equipo', movil: 'mas', orden: 25 }],
  widgetsInicio: [
    {
      id: 'equipo-mis-responsabilidades',
      titulo: 'Mis responsabilidades',
      orden: 20,
      permiso: 'atlas.equipo.ver',
      cargar: () => import('./widgets/MisResponsabilidades'),
    },
  ],
  busqueda: { proveedor: 'equipo', tipos: ['persona', 'responsabilidad'] },
  offline: { recursos: ['/api/v1/equipo/responsabilidades'], ttlHoras: 24 },
  bandera: 'modulo.equipo',
  banderaPorDefecto: true,
};
