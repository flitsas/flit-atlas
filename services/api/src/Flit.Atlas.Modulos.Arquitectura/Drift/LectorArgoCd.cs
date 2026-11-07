using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Arquitectura.Drift;

/// <summary>Lo que se leyó de Argo CD. Sin datos no quiere decir sin drift: quien lo usa no compara ni resuelve nada.</summary>
/// <param name="Motivo">Código estable y sin detalles (nunca la URL ni el token).</param>
public sealed record LecturaArgoCd(ResultadoLecturaArgo Resultado, IReadOnlyList<AplicacionArgo> Aplicaciones, string? Motivo)
{
    public static LecturaArgoCd SinConfigurar { get; } = new(ResultadoLecturaArgo.SinConfigurar, [], "sin_configurar");

    public static LecturaArgoCd Fallida(string motivo) => new(ResultadoLecturaArgo.Error, [], motivo);
}

/// <summary>
/// Lectura de Argo CD (plan 01 §11.1, ATLAS-ADR-0056 §6). Es opcional: sin configuración de un clúster, responde
/// <see cref="LecturaArgoCd.SinConfigurar"/>. Otra implementación (un túnel, la API de Panel Vigía) solo tiene que cumplir esto.
/// </summary>
public interface ILectorArgoCd
{
    /// <summary>¿Hay algo configurado para este clúster? No hace ninguna llamada.</summary>
    bool Configurado(string cluster);

    /// <summary>Nunca lanza por una falla de Argo: la devuelve como <see cref="ResultadoLecturaArgo.Error"/>.</summary>
    Task<LecturaArgoCd> LeerAsync(string cluster, CancellationToken ct);
}

/// <summary>
/// <c>GET /api/v1/applications</c> con un token de solo lectura. Solo lee <c>metadata.name</c> y el <c>status</c> de resumen: ni
/// manifiestos, ni parámetros, que podrían traer secretos. <b>No se ha probado contra un Argo CD real</b> (ATLAS-ADR-0056, «Por
/// verificar»): sus pruebas usan un servidor simulado con la forma documentada de la API.
/// </summary>
internal sealed partial class LectorArgoCdHttp(IOptionsMonitor<OpcionesArquitectura> opciones, IHttpClientFactory clientes, ILogger<LectorArgoCdHttp> log) : ILectorArgoCd
{
    public const string NombreCliente = "arquitectura-argo";

    private const long TamanoMaximo = 32 * 1024 * 1024;

    private ClusterArgo? De(string cluster) => opciones.CurrentValue.Argo.FirstOrDefault(c =>
        string.Equals(c.Cluster, cluster, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(c.Url) && !string.IsNullOrWhiteSpace(c.Token));

    public bool Configurado(string cluster) => De(cluster) is not null;

    public async Task<LecturaArgoCd> LeerAsync(string cluster, CancellationToken ct)
    {
        if (De(cluster) is not { } destino)
        {
            return LecturaArgoCd.SinConfigurar;
        }
        if (!Uri.TryCreate(destino.Url.TrimEnd('/') + "/api/v1/applications", UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback))
        {
            // Un token de Argo no viaja por http en claro.
            return LecturaArgoCd.Fallida("url_invalida");
        }
        try
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", destino.Token);
            peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var cliente = clientes.CreateClient(NombreCliente);
            using var respuesta = await cliente.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
            {
                Fallo(log, cluster, (int)respuesta.StatusCode);
                return LecturaArgoCd.Fallida($"http_{(int)respuesta.StatusCode}");
            }
            if (respuesta.Content.Headers.ContentLength > TamanoMaximo)
            {
                return LecturaArgoCd.Fallida("respuesta_invalida");
            }
            await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var documento = await JsonDocument.ParseAsync(flujo, cancellationToken: ct).ConfigureAwait(false);
            return new LecturaArgoCd(ResultadoLecturaArgo.Correcta, Leer(documento.RootElement), null);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            // Solo el tipo: el mensaje puede traer la URL interna.
            FalloRed(log, cluster, error.GetType().Name);
            return LecturaArgoCd.Fallida(error is JsonException or InvalidOperationException ? "respuesta_invalida" : "sin_respuesta");
        }
    }

    /// <summary>Una lista vacía o sin <c>items</c> es una respuesta válida; lo que no es una lista, no.</summary>
    internal static List<AplicacionArgo> Leer(JsonElement raiz)
    {
        var aplicaciones = new List<AplicacionArgo>();
        if (!raiz.TryGetProperty("items", out var items) || items.ValueKind == JsonValueKind.Null)
        {
            return aplicaciones;
        }
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("metadata", out var metadata) || !metadata.TryGetProperty("name", out var nombre) || nombre.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var estado = item.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
            aplicaciones.Add(new AplicacionArgo(
                nombre.GetString()!,
                Anidado(estado, "sync", "status"),
                Anidado(estado, "health", "status"),
                estado.ValueKind == JsonValueKind.Object && estado.TryGetProperty("reconciledAt", out var r) && r.ValueKind == JsonValueKind.String && r.TryGetDateTimeOffset(out var fecha) ? fecha : null,
                Imagenes(estado)));
        }
        return aplicaciones;
    }

    private static string? Anidado(JsonElement estado, string seccion, string campo) =>
        estado.ValueKind == JsonValueKind.Object && estado.TryGetProperty(seccion, out var s) && s.ValueKind == JsonValueKind.Object
            && s.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Imagenes(JsonElement estado) =>
        estado.ValueKind == JsonValueKind.Object && estado.TryGetProperty("summary", out var resumen) && resumen.ValueKind == JsonValueKind.Object
            && resumen.TryGetProperty("images", out var imagenes) && imagenes.ValueKind == JsonValueKind.Array
            ? [.. imagenes.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!)]
            : [];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Argo CD del clúster {Cluster} respondió {Estado}")]
    private static partial void Fallo(ILogger log, string cluster, int estado);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Argo CD del clúster {Cluster} no se pudo leer: {Tipo}")]
    private static partial void FalloRed(ILogger log, string cluster, string tipo);
}
