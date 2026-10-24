using System.Globalization;
using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Plataforma.Almacenamiento;

/// <summary>
/// Almacenamiento S3 privado de Atlas (prompt maestro A-06 y plan 00 §6.3): MinIO o RustFS en local y dev/qa, Contabo en
/// pdn. Sección <c>Plataforma:Almacenamiento</c>. Las credenciales llegan desde un SealedSecret en qa y pdn y desde los
/// user secrets del AppHost en local; nunca del repo.
/// </summary>
public sealed class OpcionesAlmacenamiento
{
    public const string Seccion = "Plataforma:Almacenamiento";

    /// <summary>Endpoint S3 con el que atlas-api sube, lista y borra (el de la red del clúster).</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Endpoint con el que se firman las URL de descarga: el que alcanza el navegador. La firma incluye el host, así que
    /// no puede ser el interno del clúster. Sin valor, el mismo <see cref="Endpoint"/>.
    /// </summary>
    public Uri? EndpointPublico { get; set; }

    public string? Usuario { get; set; }

    public string? Clave { get; set; }

    public string Bucket { get; set; } = "atlas";

    public string Region { get; set; } = "us-east-1";

    /// <summary>Solo en local: crea el bucket si no existe (en dev, qa y pdn lo crea infraestructura).</summary>
    public bool CrearBucket { get; set; }

    public bool Configurado =>
        Endpoint is { IsAbsoluteUri: true } && !string.IsNullOrWhiteSpace(Usuario) && !string.IsNullOrWhiteSpace(Clave)
        && !string.IsNullOrWhiteSpace(Bucket);
}

