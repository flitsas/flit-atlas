import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Roles y permisos (HU 1.4, ATLAS-ADR-0023): la matriz rol × permiso que genera el código, visible en Atlas. Los
 * endpoints son del núcleo (`/api/v1/plataforma/permisos/matriz`); solo la ve quien tiene `atlas.roles.ver`.
 */
export const moduloPermisos: ModuloAtlas = {
  id: 'permisos',
  nombre: 'Roles y permisos',
  descripcion: 'Qué puede hacer cada rol en cada módulo: la matriz rol × permiso que sale del código de Atlas.',
  icono: 'permisos',
  estado: 'disponible',
  rutas: [{ path: '/permisos', permiso: 'atlas.roles.ver', cargar: () => import('./paginas/Matriz') }],
  navegacion: [{ etiqueta: 'Roles y permisos', ruta: '/permisos', movil: 'mas', orden: 80, permiso: 'atlas.roles.ver' }],
  bandera: 'modulo.permisos',
  banderaPorDefecto: true,
};
