using Flit.Atlas.Identidad.Sesiones;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>
/// Políticas de autorización de la administración de identidad (ATLAS-ADR-0022).
/// <para>
/// <b>Provisional hasta la HU 1.4:</b> <see cref="Administrar"/> exige el rol <see cref="RolProvisional"/> en la
/// sesión. Cuando lleguen los permisos finos, la HU 1.4 redefine la política con el mismo nombre (una segunda
/// <c>AddPolicy(PoliticasIdentidad.Administrar, …)</c> reemplaza esta) para exigir el permiso que corresponda en el
/// manifiesto, sin tocar los endpoints. Ver <see cref="Configurar"/>.
/// </para>
/// </summary>
public static class PoliticasIdentidad
{
    /// <summary>Suspender y reactivar personas y cerrar todas sus sesiones.</summary>
    public const string Administrar = "identidad.administrar";

    /// <summary>Rol que hoy concede <see cref="Administrar"/> (prompt maestro §7.2: «Todo»).</summary>
    public const string RolProvisional = "atlas.superadmin";

    /// <summary>Punto de extensión de la HU 1.4: aquí se cambia el requisito, no en los endpoints.</summary>
    public static void Configurar(AuthorizationBuilder autorizacion)
    {
        ArgumentNullException.ThrowIfNull(autorizacion);
        autorizacion.AddPolicy(Administrar, p => p.RequireAuthenticatedUser().RequireClaim(ClaimsAtlas.Rol, RolProvisional));
    }
}
