using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>
/// Llaves VAPID de Web Push (prompt maestro §11, ATLAS-ADR-0020): P-256 en base64url, la pública sin comprimir
/// (65 bytes) y la privada de 32 bytes. Son secreto: en qa y pdn llegan de un SealedSecret como
/// <c>Plataforma__Push__ClavePublica</c> y <c>Plataforma__Push__ClavePrivada</c>; en local, de los user secrets del
/// AppHost o, si no están, se generan al arrancar (las suscripciones de una corrida no sirven en la siguiente y la
/// PWA las renueva sola).
/// </summary>
public sealed record LlavesVapid(bool Habilitado, string? ClavePublica, string? ClavePrivada, string Asunto)
{
    public const string ClavePublicaConfig = "Plataforma:Push:ClavePublica";
    public const string ClavePrivadaConfig = "Plataforma:Push:ClavePrivada";

    /// <summary>Contacto que ven los servicios de push (Google, Mozilla, Apple) si hay un problema con los envíos.</summary>
    public const string AsuntoConfig = "Plataforma:Push:Asunto";

    public const string AsuntoPorDefecto = "mailto:atlas@ejemplo.com";

    private static readonly string[] AmbientesConLlavesEfimeras = ["local", "dev"];

    public static LlavesVapid Deshabilitado { get; } = new(false, null, null, AsuntoPorDefecto);

    public static LlavesVapid Resolver(IConfiguration configuracion, ILogger registro)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var publica = configuracion[ClavePublicaConfig]?.Trim();
        var privada = configuracion[ClavePrivadaConfig]?.Trim();
        var asunto = string.IsNullOrWhiteSpace(configuracion[AsuntoConfig]) ? AsuntoPorDefecto : configuracion[AsuntoConfig]!.Trim();
        var ambiente = configuracion["Atlas:Ambiente"]?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!string.IsNullOrEmpty(publica) || !string.IsNullOrEmpty(privada))
        {
            var faltan = new[] { (ClavePublicaConfig, publica), (ClavePrivadaConfig, privada) }
                .Where(c => string.IsNullOrEmpty(c.Item2))
                .Select(c => c.Item1.Replace(":", "__", StringComparison.Ordinal))
                .ToList();
            if (faltan.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Web Push necesita las dos llaves VAPID; falta: {string.Join(", ", faltan)}.");
            }
            Validar(publica!, privada!);
            if (!asunto.StartsWith("mailto:", StringComparison.Ordinal) && !asunto.StartsWith("https://", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Plataforma__Push__Asunto debe empezar por «mailto:» o «https://».");
            }
            return new LlavesVapid(true, publica, privada, asunto);
        }

        if (AmbientesConLlavesEfimeras.Contains(ambiente))
        {
            RegistroVapid.LogEfimeras(registro, ambiente);
            var (nuevaPublica, nuevaPrivada) = Generar();
            return new LlavesVapid(true, nuevaPublica, nuevaPrivada, asunto);
        }

        RegistroVapid.LogDeshabilitado(registro);
        return Deshabilitado;
    }

    /// <summary>Par nuevo de llaves P-256 con la criptografía de .NET (nada hecho a mano).</summary>
    public static (string Publica, string Privada) Generar()
    {
        using var curva = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parametros = curva.ExportParameters(includePrivateParameters: true);
        byte[] publica = [0x04, .. parametros.Q.X!, .. parametros.Q.Y!];
        return (Base64Url.EncodeToString(publica), Base64Url.EncodeToString(parametros.D!));
    }

    private static void Validar(string publica, string privada)
    {
        static byte[] Decodificar(string valor, string nombre)
        {
            try
            {
                return Base64Url.DecodeFromChars(valor);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{nombre} no está en base64url.");
            }
        }

        var bytesPublica = Decodificar(publica, "Plataforma__Push__ClavePublica");
        var bytesPrivada = Decodificar(privada, "Plataforma__Push__ClavePrivada");
        if (bytesPublica.Length != 65 || bytesPublica[0] != 0x04)
        {
            throw new InvalidOperationException("Plataforma__Push__ClavePublica debe ser un punto P-256 sin comprimir (65 bytes).");
        }
        if (bytesPrivada.Length != 32)
        {
            throw new InvalidOperationException("Plataforma__Push__ClavePrivada debe tener 32 bytes.");
        }
    }
}

internal static partial class RegistroVapid
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sin llaves VAPID configuradas: se generaron unas efímeras para este arranque ({Ambiente}).")]
    public static partial void LogEfimeras(ILogger registro, string ambiente);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web Push deshabilitado: faltan Plataforma__Push__ClavePublica y Plataforma__Push__ClavePrivada. Las notificaciones siguen llegando al centro de notificaciones.")]
    public static partial void LogDeshabilitado(ILogger registro);
}
