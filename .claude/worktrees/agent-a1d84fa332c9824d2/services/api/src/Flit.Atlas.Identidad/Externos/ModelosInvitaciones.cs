using System.ComponentModel;
using Flit.Atlas.Identidad.Autorizacion;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>Estados de una invitación tal como los ve la administración (los vencimientos se calculan al leer).</summary>
public static class EstadosInvitacion
{
    public const string Pendiente = "pendiente";
    public const string EnlaceVencido = "enlace_vencido";
    public const string Aceptada = "aceptada";
    public const string AccesoVencido = "acceso_vencido";
    public const string Suspendida = "suspendida";
    public const string Revocada = "revocada";
}

/// <summary>Permisos extra de una invitación (§6.2). Los usan los módulos de F3–F4 (exportar y sandbox).</summary>
public static class PermisosExtraInvitacion
{
    public const string Exportar = "exportar";
    public const string Sandbox = "sandbox";

    public static readonly string[] Todos = [Exportar, Sandbox];
}

[Description("Invitación de una persona externa, con su estado calculado en este momento.")]
public sealed record InvitacionVista(
    Guid Id,
    [property: Description("Correo invitado (dato personal).")] string Correo,
    string? Nombre,
    Guid OrganizacionId,
    string OrganizacionNombre,
    [property: Description("Código del rol externo (ext.*).")] string Rol,
    string RolNombre,
    [property: Description("Recursos concedidos; vacío = lo que se comparta con la organización.")] IReadOnlyList<EndpointsRoles.AlcanceVista> Alcance,
    [property: Description("exportar, sandbox.")] IReadOnlyList<string> PermisosExtra,
    [property: Description("Cuándo vence el acceso.")] DateTimeOffset? AccesoVenceEn,
    [property: Description("Cuándo vence el enlace actual (72 h después de cada envío).")] DateTimeOffset EnlaceVenceEn,
    [property: Description("pendiente, enlace_vencido, aceptada, acceso_vencido, suspendida o revocada.")] string Estado,
    [property: Description("Persona creada al aceptar.")] Guid? PersonaId,
    DateTimeOffset? AceptadaEn,
    [property: Description("Último envío del correo que salió bien.")] DateTimeOffset? EnviadaEn,
    int Envios,
    DateTimeOffset CreadaEn);

[Description("Resultado de una operación que envía el correo de la invitación.")]
public sealed record InvitacionEnviada(
    InvitacionVista Invitacion,
    [property: Description("false si el ambiente no tiene correo o el envío falló: reenvíala más tarde.")] bool CorreoEnviado);

[Description("Invitación nueva. Un proveedor exige vencimiento y alcance; el vencimiento no supera el máximo configurado.")]
public sealed record SolicitudInvitacion(
    string Correo,
    [property: Description("Nombre del invitado, si lo conoces (opcional).")] string? Nombre,
    Guid OrganizacionId,
    [property: Description("Rol externo (ext.*).")] string Rol,
    [property: Description("Aplicativos, paquetes o documentos concedidos; vacío = lo que se comparta con la organización.")] IReadOnlyList<EndpointsRoles.AlcanceVista>? Alcance,
    [property: Description("Vencimiento del acceso; por defecto, el máximo configurado (90 días).")] DateTimeOffset? AccesoVenceEn,
    [property: Description("exportar, sandbox.")] IReadOnlyList<string>? PermisosExtra);

[Description("Renovación: vencimiento nuevo del acceso. Si la invitación no se ha aceptado, también se envía un enlace nuevo.")]
public sealed record SolicitudRenovacion(
    [property: Description("Vencimiento nuevo; por defecto, el máximo configurado.")] DateTimeOffset? AccesoVenceEn);

[Description("Organización a la que se puede invitar.")]
public sealed record OrganizacionInvitable(Guid Id, string Nombre, [property: Description("cliente o proveedor.")] string Tipo, bool AdminOrganizacionHabilitado);

[Description("Rol externo que se puede invitar.")]
public sealed record RolInvitable(string Codigo, string Nombre, string Descripcion);

[Description("Lo que la sesión puede elegir al invitar.")]
public sealed record OpcionesInvitacion(
    IReadOnlyList<OrganizacionInvitable> Organizaciones,
    IReadOnlyList<RolInvitable> Roles,
    [property: Description("Tipos de alcance que acepta una invitación.")] IReadOnlyList<string> TiposAlcance,
    IReadOnlyList<string> PermisosExtra,
    [property: Description("Días máximos de acceso desde hoy.")] int VigenciaMaximaDias,
    [property: Description("true si invita un administrador de su organización: solo dentro de su alcance.")] bool SoloSuOrganizacion);

/// <summary>Error de una operación de invitaciones o de cuentas externas.</summary>
[Description("Error de una operación de invitaciones o de cuentas externas.")]
public sealed record ErrorExternos(
    [property: Description("Código estable del error.")] string Error,
    string Mensaje);
