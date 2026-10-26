using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Plataforma.Correo;

namespace Flit.Atlas.Api.Tests;

/// <summary>Buzón en memoria que reemplaza el envío de correo en las pruebas (HU 1.5).</summary>
public sealed partial class BuzonDePrueba : IEnviadorCorreo
{
    private readonly ConcurrentQueue<MensajeCorreo> _mensajes = new();

    public bool Habilitado => true;

    /// <summary>Si es <c>true</c>, el próximo envío falla como si el servidor no respondiera.</summary>
    public bool Fallar { get; set; }

    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default)
    {
        if (Fallar)
        {
            throw new CorreoNoEnviadoException("Servidor de prueba caído.");
        }
        _mensajes.Enqueue(mensaje);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Correos de la identidad que aún están en la cola en memoria (ATLAS-ADR-0049: la recuperación y los avisos salen fuera
    /// de la petición). <see cref="Para"/> espera a que se vacíe, así las pruebas leen el buzón justo después de la petición.
    /// </summary>
    public Func<int>? Pendientes { get; set; }

    public IReadOnlyList<MensajeCorreo> Para(string correo)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (Pendientes?.Invoke() > 0 && DateTime.UtcNow < limite)
        {
            Thread.Sleep(10);
        }
        return [.. _mensajes.Where(m => m.Para == correo)];
    }

    /// <summary>El fragmento del último enlace (<c>/invitacion#…</c> o <c>/recuperar#…</c>) enviado a <paramref name="correo"/>.</summary>
    public string UltimoEnlace(string correo, string ruta)
    {
        var lista = Para(correo);
        var mensaje = lista.Count > 0 ? lista[^1] : throw new InvalidOperationException($"No hay correos para {correo}.");
        var coincidencia = Regex.Match(mensaje.Texto, Regex.Escape(ruta) + "#([A-Za-z0-9_.\\-]+)");
        Assert.True(coincidencia.Success, "El correo no trae el enlace " + ruta);
        return coincidencia.Groups[1].Value;
    }
}

/// <summary>Códigos TOTP (RFC 6238, SHA-1, 30 s, 6 dígitos) para las pruebas; el servidor usa el de Identity.</summary>
public static class Totp
{
    public static string Codigo(string claveBase32, DateTimeOffset? momento = null, int desfasePasos = 0)
    {
        var clave = Base32(claveBase32.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var paso = ((momento ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30) + desfasePasos;
        Span<byte> contador = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(contador, paso);
#pragma warning disable CA5350 // RFC 6238 usa HMAC-SHA1.
        var hash = HMACSHA1.HashData(clave, contador);
#pragma warning restore CA5350
        var desplazamiento = hash[^1] & 0x0f;
        var binario = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(desplazamiento)) & 0x7fffffff) % 1_000_000;
        return binario.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string texto)
    {
        const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var valor = 0;
        var salida = new List<byte>();
        foreach (var c in texto.TrimEnd('='))
        {
            valor = (valor << 5) | Alfabeto.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                salida.Add((byte)((valor >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return [.. salida];
    }
}

/// <summary>
/// Autenticador WebAuthn de software para las pruebas del servidor (ES256, attestation «none», verificación de usuario):
/// arma las respuestas de <c>navigator.credentials.create/get</c> que verifica el <c>PasskeyHandler</c> de Identity.
/// </summary>
public sealed class AutenticadorDePrueba(string origen = "https://localhost") : IDisposable
{
    private readonly ECDsa _llave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credencial = RandomNumberGenerator.GetBytes(32);
    private uint _firmas;
    private string? _usuario;

    public string Origen => origen;

    /// <summary>Respuesta de <c>create()</c> para las opciones de registro.</summary>
    public string Registrar(string opcionesJson)
    {
        using var opciones = JsonDocument.Parse(opcionesJson);
        var raiz = opciones.RootElement;
        var rpId = raiz.GetProperty("rp").GetProperty("id").GetString()!;
        _usuario = raiz.GetProperty("user").GetProperty("id").GetString();
        var clientData = ClientData("webauthn.create", raiz.GetProperty("challenge").GetString()!);

        var parametros = _llave.ExportParameters(false);
        var cose = Cbor.Mapa(
            (Cbor.Entero(1), Cbor.Entero(2)),
            (Cbor.Entero(3), Cbor.Entero(-7)),
            (Cbor.Entero(-1), Cbor.Entero(1)),
            (Cbor.Entero(-2), Cbor.Bytes(parametros.Q.X!)),
            (Cbor.Entero(-3), Cbor.Bytes(parametros.Q.Y!)));
        var datos = new List<byte>();
        datos.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        datos.Add(0x45); // UP | UV | AT
        datos.AddRange(ContadorBe(_firmas));
        datos.AddRange(new byte[16]);
        datos.Add((byte)(_credencial.Length >> 8));
        datos.Add((byte)_credencial.Length);
        datos.AddRange(_credencial);
        datos.AddRange(cose);
        var atestacion = Cbor.Mapa(
            (Cbor.Texto("fmt"), Cbor.Texto("none")),
            (Cbor.Texto("attStmt"), Cbor.Mapa()),
            (Cbor.Texto("authData"), Cbor.Bytes([.. datos])));

        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(_credencial),
            rawId = Base64Url.EncodeToString(_credencial),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientData),
                attestationObject = Base64Url.EncodeToString(atestacion),
                transports = new[] { "internal" },
            },
        });
    }

