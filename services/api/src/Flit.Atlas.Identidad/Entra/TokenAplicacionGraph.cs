using Flit.Atlas.Identidad.Sesiones;
using Microsoft.Identity.Abstractions;

namespace Flit.Atlas.Identidad.Entra;

/// <summary>
/// Token de aplicación de Graph con la credencial del esquema <see cref="ClaimsAtlas.EsquemaEntra"/>. Hay que nombrar el
/// esquema: sin él, Microsoft.Identity.Web lo infiere del esquema por defecto (la cookie <c>Atlas</c>), que no tiene
/// instancia ni credencial, y falla con <c>IDW10503: Cannot determine the cloud Instance</c>. Pasa siempre fuera del
/// callback de OIDC: en los trabajos en segundo plano (sincronización de personas, avisos) y al leer el perfil.
/// </summary>
internal static class TokenAplicacionGraph
{
    public static Task<string> CabeceraAplicacionAsync(this IAuthorizationHeaderProvider autorizacion, string scope, CancellationToken ct) =>
        autorizacion.CreateAuthorizationHeaderForAppAsync(scope,
            new AuthorizationHeaderProviderOptions { AcquireTokenOptions = new AcquireTokenOptions { AuthenticationOptionsName = ClaimsAtlas.EsquemaEntra } },
            ct);
}
