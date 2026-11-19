import type { ModuloAtlas } from '../../app/modulos/contrato';

/** Soporte de los sábados (prompt maestro §8.3). Llega en F2; va en la barra inferior del móvil. */
export const moduloSoporte: ModuloAtlas = {
  id: 'soporte',
  nombre: 'Soporte de los sábados',
  descripcion: 'Calendario de la rotación del soporte TI, con turnos, cambios, compensaciones y recordatorios.',
  icono: 'calendario',
  estado: 'proximamente',
  rutas: [{ path: '/soporte', permiso: 'atlas.soporte.ver', cargar: () => import('./paginas/Inicio') }],
  navegacion: [{ etiqueta: 'Soporte', ruta: '/soporte', movil: 'barra_inferior', orden: 30 }],
  widgetsInicio: [
    {
      id: 'soporte-este-sabado',
      titulo: 'Soporte de este sábado',
      orden: 10,
      permiso: 'atlas.soporte.ver',
      cargar: () => import('./widgets/EsteSabado'),
    },
  ],
  busqueda: { proveedor: 'soporte', tipos: ['turno', 'persona'] },
  offline: { recursos: ['/api/v1/soporte/turnos?desde=hoy&meses=3'], ttlHoras: 24 },
  bandera: 'modulo.soporte',
  banderaPorDefecto: true,
};
