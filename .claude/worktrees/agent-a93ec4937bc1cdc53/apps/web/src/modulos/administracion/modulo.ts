import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Administración de organizaciones e invitaciones de externos (prompt maestro §6.2, HU 1.5, ATLAS-ADR-0025) y de los
 * roles: mapeos de grupos de Entra y asignaciones por persona (§6.1, HU 1.4, ATLAS-ADR-0023). Cada ruta tiene su permiso:
 * un administrador de organización (`ext.admin_organizacion`) solo ve Invitaciones, y solo las de su organización (lo
 * filtra el servidor). Roles vive aquí y no en `permisos` porque comparte los avisos, la reautenticación y las
 * confirmaciones de `comunes.tsx` (un módulo no importa a otro, ATLAS-ADR-0026); la matriz sigue en `permisos`.
 */
export const moduloAdministracion: ModuloAtlas = {
  id: 'administracion',
  nombre: 'Administración',
  descripcion:
    'Organizaciones (FLIT, clientes y proveedores), invitaciones de personas externas con rol, alcance y vencimiento, y roles: grupos de Entra y asignaciones.',
  icono: 'administracion',
  estado: 'disponible',
  rutas: [
    { path: '/administracion/organizaciones', permiso: 'atlas.organizaciones.gestionar', cargar: () => import('./paginas/Organizaciones') },
    { path: '/administracion/invitaciones', permiso: 'atlas.invitaciones.gestionar', cargar: () => import('./paginas/Invitaciones') },
    { path: '/administracion/roles', permiso: 'atlas.roles.ver', cargar: () => import('./paginas/Roles') },
  ],
  navegacion: [
    { etiqueta: 'Invitaciones', ruta: '/administracion/invitaciones', movil: 'mas', orden: 80, permiso: 'atlas.invitaciones.gestionar' },
    { etiqueta: 'Organizaciones', ruta: '/administracion/organizaciones', movil: 'mas', orden: 81, permiso: 'atlas.organizaciones.gestionar' },
    { etiqueta: 'Asignación de roles', ruta: '/administracion/roles', movil: 'mas', orden: 82, permiso: 'atlas.roles.ver' },
  ],
  busqueda: { proveedor: 'administracion', tipos: ['organizacion', 'invitacion'] },
  bandera: 'modulo.administracion',
  banderaPorDefecto: true,
};
