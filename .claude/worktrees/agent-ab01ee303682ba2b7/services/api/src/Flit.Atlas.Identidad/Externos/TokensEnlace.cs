using System.Buffers.Text;
using System.Security.Cryptography;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Token de los enlaces de invitación (ATLAS-ADR-0025): 256 bits del generador criptográfico del framework, en
/// base64url. En la base solo se guarda su SHA-256 y la comparación es en tiempo constante. El token en claro solo existe
/// en la memoria de la petición y en el correo: nunca va a los logs, a la auditoría ni a la outbox.
/// </summary>
public static class TokensEnlace
{
    private const int Bytes = 32;

    /// <summary>Token nuevo y su hash.</summary>
    public static (string Token, byte[] Hash) Generar()
    {
        var aleatorio = RandomNumberGenerator.GetBytes(Bytes);
        var token = Base64Url.EncodeToString(aleatorio);
        CryptographicOperations.ZeroMemory(aleatorio);
        return (token, Hash(token));
    }

    /// <summary>SHA-256 del token tal como viaja en el enlace; <c>null</c> si no tiene la forma de un token de Atlas.</summary>
    public static byte[]? HashSiValido(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != Base64Url.GetEncodedLength(Bytes) || !Base64Url.IsValid(token))
        {
            return null;
        }
        return Hash(token);
    }

    /// <summary>¿El hash guardado corresponde al calculado? En tiempo constante.</summary>
    public static bool Coincide(byte[] guardado, byte[] calculado) =>
        guardado.Length == calculado.Length && CryptographicOperations.FixedTimeEquals(guardado, calculado);

    private static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));
}
