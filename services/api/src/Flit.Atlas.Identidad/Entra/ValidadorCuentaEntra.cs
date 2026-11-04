using System.Security.Claims;

namespace Flit.Atlas.Identidad.Entra;

/// <summary>
/// Reglas que el token de Entra ID debe cumplir para que un empleado entre a Atlas (prompt maestro §6.1). La autoridad
/// ya es de tenant único; esto es la segunda capa: se valida en el servidor y no se confía en la configuración de Entra.
/// </summary>
public static class ValidadorCuentaEntra
{
    /// <summary>Tenant de las cuentas personales de Microsoft (MSA).</summary>
    public static readonly Guid TenantCuentasPersonales = Guid.Parse("9188040d-6c67-4c5b-b112-36a304b66dad");

    public const string ClaimTenant = "http://schemas.microsoft.com/identity/claims/tenantid";
    public const string ClaimTenantCorto = "tid";
    public const string ClaimObjeto = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    public const string ClaimObjetoCorto = "oid";
    public const string ClaimProveedorIdentidad = "http://schemas.microsoft.com/identity/claims/identityprovider";
    public const string ClaimProveedorIdentidadCorto = "idp";
    public const string ClaimEmisor = "iss";

    public sealed record Resultado(bool Valida, string? Motivo, Guid Oid)
    {
        public static Resultado Rechazo(string motivo) => new(false, motivo, Guid.Empty);
    }

    public static Resultado Validar(ClaimsPrincipal principal, Guid tenantFlit)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!Guid.TryParse(Valor(principal, ClaimTenantCorto, ClaimTenant), out var tid))
        {
            return Resultado.Rechazo("sin_tenant");
        }
        if (tid == TenantCuentasPersonales)
        {
            return Resultado.Rechazo("cuenta_personal");
        }
        if (tid != tenantFlit)
        {
            return Resultado.Rechazo("otro_tenant");
        }

        // Un invitado (B2B) o una cuenta personal agregada al tenant trae el claim idp con su proveedor de origen,
        // distinto del emisor del tenant. Atlas solo admite empleados con cuenta del tenant de FLIT.
        var idp = Valor(principal, ClaimProveedorIdentidadCorto, ClaimProveedorIdentidad);
        var emisor = principal.FindFirst(ClaimEmisor)?.Value ?? principal.Claims.FirstOrDefault()?.Issuer;
        if (!string.IsNullOrEmpty(idp) && !string.Equals(idp, emisor, StringComparison.OrdinalIgnoreCase))
        {
            return Resultado.Rechazo("invitado_externo");
        }

        if (!Guid.TryParse(Valor(principal, ClaimObjetoCorto, ClaimObjeto), out var oid))
        {
            return Resultado.Rechazo("sin_oid");
        }
        return new Resultado(true, null, oid);
    }

    internal static string? Valor(ClaimsPrincipal principal, params string[] tipos) =>
        tipos.Select(t => principal.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
