using static Flit.Atlas.Plataforma.Permisos.RolesAtlas;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Permisos de la plataforma (§7.4) y de la administración de roles (HU 1.4).</summary>
public static class PermisosPlataforma
{
    public const string Configurar = "atlas.plataforma.configurar";
    public const string RolesGestionar = "atlas.roles.gestionar";

    /// <summary>Agregado en la HU 1.4: ver roles, asignaciones, mapeos de grupos y la matriz rol × permiso.</summary>
    public const string RolesVer = "atlas.roles.ver";

    public const string InvitacionesGestionar = "atlas.invitaciones.gestionar";
    public const string OrganizacionesGestionar = "atlas.organizaciones.gestionar";
    public const string AuditoriaVer = "atlas.auditoria.ver";

    public static ManifiestoPermisos Manifiesto { get; } = new("plataforma", "Plataforma",
    [
        new(Configurar, "Cambiar la configuración de Atlas y las banderas de los módulos."),
        new(RolesGestionar, "Asignar y retirar roles, con alcance y vigencia, y mapear grupos de Entra a roles."),
        new(RolesVer, "Ver los roles, las asignaciones de las personas, los mapeos de grupos y la matriz rol × permiso.", LiderTecnico, Auditor),
        new(InvitacionesGestionar, "Invitar, reenviar, suspender, renovar y revocar invitaciones de externos (el administrador de una organización, solo de la suya).", LiderTecnico, AdminOrganizacion),
        new(OrganizacionesGestionar, "Crear y editar organizaciones (interna, clientes y proveedores).", LiderTecnico),
        new(AuditoriaVer, "Leer la auditoría y las evidencias.", Auditor),
    ], [TiposAlcance.Global, TiposAlcance.Organizacion, TiposAlcance.Ambiente]);
}

/// <summary>
/// Manifiestos de permisos del núcleo: la plataforma y la lista base del §7.4 de los módulos que llegan en F2–F8
/// (ATLAS-ADR-0023). Los permisos base viven aquí para que la matriz y los roles estén completos desde F1 y la PWA filtre
/// la navegación desde ya; cuando llegue cada módulo, usa estos códigos (son contrato) y declara en su propio manifiesto
/// solo los permisos nuevos. Identidad agrega el suyo en <c>Flit.Atlas.Identidad</c>.
/// </summary>
public static class ManifiestosNucleo
{
    public static ManifiestoPermisos Catalogo { get; } = new("catalogo", "Catálogo",
    [
        new("atlas.catalogo.ver", "Ver el catálogo de aplicativos y sus fichas.", [.. Empleados]),
        new("atlas.catalogo.gestionar", "Crear y editar productos, módulos y servicios del catálogo.", LiderTecnico),
    ], [TiposAlcance.Aplicativo]);

    public static ManifiestoPermisos Equipo { get; } = new("equipo", "Equipo y responsables",
    [
        new("atlas.equipo.ver", "Ver personas, responsables y el mapa de carga.", [.. Empleados]),
        new("atlas.equipo.gestionar", "Cambiar el reparto de responsables, traspasos y revisiones.", LiderTecnico),
        new("atlas.equipo.ver_contacto", "Ver el teléfono y los datos de contacto de las personas.", LiderTecnico, Desarrollador, Soporte),
    ]);

    /// <summary>
    /// Lista base del §7.4 para Soporte. Desde la HU 2.6 la publica el módulo <c>Flit.Atlas.Modulos.Soporte</c> (que suma
    /// sus permisos nuevos a estos): por eso ya no va en <see cref="Todos"/>, o el módulo «soporte» quedaría registrado
    /// dos veces. Los códigos siguen siendo contrato.
    /// </summary>
    public static ManifiestoPermisos SoporteSabados { get; } = new("soporte", "Soporte de los sábados",
    [
        new("atlas.soporte.ver", "Ver el calendario de soporte de los sábados.", [.. Empleados]),
        new("atlas.soporte.solicitar_cambio", "Solicitar un cambio de turno y proponer reemplazo.", LiderTecnico, Desarrollador),
        new("atlas.soporte.aprobar", "Aprobar cambios de turno y compensaciones.", LiderTecnico),
        new("atlas.soporte.gestionar", "Configurar la rotación, generar e importar turnos.", LiderTecnico),
    ]);

