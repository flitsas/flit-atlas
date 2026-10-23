using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>
/// Políticas de autorización de la administración de identidad (ATLAS-ADR-0022). Desde la HU 1.4 (ATLAS-ADR-0023),
/// <see cref="Administrar"/> exige el permiso <see cref="PermisosIdentidad.Administrar"/> del manifiesto, calculado en el
/// servidor en cada petición (antes era el rol <c>atlas.superadmin</c> en la sesión). Los endpoints no cambian.
/// </summary>
public static class PoliticasIdentidad
{
    /// <summary>Suspender y reactivar personas y cerrar todas sus sesiones.</summary>
    public const string Administrar = "identidad.administrar";

    public static void Configurar(AuthorizationBuilder autorizacion)
    {
        ArgumentNullException.ThrowIfNull(autorizacion);
        autorizacion.AddPolicy(Administrar, ExtensionesPermisos.Politica(PermisosIdentidad.Administrar));
    }
}
