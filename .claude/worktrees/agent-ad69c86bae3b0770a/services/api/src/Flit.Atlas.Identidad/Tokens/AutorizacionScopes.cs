using Microsoft.AspNetCore.Builder;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>Metadato de los endpoints que aceptan un token Bearer con scope (lo lee el contrato OpenAPI).</summary>
public sealed class MetadatoScope(IReadOnlyList<string> scopes)
{
    /// <summary>Basta con uno de ellos.</summary>
    public IReadOnlyList<string> Scopes { get; } = scopes;
}

/// <summary>
/// Autorización por scope para los clientes que no son el navegador (ATLAS-ADR-0024). El endpoint se autentica <b>solo</b>
/// con el esquema de validación de OpenIddict: la cookie del BFF nunca basta para un endpoint con scope, y un Bearer
/// nunca abre la sesión del BFF (el esquema por defecto sigue siendo la cookie). Los Bearer no pasan por la
/// antifalsificación de la cookie: no hay credencial ambiente que un sitio ajeno pueda aprovechar.
/// </summary>
public static class AutorizacionScopes
{
    /// <summary>Esquema de autenticación de los tokens Bearer de Atlas.</summary>
    public const string EsquemaToken = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;

    /// <summary>
    /// Exige un token de acceso de Atlas vigente (firma, audiencia <c>atlas-api</c>, y token y autorización no revocados)
    /// con al menos uno de los <paramref name="scopes"/>. Sin token: 401; con token sin el scope: 403.
    /// </summary>
    public static TBuilder RequiereScope<TBuilder>(this TBuilder builder, params string[] scopes) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (scopes.Length == 0)
        {
            throw new ArgumentException("Indica al menos un scope.", nameof(scopes));
        }
        var copia = scopes.ToArray();
        return builder
            .WithMetadata(new MetadatoScope(copia))
            .RequireAuthorization(p => p
                .AddAuthenticationSchemes(EsquemaToken)
                .RequireAuthenticatedUser()
                .RequireAssertion(c => copia.Any(c.User.HasScope)));
    }
}
