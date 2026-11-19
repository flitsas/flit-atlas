using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Plataforma.Render;

/// <summary>
/// <see cref="IRenderDiff"/> por HTTP hacia atlas-render (<c>POST /internal/v1/contratos/diff</c>). La dirección sale del
/// descubrimiento de servicios de Aspire (<c>http://render</c>) y los reintentos ante 5xx o tiempos vencidos los pone el
/// manejador de resiliencia de ServiceDefaults: el diff es idempotente y, si atlas-render alcanzó a terminarlo, el
/// reintento sale de su caché. El cliente no registra cuerpos: solo método, URL y código.
/// </summary>
internal sealed class ClienteRenderDiff(IHttpClientFactory fabrica) : IRenderDiff
{
    public const string NombreCliente = "atlas-render";

    public const string Ruta = "internal/v1/contratos/diff";

    private const string EncabezadoCache = "X-Atlas-Cache";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoDiff> CalcularAsync(SolicitudDiff solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var cliente = fabrica.CreateClient(NombreCliente);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await cliente.PostAsJsonAsync(Ruta, solicitud, Json, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RenderDiffException("atlas-render no respondió.", MotivoErrorDiff.NoDisponible, causa: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new RenderDiffException("atlas-render tardó demasiado.", MotivoErrorDiff.NoDisponible, causa: ex);
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
            {
                var resultado = await respuesta.Content.ReadFromJsonAsync<ResultadoDiff>(Json, ct).ConfigureAwait(false)
                    ?? throw new RenderDiffException("atlas-render respondió vacío.", MotivoErrorDiff.NoDisponible, (int)respuesta.StatusCode);
                var desdeCache = respuesta.Headers.TryGetValues(EncabezadoCache, out var valores)
                    && valores.Contains("acierto", StringComparer.OrdinalIgnoreCase);
                return resultado with { DesdeCache = desdeCache };
            }

            var problema = await LeerProblemaAsync(respuesta, ct).ConfigureAwait(false);
            var estado = (int)respuesta.StatusCode;
            var titulo = problema?.Title ?? $"atlas-render respondió {estado}.";
            throw respuesta.StatusCode switch
            {
                HttpStatusCode.UnprocessableEntity when !string.IsNullOrEmpty(problema?.Lado) =>
                    new RenderDiffException(titulo, MotivoErrorDiff.SpecInvalida, estado, problema.Lado),
                HttpStatusCode.UnprocessableEntity => new RenderDiffException(titulo, MotivoErrorDiff.NoSoportado, estado),
                HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType =>
                    new RenderDiffException(titulo, MotivoErrorDiff.Rechazado, estado),
                _ => new RenderDiffException(titulo, MotivoErrorDiff.NoDisponible, estado),
            };
        }
    }

    // El detalle del problema puede citar el error del analizador de la spec: no se copia al mensaje de la excepción
    // (que sí va a los logs); solo el título y el lado.
    private static async Task<Problema?> LeerProblemaAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            return await respuesta.Content.ReadFromJsonAsync<Problema>(Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record Problema(string? Title, int? Status, string? Detail, string? Lado);
}

/// <summary>Registro del cliente de atlas-render.</summary>
public static class ExtensionesRender
{
    /// <summary>
    /// Registra <see cref="IRenderDiff"/> con la implementación HTTP. Las pruebas pueden reemplazarla antes o después con
    /// <see cref="RenderDiffSimulado"/> (se usa <c>TryAdd</c>).
    /// </summary>
    public static IServiceCollection AgregarRender(this IServiceCollection servicios)
    {
        servicios.AddOptions<OpcionesRender>().BindConfiguration(OpcionesRender.Seccion);
        servicios.AddHttpClient(ClienteRenderDiff.NombreCliente, (sp, c) =>
            c.BaseAddress = sp.GetRequiredService<IOptions<OpcionesRender>>().Value.Url);
        servicios.TryAddSingleton<IRenderDiff, ClienteRenderDiff>();
        return servicios;
    }
}
