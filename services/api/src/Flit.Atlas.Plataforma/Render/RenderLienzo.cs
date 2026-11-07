using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Flit.Atlas.Plataforma.Render;

/// <summary>Un nodo del lienzo para exportar: el id solo une las relaciones.</summary>
/// <param name="Tipo"><c>actor</c>, <c>sistema</c>, <c>contenedor</c>, <c>componente</c> o <c>nota</c>.</param>
public sealed record NodoExporte(string Id, string Tipo, string Etiqueta);

/// <summary>Una relación del lienzo para exportar.</summary>
public sealed record RelacionExporte(string Origen, string Destino, string? Etiqueta);

/// <summary>Lo que atlas-render convierte en SVG: el modelo, nunca texto D2.</summary>
public sealed record ModeloExporte(IReadOnlyList<NodoExporte> Nodos, IReadOnlyList<RelacionExporte> Relaciones);

/// <summary>
/// Exporte de un lienzo a SVG con D2 en atlas-render (HU 6.8, ATLAS-ADR-0054 §7). El SVG llega limpio y atlas-render lo guarda en
/// caché por el hash del modelo. Lanza <see cref="RenderLienzoException"/> si no responde o rechaza el modelo.
/// </summary>
public interface IRenderLienzo
{
    Task<byte[]> SvgAsync(ModeloExporte modelo, CancellationToken ct = default);
}

/// <summary>Por qué no se pudo exportar. El mensaje no trae contenido del lienzo: se puede registrar.</summary>
public sealed class RenderLienzoException(string mensaje, bool noDisponible, int? estado = null, Exception? causa = null) : Exception(mensaje, causa)
{
    /// <summary>atlas-render no está desplegado, no responde o está ocupado; si no, rechazó el modelo.</summary>
    public bool NoDisponible { get; } = noDisponible;

    public int? Estado { get; } = estado;
}

/// <summary>
/// <see cref="IRenderLienzo"/> por HTTP. Usa el cliente de <see cref="ClienteRenderDiff"/>, con sus reintentos: exportar es
/// idempotente y, si atlas-render alcanzó a terminar, el reintento sale de su caché. No registra cuerpos.
/// </summary>
internal sealed class ClienteRenderLienzo(IHttpClientFactory fabrica) : IRenderLienzo
{
    public const string Ruta = "internal/v1/diagramas/lienzo/svg";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<byte[]> SvgAsync(ModeloExporte modelo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var cliente = fabrica.CreateClient(ClienteRenderDiff.NombreCliente);
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await cliente.PostAsJsonAsync(Ruta, modelo, Json, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RenderLienzoException("atlas-render no respondió.", noDisponible: true, causa: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new RenderLienzoException("atlas-render tardó demasiado.", noDisponible: true, causa: ex);
        }
        // El manejador de resiliencia de ServiceDefaults lanza las suyas al agotar el tiempo o abrir el circuito. Plataforma no
        // referencia Polly: se reconocen por su espacio de nombres.
        catch (Exception ex) when (ex.GetType().FullName?.StartsWith("Polly.", StringComparison.Ordinal) == true)
        {
            throw new RenderLienzoException("atlas-render no está disponible.", noDisponible: true, causa: ex);
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
            {
                return await respuesta.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }
            var estado = (int)respuesta.StatusCode;
            var rechazado = respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType;
            throw new RenderLienzoException($"atlas-render respondió {estado}.", noDisponible: !rechazado, estado);
        }
    }
}

/// <summary><see cref="IRenderLienzo"/> en memoria para las pruebas de los módulos. Guarda los modelos recibidos.</summary>
public sealed class RenderLienzoSimulado : IRenderLienzo
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<ModeloExporte> _recibidos = new();

    /// <summary>Si se define, esta excepción sale en cada llamada.</summary>
    public RenderLienzoException? Fallo { get; set; }

    public IReadOnlyCollection<ModeloExporte> Recibidos => _recibidos;

    public Task<byte[]> SvgAsync(ModeloExporte modelo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        _recibidos.Enqueue(modelo);
        return Fallo is { } fallo
            ? Task.FromException<byte[]>(fallo)
            : Task.FromResult("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray());
    }
}
