import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Soporte de los sábados (prompt maestro §8.3, HU 2.6 a 2.9, ATLAS-ADR-0029 y ATLAS-ADR-0032): calendario en lista, semana
 * y mes, widget «Soporte de este sábado» por equipo, la gestión (configuración, equipos, participantes, ausencias,
 * festivos y generación), el importador del Excel, los cambios de turno (que se encolan sin conexión) y compensaciones, y
 * Microsoft 365 (HU 2.10, ATLAS-ADR-0033): estado del calendario de Outlook y de Teams, y los feeds .ics.
 * Va en la barra inferior del móvil. Un externo solo ve el widget del canal del sábado (`atlas.soporte.ver_canal`).
 */
export const moduloSoporte: ModuloAtlas = {
  id: 'soporte',
  nombre: 'Soporte de los sábados',
  descripcion: 'Calendario de la rotación del soporte TI: quién atiende cada sábado, su horario y cómo contactarlo.',
  icono: 'calendario',
  estado: 'disponible',
  rutas: [
    { path: '/soporte', permiso: 'atlas.soporte.ver', cargar: () => import('./paginas/Calendario') },
    { path: '/soporte/indicadores', permiso: 'atlas.soporte.ver', cargar: () => import('./paginas/Indicadores') },
    { path: '/soporte/gestion', permiso: 'atlas.soporte.gestionar', cargar: () => import('./paginas/Gestion') },
    // HU 2.7: importador del Excel con vista previa.
    { path: '/soporte/importar', permiso: 'atlas.soporte.gestionar', cargar: () => import('./paginas/Importar') },
    // HU 2.9: cambios de turno (funcionan sin conexión) y compensaciones.
    { path: '/soporte/cambios', permiso: 'atlas.soporte.ver', cargar: () => import('./paginas/Cambios') },
    // HU 2.10: estado del calendario compartido de Outlook y de los avisos de Teams, y los feeds .ics.
    { path: '/soporte/microsoft-365', permiso: 'atlas.soporte.ver', cargar: () => import('./paginas/Microsoft365') },
    // Lo único de soporte para un externo; con ella el módulo es visible y su widget del canal aparece en Inicio.
    { path: '/soporte/canal', permiso: 'atlas.soporte.ver_canal', cargar: () => import('./paginas/Canal') },
  ],
  navegacion: [
    { etiqueta: 'Soporte', ruta: '/soporte', movil: 'barra_inferior', orden: 30, permiso: 'atlas.soporte.ver' },
  ],
  widgetsInicio: [
    {
      id: 'soporte-este-sabado',
      titulo: 'Soporte de este sábado',
      orden: 10,
      permiso: 'atlas.soporte.ver',
      cargar: () => import('./widgets/EsteSabado'),
    },
    {
      id: 'soporte-canal',
      titulo: 'Soporte del sábado',
      orden: 11,
      permiso: 'atlas.soporte.ver_canal',
      cargar: () => import('./widgets/CanalSabado'),
    },
  ],
  busqueda: { proveedor: 'soporte', tipos: ['turno', 'persona'] },
  // HU 2.12: se guarda sin conexión (aún no lo usa el shell).
  offline: { recursos: ['/api/v1/soporte/turnos'], ttlHoras: 24 },
  bandera: 'modulo.soporte',
  banderaPorDefecto: true,
};
