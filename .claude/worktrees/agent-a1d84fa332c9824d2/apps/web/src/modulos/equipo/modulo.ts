import type { ModuloAtlas } from '../../app/modulos/contrato';

/** Equipo y responsables (prompt maestro §8.2). Llega en F2. */
export const moduloEquipo: ModuloAtlas = {
  id: 'equipo',
  nombre: 'Equipo y responsables',
  descripcion: 'Quién es responsable principal y secundario de cada producto y módulo, su carga, su historial y su traspaso.',
  icono: 'equipo',
  estado: 'proximamente',
  rutas: [{ path: '/equipo', permiso: 'atlas.equipo.ver', cargar: () => import('./paginas/Inicio') }],
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