    /// <summary>Respuesta de <c>get()</c> para las opciones de ingreso.</summary>
    public string Firmar(string opcionesJson)
    {
        using var opciones = JsonDocument.Parse(opcionesJson);
        var raiz = opciones.RootElement;
        var rpId = raiz.GetProperty("rpId").GetString()!;
        var clientData = ClientData("webauthn.get", raiz.GetProperty("challenge").GetString()!);
        _firmas++;
        var datos = new List<byte>();
        datos.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        datos.Add(0x05); // UP | UV
        datos.AddRange(ContadorBe(_firmas));
        var firmado = datos.Concat(SHA256.HashData(clientData)).ToArray();
        var firma = _llave.SignData(firmado, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(_credencial),
            rawId = Base64Url.EncodeToString(_credencial),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientData),
                authenticatorData = Base64Url.EncodeToString([.. datos]),
                signature = Base64Url.EncodeToString(firma),
                userHandle = _usuario,
            },
        });
    }

    private byte[] ClientData(string tipo, string desafio) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type = tipo, challenge = desafio, origin = origen, crossOrigin = false });

    private static byte[] ContadorBe(uint valor)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, valor);
        return bytes;
    }

    public void Dispose() => _llave.Dispose();

    /// <summary>Lo mínimo de CBOR (RFC 8949) para la atestación y la llave COSE.</summary>
    private static class Cbor
    {
        public static byte[] Entero(long valor) => valor >= 0 ? Cabecera(0, (ulong)valor) : Cabecera(1, (ulong)(-1 - valor));

        public static byte[] Bytes(byte[] valor) => [.. Cabecera(2, (ulong)valor.Length), .. valor];

        public static byte[] Texto(string valor)
        {
            var bytes = Encoding.UTF8.GetBytes(valor);
            return [.. Cabecera(3, (ulong)bytes.Length), .. bytes];
        }

        public static byte[] Mapa(params (byte[] Clave, byte[] Valor)[] pares) =>
            [.. Cabecera(5, (ulong)pares.Length), .. pares.SelectMany(p => p.Clave.Concat(p.Valor))];

        private static byte[] Cabecera(byte tipo, ulong valor) => valor switch
        {
            < 24 => [(byte)((tipo << 5) | (byte)valor)],
            <= byte.MaxValue => [(byte)((tipo << 5) | 24), (byte)valor],
            <= ushort.MaxValue => [(byte)((tipo << 5) | 25), (byte)(valor >> 8), (byte)valor],
            _ => throw new ArgumentOutOfRangeException(nameof(valor)),
        };
    }
}