    public static ManifiestoPermisos Contratos { get; } = new("contratos", "Contratos de integración",
    [
        new("atlas.contratos.ver_internos", "Ver los contratos internos (OpenAPI, AsyncAPI y proto) y su historial.", LiderTecnico, Desarrollador, Soporte),
        new("atlas.contratos.publicar_manual", "Publicar una versión de contrato a mano, por excepción.", LiderTecnico),
        new("atlas.contratos.guias.editar", "Editar las guías de integración.", LiderTecnico, Responsable),
        new("atlas.paquetes.gestionar", "Armar paquetes de contratos y compartirlos con organizaciones.", LiderTecnico, Responsable),
        new("atlas.portal.ver", "Entrar al portal de clientes y ver lo compartido con la organización.", LiderTecnico, Soporte, Integrador, ClienteFuncional, AsesorComercial, Proveedor, AdminOrganizacion),
        new("atlas.exportaciones.solicitar", "Solicitar exportaciones (PDF, Word, Postman, Excel, ZIP).", LiderTecnico, Desarrollador, Negocio, AsesorComercial),
        new("atlas.sandbox.usar", "Usar el sandbox de los contratos.", LiderTecnico, Desarrollador, Soporte),
    ], [TiposAlcance.Paquete]);

    public static ManifiestoPermisos Documentacion { get; } = new("documentacion", "Documentación",
    [
        new("atlas.docs.ver", "Ver documentos según su nivel de visibilidad.", [.. Empleados, .. Externos]),
        new("atlas.docs.editar", "Crear y editar borradores de documentos.", LiderTecnico, Desarrollador, Responsable),
        new("atlas.docs.aprobar", "Aprobar y publicar documentos.", LiderTecnico, Responsable),
        new("atlas.docs.exportar", "Exportar documentos y colecciones.", LiderTecnico, Desarrollador, Soporte, Negocio, AsesorComercial),
    ], [TiposAlcance.Documento]);

    public static ManifiestoPermisos Diagramas { get; } = new("diagramas", "Diagramas",
    [
        new("atlas.arquitectura.ver", "Ver los diagramas de arquitectura (LikeC4) según su visibilidad.", LiderTecnico, Desarrollador, AsesorComercial),
        new("atlas.arquitectura.ver_despliegue", "Ver las vistas de despliegue por ambiente y el drift.", LiderTecnico, Desarrollador),
        new("atlas.diagramas.editar", "Crear y editar diagramas del lienzo y funcionales.", LiderTecnico, Desarrollador, Responsable),
    ]);

    public static ManifiestoPermisos Asistente { get; } = new("asistente", "Asistente Atlas",
    [
        new("atlas.asistente.usar", "Preguntar al asistente, limitado a lo que la persona puede ver.", LiderTecnico, Desarrollador, Soporte, Negocio, GobiernoIa, Auditor, Integrador, ClienteFuncional, AsesorComercial, Proveedor),
        new("atlas.asistente.configurar", "Configurar el asistente, sus fuentes y sus topes.", GobiernoIa),
    ]);

    public static ManifiestoPermisos GobiernoDeIa { get; } = new("gia", "Gobierno de IA",
    [
        new("atlas.gia.ver", "Ver el inventario de casos de IA, políticas y costos.", GobiernoIa, Auditor),
        new("atlas.gia.gestionar", "Gestionar casos de uso, riesgos, políticas y llaves virtuales.", GobiernoIa),
        new("atlas.gia.auditar", "Revisar la evidencia de gobierno de IA.", GobiernoIa, Auditor),
    ]);

    /// <summary>
    /// Manifiestos que vienen en <c>Flit.Atlas.Plataforma</c>, en el orden de la matriz. Sin <see cref="SoporteSabados"/>:
    /// lo registra su módulo.
    /// </summary>
    public static IReadOnlyList<ManifiestoPermisos> Todos { get; } =
        [PermisosPlataforma.Manifiesto, Catalogo, Equipo, Contratos, Documentacion, Diagramas, Asistente, GobiernoDeIa];
}
