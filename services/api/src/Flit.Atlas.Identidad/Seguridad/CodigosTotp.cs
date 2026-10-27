using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Identidad.Datos;
using Microsoft.AspNetCore.Identity;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>
/// Formato y paso de tiempo de los códigos TOTP de las cuentas locales (ATLAS-ADR-0049). La validez del código la decide
/// siempre el <c>AuthenticatorTokenProvider</c> de ASP.NET Core Identity; esto solo:
/// <list type="bullet">
/// <item>normaliza el código <b>una sola vez</b> (sin espacios; exactamente 6 dígitos ASCII, sin guiones ni otros
/// caracteres), para que la protección contra la reutilización y la verificación vean el mismo texto;</item>
/// <item>ubica, después de que Identity lo aceptó, en qué paso de 30 s cae (RFC 6238 §4, la misma HMAC-SHA1 de la BCL que
/// usa Identity por dentro y que no expone), para guardar el último paso aceptado por usuario (RFC 6238 §5.2).</item>
/// </list>
/// No es un verificador propio: si Identity rechaza el código, nada de esto se consulta.
/// </summary>
public static class CodigosTotp
{
    public const int Digitos = 6;

    private const int PeriodoSegundos = 30;

    /// <summary>
    /// Identity acepta ±2 pasos alrededor de su hora; se busca en ±3 para cubrir el redondeo y el instante distinto en que
    /// cada uno lee el reloj. Si no aparece, el código se rechaza (falla cerrada).
    /// </summary>
    private const int Ventana = 3;

    /// <summary>El código sin espacios si son exactamente 6 dígitos ASCII; si no, <c>null</c> (otro formato se rechaza).</summary>
    public static string? Normalizar(string? codigo)
    {
        var limpio = (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        return limpio.Length == Digitos && limpio.All(char.IsAsciiDigit) ? limpio : null;
    }

    /// <summary>Paso de tiempo (Unix / 30 s) en que <paramref name="codigo"/> coincide con la clave, o <c>null</c>.</summary>
    public static long? Paso(string claveBase32, string codigo, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(claveBase32);
        ArgumentNullException.ThrowIfNull(codigo);
        var clave = DesdeBase32(claveBase32);
        if (clave is null)
        {
            return null;
        }
        var actual = ahora.ToUnixTimeSeconds() / PeriodoSegundos;
        var esperado = Encoding.ASCII.GetBytes(codigo);
        // Del paso actual hacia afuera; todos se calculan para que el tiempo no diga cuál coincidió.
        long? encontrado = null;
        foreach (var desfase in Enumerable.Range(-Ventana, (2 * Ventana) + 1).OrderBy(Math.Abs))
        {
            if (CryptographicOperations.FixedTimeEquals(esperado, Encoding.ASCII.GetBytes(Calcular(clave, actual + desfase))) && encontrado is null)
            {
                encontrado = actual + desfase;
            }
        }
        return encontrado;
    }

    /// <summary>HOTP (RFC 4226 §5.3) del paso, con 6 dígitos y HMAC-SHA1, como el proveedor de Identity.</summary>
    private static string Calcular(byte[] clave, long paso)
    {
        Span<byte> contador = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(contador, paso);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
#pragma warning disable CA5350 // RFC 6238 y las aplicaciones de autenticación usan HMAC-SHA1, como el proveedor de Identity.
        HMACSHA1.HashData(clave, contador, hash);
#pragma warning restore CA5350
        var desplazamiento = hash[^1] & 0x0f;
        var binario = BinaryPrimitives.ReadInt32BigEndian(hash.Slice(desplazamiento, 4)) & 0x7fffffff;
        return (binario % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Base32 de RFC 4648 (el de las claves que genera <see cref="UserManager{TUser}.GenerateNewAuthenticatorKey"/>).</summary>
    private static byte[]? DesdeBase32(string texto)
    {
        const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var limpio = texto.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var salida = new List<byte>(limpio.Length * 5 / 8);
        int bufer = 0, bits = 0;
        foreach (var c in limpio)
        {
            var valor = Alfabeto.IndexOf(c, StringComparison.Ordinal);
            if (valor < 0)
            {
                return null;
            }
            bufer = (bufer << 5) | valor;
            bits += 5;
            if (bits >= 8)
            {
                salida.Add((byte)(bufer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. salida];
    }
}

/// <summary>
/// Hash ficticio de contraseña (PBKDF2 de Identity) para igualar el tiempo de las respuestas cuando no hay contraseña que
/// comparar: correo desconocido, empleado, cuenta sin contraseña (solo passkey) o bloqueada (F4-SEC-04, ATLAS-ADR-0049).
/// </summary>
internal static class HashFicticio
{
    private static string? _hash;

    /// <summary>Con el mismo hasher (y costo) que las contraseñas reales.</summary>
    public static void Verificar(IPasswordHasher<UsuarioAtlas> hasher, string? contrasena)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        var ficticio = new UsuarioAtlas { PersonaId = Guid.Empty };
        _hash ??= hasher.HashPassword(ficticio, Guid.NewGuid().ToString());
        hasher.VerifyHashedPassword(ficticio, _hash, contrasena ?? string.Empty);
    }
}
