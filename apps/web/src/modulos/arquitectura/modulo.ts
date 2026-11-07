import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Arquitectura como código (prompt maestro §8.6, ATLAS-ADR-0056). `ext.asesor_comercial` tiene `atlas.arquitectura.ver` por el §7.4, pero un
 * externo no ve nada de esto: la entrada es solo para empleados y el servidor le responde vacío. Las páginas y el visor de LikeC4 cargan
 * aparte: no suman al bundle inicial.
 */
export const moduloArquitectura: ModuloAtlas = {
  id: 'arquitectura',
  nombre: 'Arquitectura',
  descripcion: 'Vistas de LikeC4 por sha y canal, despliegue por ambiente y drift entre el modelo, gitops y Argo CD.',
  icono: 'diagramas',
  estado: 'disponible',
  rutas: [
    { path: '/arquitectura', permiso: 'atlas.arquitectura.ver', cargar: () => import('./paginas/Inicio') },
    { path: '/arquitectura/proyectos/:proyecto', permiso: 'atlas.arquitectura.ver', cargar: () => import('./paginas/Vistas') },
    { path: '/arquitectura/proyectos/:proyecto/vistas/:vista', permiso: 'atlas.arquitectura.ver', cargar: () => import('./paginas/Vistas') },
    { path: '/arquitectura/despliegue', permiso: 'atlas.arquitectura.ver_despliegue', cargar: () => import('./paginas/Despliegue') },
    { path: '/arquitectura/drift', permiso: 'atlas.arquitectura.ver_despliegue', cargar: () => import('./paginas/Drift') },
  ],
  navegacion: [{ etiqueta: 'Arquitectura', ruta: '/arquitectura', movil: 'mas', orden: 56, permiso: 'atlas.arquitectura.ver', audiencia: 'empleados' }],
  bandera: 'modulo.arquitectura',
  banderaPorDefecto: true,
};
