import type { MatrizPermisos } from '../../src/modulos/permisos/api';

/**
 * Recorte de la matriz que entrega `GET /api/v1/plataforma/permisos/matriz` (mismos roles y forma que
 * docs/permisos/matriz-rol-permiso.md). Lo usan Vitest y Playwright (`page.route`).
 */
export const MATRIZ: MatrizPermisos = {
  roles: [
    { codigo: 'atlas.superadmin', nombre: 'Superadministrador', tipo: 'interno', descripcion: 'Administración de Atlas.', asignable: true },
    { codigo: 'atlas.lider_tecnico', nombre: 'Líder técnico', tipo: 'interno', descripcion: 'Gestiona catálogo, responsables y rotación.', asignable: true },
    { codigo: 'atlas.desarrollador', nombre: 'Desarrollador', tipo: 'interno', descripcion: 'Ve todo lo interno_ti.', asignable: true },
    { codigo: 'atlas.soporte', nombre: 'Soporte', tipo: 'interno', descripcion: 'Mesa de ayuda e implementación.', asignable: true },
    { codigo: 'atlas.negocio', nombre: 'Negocio', tipo: 'interno', descripcion: 'Comercial interno y gerencia.', asignable: true },
    { codigo: 'atlas.gobierno_ia', nombre: 'Gobierno de IA', tipo: 'interno', descripcion: 'Comité y responsable de IA.', asignable: true },
    { codigo: 'atlas.auditor', nombre: 'Auditor', tipo: 'interno', descripcion: 'Lectura de auditoría y evidencias.', asignable: true },
    { codigo: 'atlas.lector', nombre: 'Lector', tipo: 'interno', descripcion: 'Todo empleado.', asignable: true },
    { codigo: 'ext.integrador', nombre: 'Integrador', tipo: 'externo', descripcion: 'Equipo técnico de un cliente.', asignable: true },
    { codigo: 'ext.cliente_funcional', nombre: 'Cliente funcional', tipo: 'externo', descripcion: 'Organismos de tránsito.', asignable: true },
    { codigo: 'ext.asesor_comercial', nombre: 'Asesor comercial', tipo: 'externo', descripcion: 'Asesores comerciales externos.', asignable: true },
    { codigo: 'ext.proveedor', nombre: 'Proveedor', tipo: 'externo', descripcion: 'Consultores y proveedores.', asignable: true },
    {
      codigo: 'ext.admin_organizacion',
      nombre: 'Administrador de la organización',
      tipo: 'externo',
      descripcion: 'Responsable del lado del cliente.',
      asignable: true,
    },
    { codigo: 'atlas.responsable', nombre: 'Responsable del aplicativo', tipo: 'implicito', descripcion: 'Implícito.', asignable: false },
  ],
  modulos: [
    {
      id: 'plataforma',
      nombre: 'Plataforma',
      permisos: [
        { codigo: 'atlas.plataforma.configurar', descripcion: 'Cambiar la configuración de Atlas.', roles: ['atlas.superadmin'] },
        {
          codigo: 'atlas.roles.ver',
          descripcion: 'Ver los roles, las asignaciones y la matriz rol × permiso.',
          roles: ['atlas.superadmin', 'atlas.lider_tecnico', 'atlas.auditor'],
        },
        {
          codigo: 'atlas.invitaciones.gestionar',
          descripcion: 'Invitar externos.',
          roles: ['atlas.superadmin', 'atlas.lider_tecnico', 'ext.admin_organizacion'],
        },
      ],
    },
    {
      id: 'hola',
      nombre: 'Hola (ejemplo)',
      permisos: [
        {
          codigo: 'atlas.hola.ver',
          descripcion: 'Ver los saludos de tu organización (módulo de ejemplo).',
          roles: ['atlas.superadmin', 'atlas.lector', 'ext.integrador'],
        },
        {
          codigo: 'atlas.hola.gestionar',
          descripcion: 'Escribir saludos en tu organización (módulo de ejemplo).',
          roles: ['atlas.superadmin', 'atlas.lider_tecnico', 'atlas.desarrollador'],
        },
      ],
    },
  ],
};
