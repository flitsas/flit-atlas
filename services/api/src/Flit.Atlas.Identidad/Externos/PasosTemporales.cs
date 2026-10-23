using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Estado de los pasos intermedios del registro y del ingreso de externos (ATLAS-ADR-0025): el desafío de WebAuthn de
/// Identity (<c>PasskeyHandler</c>), la clave TOTP aún sin confirmar y el primer paso del ingreso con contraseña. Vive en
/// Redis (compartido entre réplicas) con vencimiento corto y cifrado con Data Protection; el navegador solo recibe un id
/// aleatorio. Los desafíos se <b>toman</b> (se leen y se borran a la vez): no sirven dos veces.
/// </summary>
public sealed class PasosTemporales(IConnectionMultiplexer redis, IDataProtectionProvider proteccion, IOptions<OpcionesExternos> opciones)
{
    private readonly IDataProtector _protector = proteccion.CreateProtector("Flit.Atlas.Identidad.Externos.Pasos.v1");

    public const string Prefijo = "atlas:identidad:paso:";

    public async Task<string> GuardarAsync<T>(string tipo, T valor)
    {
        var id = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
        await redis.GetDatabase().StringSetAsync(Clave(tipo, id), Proteger(valor), opciones.Value.VigenciaPaso).ConfigureAwait(false);
        return id;
    }

    /// <summary>Lee el paso sin borrarlo (p. ej. la clave TOTP, que admite reintentar el código).</summary>
    public async Task<T?> LeerAsync<T>(string tipo, string? id) where T : class =>
        Valido(id) ? Desproteger<T>(await redis.GetDatabase().StringGetAsync(Clave(tipo, id!)).ConfigureAwait(false)) : null;

    /// <summary>Lee y borra el paso en una sola operación (GETDEL): un desafío no sirve dos veces.</summary>
    public async Task<T?> TomarAsync<T>(string tipo, string? id) where T : class =>
        Valido(id) ? Desproteger<T>(await redis.GetDatabase().StringGetDeleteAsync(Clave(tipo, id!)).ConfigureAwait(false)) : null;

    public Task BorrarAsync(string tipo, string? id) =>
        Valido(id) ? redis.GetDatabase().KeyDeleteAsync(Clave(tipo, id!)) : Task.CompletedTask;

    private static string Clave(string tipo, string id) => $"{Prefijo}{tipo}:{id}";

    private static bool Valido(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 64 && Base64Url.IsValid(id);

    private byte[] Proteger<T>(T valor) => _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(valor));

    private T? Desproteger<T>(RedisValue valor) where T : class
    {
        if (valor.IsNullOrEmpty)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(_protector.Unprotect((byte[])valor!));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
