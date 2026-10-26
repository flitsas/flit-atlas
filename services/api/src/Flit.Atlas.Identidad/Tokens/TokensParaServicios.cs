using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>
/// Tokens de acceso para los servicios internos (ATLAS-ADR-0044): un token cuya audiencia <b>no</b> incluye atlas-api
/// (p. ej. <c>atlas-docs</c>, <c>atlas-render</c>) sale firmado (JWS) pero sin cifrar, para que ese servicio lo valide
/// con el JWKS público de atlas-api sin tener la llave de cifrado. Los de atlas-api siguen cifrados (JWE), como hasta
/// ahora (ATLAS-ADR-0024). Corre justo después de <see cref="OpenIddictServerHandlers.Protection.AttachSecurityCredentials"/>,
/// que pone las credenciales por defecto. La firma, la emisión y el registro del token siguen siendo de OpenIddict.
/// </summary>
internal sealed class TokensParaServicios : IOpenIddictServerHandler<GenerateTokenContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<GenerateTokenContext>()
            .UseSingletonHandler<TokensParaServicios>()
            .SetOrder(OpenIddictServerHandlers.Protection.AttachSecurityCredentials.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(GenerateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TokenType == OpenIddictConstants.TokenTypeIdentifiers.AccessToken && SoloParaServicios(context.Principal))
        {
            context.EncryptionCredentials = null;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Tiene audiencias y ninguna es atlas-api.</summary>
    internal static bool SoloParaServicios(System.Security.Claims.ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return false;
        }
        var audiencias = principal.GetAudiences();
        if (audiencias.IsDefaultOrEmpty)
        {
            audiencias = principal.GetResources();
        }
        return !audiencias.IsDefaultOrEmpty && !audiencias.Contains(OpcionesTokens.AudienciaApi, StringComparer.Ordinal);
    }
}
