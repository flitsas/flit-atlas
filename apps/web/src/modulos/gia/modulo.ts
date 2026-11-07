import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Gobierno de IA (prompt maestro §8.8, ATLAS-ADR-0055). Viene apagado: encenderlo es decisión del líder, después de revisar
 * lo sembrado. Es solo de `atlas.gia.*`, así que un externo nunca lo ve.
 */
export const moduloGia: ModuloAtlas = {
  id: 'gia',
  nombre: 'Gobierno de IA',
  descripcion: 'Casos de uso de IA, evaluación de riesgo, políticas, herramientas del equipo e incidentes.',
  icono: 'permisos',
  estado: 'disponible',
  rutas: [
    { path: '/gobierno-ia', permiso: 'atlas.gia.ver', cargar: () => import('./paginas/Tablero') },
    { path: '/gobierno-ia/casos', permiso: 'atlas.gia.ver', cargar: () => import('./paginas/Casos') },
    { path: '/gobierno-ia/casos/nuevo', permiso: 'atlas.gia.gestionar', cargar: () => import('./paginas/NuevoCaso') },
    { path: '/gobierno-ia/casos/:id', permiso: 'atlas.gia.ver', cargar: () => import('./paginas/Caso') },
    { path: '/gobierno-ia/casos/:id/editar', permiso: 'atlas.gia.gestionar', cargar: () => import('./paginas/EditarCaso') },
    { path: '/gobierno-ia/casos/:id/evaluar', permiso: 'atlas.gia.gestionar', cargar: () => import('./paginas/Evaluar') },
    { path: '/gobierno-ia/politicas', permiso: 'atlas.gia.politicas.ver', cargar: () => import('./paginas/Politicas') },
    { path: '/gobierno-ia/politicas/:id', permiso: 'atlas.gia.politicas.ver', cargar: () => import('./paginas/Politica') },
    { path: '/gobierno-ia/herramientas', permiso: 'atlas.gia.ver', cargar: () => import('./paginas/Herramientas') },
    { path: '/gobierno-ia/incidentes', permiso: 'atlas.gia.ver', cargar: () => import('./paginas/Incidentes') },
    { path: '/gobierno-ia/roles', permiso: 'atlas.gia.ver', cargar: () => import('./paginas/Roles') },
  ],
  navegacion: [
    { etiqueta: 'Gobierno de IA', ruta: '/gobierno-ia', permiso: 'atlas.gia.ver', movil: 'mas', orden: 70, audiencia: 'empleados' },
    { etiqueta: 'Políticas de IA', ruta: '/gobierno-ia/politicas', permiso: 'atlas.gia.politicas.ver', movil: 'mas', orden: 71, audiencia: 'empleados' },
  ],
  bandera: 'modulo.gia',
  banderaPorDefecto: false,
};
