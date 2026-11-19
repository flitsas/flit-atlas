import type { ModuloAtlas } from '../../app/modulos/contrato';

/**
 * Administración de organizaciones e invitaciones de externos (prompt maestro §6.2, HU 1.5, ATLAS-ADR-0025). Cada ruta
 * tiene su permiso: un administrador de organización (`ext.admin_organizacion`) solo ve Invitaciones, y solo las de su
 * organización (lo filtra el servidor).
 */
export const moduloAdministracion: ModuloAtlas = {
  id: 'administracion',
  nombre: 'Administración',
  descripcion: 'Organizaciones (FLIT, clientes y proveedores) e invitaciones de personas externas con rol, alcance y vencimiento.',
  icono: 'administracion',
  estado: 'disponible',
  rutas: [
    { path: '/administracion/organizaciones', permiso: 'atlas.organizaciones.gestionar', cargar: () => import('./paginas/Organizaciones') },
    { path: '/administracion/invitaciones', permiso: 'atlas.invitaciones.gestionar', cargar: () => import('./paginas/Invitaciones') },
  ],
  navegacion: [
    { etiqueta: 'Invitaciones', ruta: '/administracion/invitaciones', movil: 'mas', orden: 80, permiso: 'atlas.invitaciones.gestionar' },
    { etiqueta: 'Organizaciones', ruta: '/administracion/organizaciones', movil: 'mas', orden: 81, permiso: 'atlas.organizaciones.gestionar' },
  ],
  busqueda: { proveedor: 'administracion', tipos: ['organizacion', 'invitacion'] },
  bandera: 'modulo.administracion',
  banderaPorDefecto: true,
};
