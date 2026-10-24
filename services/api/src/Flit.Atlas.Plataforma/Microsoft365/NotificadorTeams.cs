using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Plataforma.Microsoft365;

/// <summary>Un dato de la tarjeta («Equipo: Desarrollo TI»).</summary>
public sealed record HechoTeams(string Titulo, string Valor);

/// <summary>
/// Aviso para un canal de Teams (ATLAS-ADR-0033): título, texto, datos opcionales y un botón que abre Atlas. Va como
/// tarjeta adaptable. Puede llevar nombres de empleados (el canal es interno), nunca teléfonos ni correos.
/// </summary>
public sealed record MensajeTeams(string Titulo, string Texto, IReadOnlyList<HechoTeams>? Hechos = null, Uri? Enlace = null, string? TextoEnlace = null);

/// <summary>
/// Envío de avisos a un canal de Teams (prompt maestro §8.3 y §8.9, ATLAS-ADR-0033). Los canales tienen nombre
/// (<c>soporte</c>, …) y cada uno su URL secreta del flujo de Workflows en <c>Plataforma:Teams:Canales:&lt;nombre&gt;</c>
/// (SealedSecret). Lo usan el soporte (HU 2.10) y los recordatorios (HU 2.11).
/// </summary>
public interface INotificadorTeams
{
    /// <summary>El canal tiene una URL válida. Sin ella, quien llama no encola nada y la pantalla lo dice.</summary>
    bool Configurado(string canal);

    /// <summary>
    /// Publica el aviso. Lanza <see cref="AvisoTeamsNoEnviadoException"/> si el flujo no lo acepta o no responde (quien
    /// llama reintenta). Nunca registra la URL del canal: es la credencial.
    /// </summary>
    Task EnviarAsync(string canal, MensajeTeams mensaje, CancellationToken ct = default);
}

/// <summary>El aviso no salió. El mensaje es apto para los logs: no lleva la URL ni el contenido.</summary>
public sealed class AvisoTeamsNoEnviadoException(string mensaje, HttpStatusCode? estado = null, TimeSpan? reintentarEn = null, Exception? causa = null)
    : Exception(mensaje, causa)
{
    public HttpStatusCode? Estado { get; } = estado;

    /// <summary>Lo que pidió el servidor con <c>Retry-After</c>, si lo dijo.</summary>
    public TimeSpan? ReintentarEn { get; } = reintentarEn;
}

/// <summary>Sección <c>Plataforma:Teams</c>.</summary>
public sealed class OpcionesTeams
{
    public const string Seccion = "Plataforma:Teams";

    /// <summary>Nombre del canal → URL del disparador «Cuando se recibe una solicitud de webhook de Teams» del flujo.</summary>
    public Dictionary<string, string> Canales { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan Tiempo { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>La URL del canal si es absoluta y HTTPS; si no, <c>null</c> (el canal queda apagado).</summary>
    public Uri? UrlDe(string canal) =>
        Canales.TryGetValue(canal, out var texto) && Uri.TryCreate(texto?.Trim(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            ? url
            : null;
}

/// <summary>
/// Publica en un canal de Teams con el webhook de un flujo de Workflows (Power Automate), el reemplazo que Microsoft
/// indica para los conectores de Office 365 retirados. El cuerpo es una tarjeta adaptable 1.4 en el formato que espera
/// la plantilla «Publicar en un canal cuando se recibe una solicitud de webhook». La URL lleva la firma del flujo: es
/// un secreto.
/// </summary>
internal sealed class NotificadorTeamsWorkflows(IHttpClientFactory fabrica, IOptionsMonitor<OpcionesTeams> opciones) : INotificadorTeams
{
    public const string NombreCliente = "atlas-teams";

    public bool Configurado(string canal) => opciones.CurrentValue.UrlDe(canal) is not null;

    public async Task EnviarAsync(string canal, MensajeTeams mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        var config = opciones.CurrentValue;
        var url = config.UrlDe(canal) ?? throw new AvisoTeamsNoEnviadoException($"El canal de Teams «{canal}» no está configurado.");
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(config.Tiempo);
        HttpResponseMessage respuesta;
        try
        {
            using var http = fabrica.CreateClient(NombreCliente);
            respuesta = await http.PostAsJsonAsync(url, Tarjeta(mensaje), limite.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Sin el mensaje de la excepción: el de HttpRequestException puede traer la URL.
            throw new AvisoTeamsNoEnviadoException($"El flujo de Teams no respondió ({ex.GetType().Name}).");
        }
        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                var espera = respuesta.Headers.RetryAfter?.Delta
                    ?? (respuesta.Headers.RetryAfter?.Date is { } fecha ? fecha - DateTimeOffset.UtcNow : null);
                throw new AvisoTeamsNoEnviadoException($"El flujo de Teams respondió {(int)respuesta.StatusCode}.", respuesta.StatusCode, espera);
            }
        }
    }

    /// <summary>El cuerpo que recibe el flujo: un mensaje con una tarjeta adaptable.</summary>
    internal static JsonObject Tarjeta(MensajeTeams mensaje)
    {
        var cuerpo = new JsonArray
        {
            new JsonObject { ["type"] = "TextBlock", ["text"] = mensaje.Titulo, ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true },
            new JsonObject { ["type"] = "TextBlock", ["text"] = mensaje.Texto, ["wrap"] = true },
        };
        if (mensaje.Hechos is { Count: > 0 } hechos)
        {
            var lista = new JsonArray();
            foreach (var h in hechos)
            {
                lista.Add(new JsonObject { ["title"] = h.Titulo, ["value"] = h.Valor });
            }
            cuerpo.Add(new JsonObject { ["type"] = "FactSet", ["facts"] = lista });
        }
        var tarjeta = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = cuerpo,
        };
        if (mensaje.Enlace is { } enlace)
        {
            tarjeta["actions"] = new JsonArray
            {
                new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = mensaje.TextoEnlace ?? "Abrir en Atlas", ["url"] = enlace.ToString() },
            };
        }
        return new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray
            {
                new JsonObject { ["contentType"] = "application/vnd.microsoft.card.adaptive", ["contentUrl"] = null, ["content"] = tarjeta },
            },
        };
    }
}
