using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Flit.Atlas.Plataforma.Microsoft365;

namespace Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;

/// <summary>Lo que debe decir el evento de Outlook de un turno.</summary>
/// <param name="Correo">Correo del asistente (dato personal: nunca va a los logs).</param>
public sealed record EventoDeseado(
    Guid TurnoId, string Asunto, string Texto, DateOnly Fecha, TimeOnly Inicio, TimeOnly Fin, Guid PersonaId, string Correo, string Nombre);

/// <summary>Graph no aceptó la petición. El mensaje es apto para los logs: solo el código HTTP.</summary>
public sealed class ErrorGraphException(string mensaje, HttpStatusCode? estado = null, TimeSpan? reintentarEn = null) : Exception(mensaje)
{
    public HttpStatusCode? Estado { get; } = estado;

    public TimeSpan? ReintentarEn { get; } = reintentarEn;
}

/// <summary>
/// Eventos del calendario compartido «Soporte TI sábados» por Graph con el token de aplicación (ATLAS-ADR-0033):
/// <c>POST /users/{buzón}/events</c> (con <c>transactionId</c> para que un reintento no duplique), <c>PATCH</c> y
/// <c>POST …/cancel</c>. El buzón es el organizador: Exchange envía la invitación, la actualización o la cancelación a
/// cada asistente. Necesita <c>Calendars.ReadWrite</c> de aplicación, limitado a ese buzón con RBAC para aplicaciones
/// de Exchange Online. Sin SDK: <see cref="HttpClient"/> directo, como el resto de Atlas.
/// </summary>
public sealed class CalendarioGraph(IHttpClientFactory fabrica, IAccesoGraph acceso)
{
    /// <summary>Bogotá en el formato de zona de Windows que acepta Graph (UTC−5, sin horario de verano).</summary>
    public const string ZonaGraph = "SA Pacific Standard Time";

    public async Task<string> CrearAsync(string buzon, EventoDeseado evento, string transaccion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evento);
        var cuerpo = Cuerpo(evento);
        cuerpo["transactionId"] = transaccion;
        using var respuesta = await EnviarAsync(HttpMethod.Post, $"v1.0/users/{Uri.EscapeDataString(buzon)}/events?$select=id", cuerpo, ct);
        Asegurar(respuesta);
        var creado = await respuesta.Content.ReadFromJsonAsync<JsonObject>(ct);
        return creado?["id"]?.GetValue<string>() ?? throw new ErrorGraphException("Graph no devolvió el id del evento.");
    }

    /// <summary>Actualiza el evento; <c>false</c> si ya no existe (alguien lo borró del buzón): quien llama lo crea de nuevo.</summary>
    public async Task<bool> ActualizarAsync(string buzon, string id, EventoDeseado evento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evento);
        using var respuesta = await EnviarAsync(HttpMethod.Patch, Ruta(buzon, id), Cuerpo(evento), ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        Asegurar(respuesta);
        return true;
    }

    /// <summary>Cancela el evento: los asistentes reciben la cancelación. Uno que ya no existe cuenta como cancelado.</summary>
    public async Task CancelarAsync(string buzon, string id, string comentario, CancellationToken ct)
    {
        using var respuesta = await EnviarAsync(HttpMethod.Post, Ruta(buzon, id) + "/cancel", new JsonObject { ["comment"] = comentario }, ct);
        if (respuesta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return;
        }
        Asegurar(respuesta);
    }

    /// <summary>El evento como lo pide Graph: horario en Bogotá, un asistente obligatorio, sin pedir respuesta.</summary>
    internal static JsonObject Cuerpo(EventoDeseado e) => new()
    {
        ["subject"] = e.Asunto,
        ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = e.Texto },
        ["start"] = Momento(e.Fecha, e.Inicio),
        ["end"] = Momento(e.Fecha, e.Fin),
        ["attendees"] = new JsonArray
        {
            new JsonObject
            {
                ["emailAddress"] = new JsonObject { ["address"] = e.Correo, ["name"] = e.Nombre },
                ["type"] = "required",
            },
        },
        ["showAs"] = "busy",
        ["isReminderOn"] = true,
        ["reminderMinutesBeforeStart"] = 60,
        ["responseRequested"] = false,
        ["allowNewTimeProposals"] = false,
        ["isOnlineMeeting"] = false,
    };

    private static JsonObject Momento(DateOnly fecha, TimeOnly hora) => new()
    {
        ["dateTime"] = fecha.ToDateTime(hora).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        ["timeZone"] = ZonaGraph,
    };

    private static string Ruta(string buzon, string id) => $"v1.0/users/{Uri.EscapeDataString(buzon)}/events/{Uri.EscapeDataString(id)}";

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string ruta, JsonObject cuerpo, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Authorization = AuthenticationHeaderValue.Parse(await acceso.CabeceraAsync(ct));
        using var http = fabrica.CreateClient(ClienteGraph.Nombre);
        try
        {
            return await http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ErrorGraphException($"Graph no respondió ({ex.HttpRequestError}).");
        }
    }

    private static void Asegurar(HttpResponseMessage respuesta)
    {
        if (respuesta.IsSuccessStatusCode)
        {
            return;
        }
        var espera = respuesta.Headers.RetryAfter?.Delta
            ?? (respuesta.Headers.RetryAfter?.Date is { } fecha ? fecha - DateTimeOffset.UtcNow : null);
        throw new ErrorGraphException($"Graph respondió {(int)respuesta.StatusCode}.", respuesta.StatusCode, espera);
    }
}
