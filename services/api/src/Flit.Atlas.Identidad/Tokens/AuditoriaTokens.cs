using Flit.Atlas.Identidad.Seguridad;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>
/// Tipos de evento de seguridad del servidor de tokens (HU 1.3c, ATLAS-ADR-0024). Se registran con
/// <see cref="RegistroSeguridad"/> como los de <see cref="TiposEventoSeguridad"/> y quedan en la auditoría como
/// <c>identidad.&lt;tipo&gt;</c>. Nunca se guarda el token, solo el cliente, el flujo, los scopes y el código de error.
/// </summary>
public static class TiposEventoTokens
{
    public const string AutorizacionConcedida = "autorizacion_concedida";
    public const string AutorizacionRechazada = "autorizacion_rechazada";
    public const string TokenEmitido = "token_emitido";
    public const string TokenRechazado = "token_rechazado";
    public const string ClienteRechazado = "cliente_rechazado";

    /// <summary>Se presentó un refresh token ya canjeado: OpenIddict revocó toda la cadena de esa autorización.</summary>
    public const string RefreshReutilizado = "refresh_reutilizado";
}

/// <summary>
/// Eventos de seguridad del servidor de tokens (ATLAS-ADR-0024): cada respuesta de <c>/connect/token</c> y cada error
/// de <c>/connect/authorize</c> deja un <c>identidad.*</c> en la auditoría. Corre antes de escribir la respuesta, cuando
/// OpenIddict ya decidió el resultado. Nunca registra tokens, códigos ni secretos: solo el cliente, el flujo, los scopes
/// y el código de error.
/// </summary>
internal sealed partial class AuditoriaTokens(RegistroSeguridad registro, IHttpContextAccessor contexto, ILogger<AuditoriaTokens> log)
    : IOpenIddictServerHandler<ApplyTokenResponseContext>, IOpenIddictServerHandler<ApplyAuthorizationResponseContext>
{
    /// <summary>
    /// Clave de <see cref="HttpContext.Items"/> donde el endpoint de token deja a quién se emitió (usuario y persona), para
    /// registrar el evento solo si OpenIddict de verdad entrega el token.
    /// </summary>
    internal const string ClaveDatos = "atlas.tokens.datos";

    /// <summary>Error de OpenIddict cuando se presenta un refresh token ya canjeado (y revoca la cadena).</summary>
    internal const string ErrorRefreshCanjeado = "ID2012";

    /// <summary>Lo mismo con un código de autorización ya canjeado.</summary>
    internal const string ErrorCodigoCanjeado = "ID2010";

    public async ValueTask HandleAsync(ApplyTokenResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var solicitud = context.Request;
        var respuesta = context.Response;
        var datos = Datos();
        string tipo;
        object detalle;
        if (string.IsNullOrEmpty(respuesta.Error))
        {
            tipo = TiposEventoTokens.TokenEmitido;
            detalle = new
            {
                cliente = solicitud?.ClientId,
                flujo = solicitud?.GrantType,
                scopes = respuesta.Scope,
                refresh = !string.IsNullOrEmpty(respuesta.RefreshToken),
            };
        }
        else
        {
            var codigoOpenIddict = CodigoOpenIddict(respuesta.ErrorUri);
            tipo = respuesta.Error switch
            {
                _ when codigoOpenIddict == ErrorRefreshCanjeado => TiposEventoTokens.RefreshReutilizado,
                Errors.InvalidClient or Errors.UnauthorizedClient => TiposEventoTokens.ClienteRechazado,
                _ => TiposEventoTokens.TokenRechazado,
            };
            detalle = new
            {
                cliente = solicitud?.ClientId,
                flujo = solicitud?.GrantType,
                error = respuesta.Error,
                codigo = codigoOpenIddict,
                motivo = codigoOpenIddict is null ? respuesta.ErrorDescription : null,
            };
        }
        await RegistrarAsync(tipo, datos with { Detalle = detalle }, context.CancellationToken);
    }

    public async ValueTask HandleAsync(ApplyAuthorizationResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var solicitud = context.Request;
        var respuesta = context.Response;
        if (string.IsNullOrEmpty(respuesta.Error))
        {
            // Se entregó un código: la concesión queda registrada con el usuario de la sesión.
            await RegistrarAsync(TiposEventoTokens.AutorizacionConcedida, Datos() with
            {
                Detalle = new { cliente = solicitud?.ClientId, scopes = solicitud?.Scope },
            }, context.CancellationToken);
            return;
        }
        var tipo = respuesta.Error is Errors.InvalidClient or Errors.UnauthorizedClient
            ? TiposEventoTokens.ClienteRechazado
            : TiposEventoTokens.AutorizacionRechazada;
        await RegistrarAsync(tipo, Datos() with
        {
            Detalle = new
            {
                cliente = solicitud?.ClientId,
                error = respuesta.Error,
                codigo = CodigoOpenIddict(respuesta.ErrorUri),
            },
        }, context.CancellationToken);
    }

    private DatosEventoSeguridad Datos() =>
        contexto.HttpContext?.Items[ClaveDatos] as DatosEventoSeguridad ?? new DatosEventoSeguridad();

    /// <summary><c>https://documentation.openiddict.com/errors/ID2012</c> → <c>ID2012</c>.</summary>
    internal static string? CodigoOpenIddict(string? uri) =>
        uri is not null && uri.LastIndexOf('/') is var i and >= 0 && uri[(i + 1)..] is { Length: > 2 } codigo
            && codigo.StartsWith("ID", StringComparison.Ordinal)
            ? codigo
            : null;

    private async Task RegistrarAsync(string tipo, DatosEventoSeguridad datos, CancellationToken ct)
    {
        try
        {
            await registro.RegistrarAsync(tipo, datos, ct);
        }
#pragma warning disable CA1031 // Un fallo al auditar no debe cambiar la respuesta del protocolo.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            NoSeRegistro(log, tipo, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo registrar el evento de seguridad {Tipo} del servidor de tokens")]
    private static partial void NoSeRegistro(ILogger log, string tipo, Exception error);
}
