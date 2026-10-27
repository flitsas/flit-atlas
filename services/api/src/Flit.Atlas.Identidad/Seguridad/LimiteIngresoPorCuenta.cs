using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>
/// Límite de intentos de ingreso local por <b>cuenta atacada</b> (F4-SEC-01, ATLAS-ADR-0049), además de los límites por IP
/// de <see cref="LimitacionTasa"/>, que no frenan un ataque repartido entre muchas IP. Ventana fija en Redis, compartida
/// por las réplicas; la clave es el SHA-256 del correo normalizado (Redis no guarda el correo) y se cuenta exista o no la
/// cuenta: el 429 no dice si el correo es de alguien.
/// </summary>
public sealed class LimiteIngresoPorCuenta(IConnectionMultiplexer redis, IOptions<OpcionesLimiteTasa> opciones, RegistroSeguridad registro)
{
    public const string Prefijo = "atlas:identidad:limite-cuenta:";

    /// <summary>Suma un intento para el correo; <c>false</c> si pasa el tope de la ventana.</summary>
    public async Task<bool> PermitirAsync(string? correo, CancellationToken ct)
    {
        var limites = opciones.Value;
        var normalizado = (correo ?? string.Empty).Trim().ToLowerInvariant();
        var clave = Prefijo + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizado)));
        var baseRedis = redis.GetDatabase();
        var intentos = await baseRedis.StringIncrementAsync(clave).ConfigureAwait(false);
        if (intentos == 1)
        {
            await baseRedis.KeyExpireAsync(clave, limites.VentanaPorCuenta).ConfigureAwait(false);
        }
        if (intentos <= limites.IngresoPorCuenta)
        {
            return true;
        }
        if (intentos == limites.IngresoPorCuenta + 1)
        {
            // Una vez por ventana: un ataque no inunda la auditoría.
            await registro.RegistrarAsync(TiposEventoSeguridad.LimiteTasa, new DatosEventoSeguridad
            {
                Detalle = new { limite = "ingreso_por_cuenta", ventana_minutos = (int)limites.VentanaPorCuenta.TotalMinutes },
            }, ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>El mismo 429 (problem+json con <c>Retry-After</c>) que los demás límites de tasa.</summary>
    public IResult Rechazo(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var segundos = Math.Max(1, (int)Math.Ceiling(opciones.Value.VentanaPorCuenta.TotalSeconds));
        http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Demasiadas peticiones. Intenta de nuevo en un momento.",
            extensions: new Dictionary<string, object?> { ["codigo"] = LimitacionTasa.Codigo, ["reintentarEnSegundos"] = segundos });
    }
}