/// <summary>El almacenamiento no está configurado o no respondió.</summary>
public sealed class AlmacenNoDisponibleException(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

/// <summary>Objetos privados de Atlas: lo que suben los módulos y las URL firmadas con que el navegador los descarga.</summary>
public interface IAlmacenObjetos
{
    /// <summary>Si hay un S3 configurado. Sin él, las operaciones lanzan <see cref="AlmacenNoDisponibleException"/>.</summary>
    bool Disponible { get; }

    /// <exception cref="AlmacenNoDisponibleException"/>
    Task SubirAsync(string clave, byte[] datos, string tipo, CancellationToken ct);

    /// <summary>
    /// URL GET firmada (SigV4) que vence en <paramref name="duracion"/>: después el S3 responde 403. Pide al S3 que la
    /// respuesta llegue como adjunto con <paramref name="nombreArchivo"/>. Firmar no usa la red.
    /// </summary>
    /// <exception cref="AlmacenNoDisponibleException"/>
    Uri UrlFirmada(string clave, TimeSpan duracion, string nombreArchivo);

    /// <summary>Borra todos los objetos bajo <paramref name="prefijo"/>; devuelve cuántos borró.</summary>
    /// <exception cref="AlmacenNoDisponibleException"/>
    Task<int> BorrarPrefijoAsync(string prefijo, CancellationToken ct);
}

/// <summary>Sin S3 configurado: nada se sube y quien lo necesita responde 503.</summary>
internal sealed class SinAlmacen : IAlmacenObjetos
{
    private static AlmacenNoDisponibleException Error() =>
        new($"No hay almacenamiento S3 configurado ({OpcionesAlmacenamiento.Seccion}).");

    public bool Disponible => false;

    public Task SubirAsync(string clave, byte[] datos, string tipo, CancellationToken ct) => throw Error();

    public Uri UrlFirmada(string clave, TimeSpan duracion, string nombreArchivo) => throw Error();

    public Task<int> BorrarPrefijoAsync(string prefijo, CancellationToken ct) => throw Error();
}

/// <summary>
/// <see cref="IAlmacenObjetos"/> con el SDK de AWS para S3 (rutas por bucket, como piden MinIO, RustFS y Contabo). Dos
/// clientes: el interno sube, lista y borra; el público solo firma (no abre conexiones).
/// </summary>
internal sealed partial class AlmacenS3 : IAlmacenObjetos, IDisposable
{
    private readonly OpcionesAlmacenamiento _opciones;
    private readonly AmazonS3Client _interno;
    private readonly AmazonS3Client _publico;
    private readonly bool _publicoEsHttp;
    private readonly ILogger<AlmacenS3> _log;
    private readonly SemaphoreSlim _bucket = new(1, 1);
    private bool _bucketListo;

    public AlmacenS3(IOptions<OpcionesAlmacenamiento> opciones, ILogger<AlmacenS3> log)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _opciones = opciones.Value;
        _log = log;
        var credenciales = new BasicAWSCredentials(_opciones.Usuario, _opciones.Clave);
        var publico = _opciones.EndpointPublico ?? _opciones.Endpoint!;
        _interno = new AmazonS3Client(credenciales, Configuracion(_opciones.Endpoint!, _opciones.Region));
        _publico = new AmazonS3Client(credenciales, Configuracion(publico, _opciones.Region));
        _publicoEsHttp = publico.Scheme == Uri.UriSchemeHttp;
        _bucketListo = !_opciones.CrearBucket;
    }

    private static AmazonS3Config Configuracion(Uri endpoint, string region) => new()
    {
        ServiceURL = endpoint.ToString(),
        ForcePathStyle = true,
        AuthenticationRegion = region,
        // Los S3 compatibles no siempre aceptan las sumas CRC que el SDK 4 agrega por defecto.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        Timeout = TimeSpan.FromSeconds(30),
        MaxErrorRetry = 2,
    };

    public bool Disponible => true;

    public async Task SubirAsync(string clave, byte[] datos, string tipo, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        ArgumentNullException.ThrowIfNull(datos);
        await AsegurarBucketAsync(ct).ConfigureAwait(false);
        using var flujo = new MemoryStream(datos, writable: false);
        try
        {
            await _interno.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _opciones.Bucket,
                Key = clave,
                InputStream = flujo,
                ContentType = tipo,
                AutoCloseStream = false,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or IOException)
        {
            throw new AlmacenNoDisponibleException("El almacenamiento S3 no recibió el objeto.", ex);
        }
    }

    public Uri UrlFirmada(string clave, TimeSpan duracion, string nombreArchivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        var expira = DateTime.UtcNow + duracion;
        var url = Firmar(clave, expira, nombreArchivo);
        // El SDK calcula X-Amz-Expires contra su hora corregida por el desfase con el servidor S3 y redondea: si se pasa de
        // lo pedido (15 minutos, plan 00 §6.3), se vuelve a firmar restando el exceso.
        var exceso = SegundosFirmados(url) - (long)duracion.TotalSeconds;
        return exceso > 0 ? Firmar(clave, expira - TimeSpan.FromSeconds(exceso), nombreArchivo) : url;
    }

    private Uri Firmar(string clave, DateTime expira, string nombreArchivo)
    {
        var solicitud = new GetPreSignedUrlRequest
        {
            BucketName = _opciones.Bucket,
            Key = clave,
            Verb = HttpVerb.GET,
            Expires = expira,
            Protocol = _publicoEsHttp ? Protocol.HTTP : Protocol.HTTPS,
        };
        solicitud.ResponseHeaderOverrides.ContentDisposition = Adjunto(nombreArchivo);
        return new Uri(_publico.GetPreSignedURL(solicitud));
    }

    internal static long SegundosFirmados(Uri url)
    {
        foreach (var parte in url.Query.TrimStart('?').Split('&'))
        {
            if (parte.StartsWith("X-Amz-Expires=", StringComparison.Ordinal)
                && long.TryParse(parte["X-Amz-Expires=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var segundos))
            {
                return segundos;
            }
        }
        return 0;
    }

    public async Task<int> BorrarPrefijoAsync(string prefijo, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefijo);
        var borrados = 0;
        try
        {
            string? continuacion = null;
            do
            {
                var pagina = await _interno.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _opciones.Bucket,
                    Prefix = prefijo,
                    ContinuationToken = continuacion,
                }, ct).ConfigureAwait(false);
                var objetos = pagina.S3Objects ?? [];
                if (objetos.Count > 0)
                {
                    await _interno.DeleteObjectsAsync(new DeleteObjectsRequest
                    {
                        BucketName = _opciones.Bucket,
                        Objects = [.. objetos.Select(o => new KeyVersion { Key = o.Key })],
                        Quiet = true,
                    }, ct).ConfigureAwait(false);
                    borrados += objetos.Count;
                }
                continuacion = pagina.IsTruncated == true ? pagina.NextContinuationToken : null;
            }
            while (continuacion is not null);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Sin bucket no hay nada que borrar.
        }
        catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or IOException)
        {
            throw new AlmacenNoDisponibleException("El almacenamiento S3 no respondió al borrar.", ex);
        }
        return borrados;
    }

    private async Task AsegurarBucketAsync(CancellationToken ct)
    {
        if (_bucketListo)
        {
            return;
        }
        await _bucket.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_bucketListo)
            {
                if (!await AmazonS3Util.DoesS3BucketExistV2Async(_interno, _opciones.Bucket).ConfigureAwait(false))
                {
                    await _interno.PutBucketAsync(new PutBucketRequest { BucketName = _opciones.Bucket }, ct).ConfigureAwait(false);
                    BucketCreado(_log, _opciones.Bucket);
                }
                _bucketListo = true;
            }
        }
        finally
        {
            _bucket.Release();
        }
    }

    /// <summary>
    /// <c>attachment</c> con el nombre en ASCII (para navegadores viejos) y en UTF-8 (RFC 6266 y RFC 5987): así iOS guarda
    /// el archivo con su nombre en vez de mostrarlo en línea.
    /// </summary>
    internal static string Adjunto(string nombre)
    {
        var limpio = string.IsNullOrWhiteSpace(nombre) ? "exportacion" : nombre.Trim();
        var ascii = new StringBuilder(limpio.Length);
        foreach (var c in limpio.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            ascii.Append(c is >= ' ' and < (char)127 and not '"' and not '\\' and not ';' ? c : '_');
        }
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(limpio)}";
    }

    public void Dispose()
    {
        _interno.Dispose();
        _publico.Dispose();
        _bucket.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Se creó el bucket {Bucket} (solo en local)")]
    private static partial void BucketCreado(ILogger log, string bucket);
}

public static class ExtensionesAlmacenamiento
{
    /// <summary>
    /// <see cref="IAlmacenObjetos"/>: S3 si <c>Plataforma:Almacenamiento</c> está completo; si no, uno que responde «no
    /// disponible» (atlas-api arranca igual: un ambiente sin S3 aún no exporta, pero lo demás funciona).
    /// </summary>
    public static IServiceCollection AgregarAlmacenamiento(this IServiceCollection servicios)
    {
        servicios.AddOptions<OpcionesAlmacenamiento>().BindConfiguration(OpcionesAlmacenamiento.Seccion);
        servicios.TryAddSingleton<IAlmacenObjetos>(sp =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesAlmacenamiento>>();
            return opciones.Value.Configurado
                ? ActivatorUtilities.CreateInstance<AlmacenS3>(sp)
                : new SinAlmacen();
        });
        return servicios;
    }
}
