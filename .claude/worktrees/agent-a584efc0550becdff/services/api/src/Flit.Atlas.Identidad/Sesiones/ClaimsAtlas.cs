namespace Flit.Atlas.Identidad.Sesiones;

/// <summary>Claims de la sesión de Atlas (ATLAS-ADR-0004 §3). Se arman en el servidor; el navegador nunca los ve.</summary>
public static class ClaimsAtlas
{
    public const string Sub = "sub";
    public const string PersonaId = "persona_id";
    public const string TipoPrincipal = "tipo_principal";
    public const string OrganizacionId = "organizacion_id";
    public const string Rol = "roles";
    public const string Sid = "sid";
    public const string Amr = "amr";
    public const string AuthTime = "auth_time";
    public const string Proveedor = "proveedor";
    public const string Nombre = "name";

    public const string TipoEmpleado = "empleado";
    public const string TipoExterno = "externo";

    /// <summary>Esquema de la cookie del BFF.</summary>
    public const string EsquemaCookie = "Atlas";

    /// <summary>Esquema del manejador OIDC de Entra ID.</summary>
    public const string EsquemaEntra = "Entra";

    public const string NombreCookie = "__Host-atlas";
}
