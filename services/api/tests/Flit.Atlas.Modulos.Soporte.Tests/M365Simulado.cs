using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Flit.Atlas.Plataforma.Microsoft365;

namespace Flit.Atlas.Pruebas.Soporte;

/// <summary>
/// Calendario de un buzón compartido en Microsoft Graph, simulado en memoria para la HU 2.10 (ATLAS-ADR-0033): crea
/// eventos (sin duplicar por <c>transactionId</c>, como Graph), los actualiza y los cancela, y registra cada llamada. Nada
/// sale a Microsoft. Datos ficticios (<c>ejemplo.com</c>).
/// </summary>
public sealed class GraphCalendarioSimulado : HttpMessageHandler
{
    public const string Token = "Bearer token-de-prueba-calendario";

    public sealed class Evento
    {
        public required string Id { get; init; }
        public required string Buzon { get; init; }
        public string? Transaccion { get; init; }
        public string? Asunto { get; set; }
        public string? Asistente { get; set; }
        public string? Inicio { get; set; }
        public string? Fin { get; set; }
        public string? Zona { get; set; }
        public bool Cancelado { get; set; }
        public int Actualizaciones { get; set; }
    }

    public ConcurrentDictionary<string, Evento> Eventos { get; } = new();

    /// <summary>Llamadas atendidas: «MÉTODO ruta».</summary>
    public ConcurrentQueue<string> Llamadas { get; } = new();

    /// <summary>Respuestas con <see cref="CodigoFalla"/> pendientes antes de volver a responder bien.</summary>
    public int FallasPendientes { get; set; }

    /// <summary>Un código que el manejador de resiliencia no reintenta, para probar los reintentos de Atlas.</summary>
    public HttpStatusCode CodigoFalla { get; set; } = HttpStatusCode.Forbidden;

    private int _siguiente;

    /// <summary>Eventos vigentes (no cancelados) con el asistente dado.</summary>
    public List<Evento> VigentesDe(string correo) =>
        [.. Eventos.Values.Where(e => !e.Cancelado && string.Equals(e.Asistente, correo, StringComparison.OrdinalIgnoreCase))];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ruta = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Llamadas.Enqueue($"{request.Method} {ruta}");
        if (request.Headers.Authorization?.ToString() != Token)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        if (FallasPendientes > 0)
        {
            FallasPendientes--;
            return new HttpResponseMessage(CodigoFalla);
        }
        var cuerpo = request.Content is null ? null : await request.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var partes = ruta.Trim('/').Split('/');
        lock (Eventos)
        {
            switch (partes)
            {
                case ["v1.0", "users", var buzon, "events"] when request.Method == HttpMethod.Post:
                {
                    var transaccion = cuerpo?["transactionId"]?.GetValue<string>();
                    var existente = Eventos.Values.FirstOrDefault(e => transaccion is not null && e.Transaccion == transaccion);
                    if (existente is not null)
                    {
                        return Json(HttpStatusCode.Created, new { id = existente.Id });
                    }
                    var evento = new Evento { Id = $"AAMk-evento-{Interlocked.Increment(ref _siguiente)}", Buzon = buzon, Transaccion = transaccion };
                    Aplicar(evento, cuerpo);
                    Eventos[evento.Id] = evento;
                    return Json(HttpStatusCode.Created, new { id = evento.Id });
                }
                case ["v1.0", "users", _, "events", var id] when request.Method == HttpMethod.Patch:
                    if (!Eventos.TryGetValue(id, out var cambiar) || cambiar.Cancelado)
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }
                    Aplicar(cambiar, cuerpo);
                    cambiar.Actualizaciones++;
                    return Json(HttpStatusCode.OK, new { id });
                case ["v1.0", "users", _, "events", var id, "cancel"] when request.Method == HttpMethod.Post:
                    if (!Eventos.TryGetValue(id, out var cancelar) || cancelar.Cancelado)
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }
                    cancelar.Cancelado = true;
                    return new HttpResponseMessage(HttpStatusCode.Accepted);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }
    }

    private static void Aplicar(Evento evento, JsonObject? cuerpo)
    {
        evento.Asunto = cuerpo?["subject"]?.GetValue<string>();
        evento.Asistente = cuerpo?["attendees"]?[0]?["emailAddress"]?["address"]?.GetValue<string>();
        evento.Inicio = cuerpo?["start"]?["dateTime"]?.GetValue<string>();
        evento.Fin = cuerpo?["end"]?["dateTime"]?.GetValue<string>();
        evento.Zona = cuerpo?["start"]?["timeZone"]?.GetValue<string>();
    }

    private static HttpResponseMessage Json(HttpStatusCode estado, object cuerpo) => new(estado) { Content = JsonContent.Create(cuerpo) };

}

/// <summary>Webhook de un flujo de Workflows de Teams simulado: guarda cada tarjeta recibida.</summary>
public sealed class TeamsSimulado : HttpMessageHandler
{
    /// <summary>URL ficticia del flujo (la real es un secreto en SealedSecret).</summary>
    public const string Url = "https://flujo-teams.ejemplo.com/workflows/soporte/disparador";

    public ConcurrentQueue<JsonObject> Tarjetas { get; } = new();

    public int FallasPendientes { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (FallasPendientes > 0)
        {
            FallasPendientes--;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }
        Assert.Equal(Url, request.RequestUri!.ToString());
        Tarjetas.Enqueue((await request.Content!.ReadFromJsonAsync<JsonObject>(cancellationToken))!);
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    }

    /// <summary>Títulos de las tarjetas recibidas.</summary>
    public List<string> Titulos() =>
        [.. Tarjetas.Select(t => t["attachments"]![0]!["content"]!["body"]![0]!["text"]!.GetValue<string>())];
}

/// <summary>
/// Manejador que reenvía a uno compartido sin descartarlo: <c>IHttpClientFactory</c> descarta los suyos al rotarlos y el
/// simulado debe vivir toda la prueba.
/// </summary>
public sealed class ReenvioA(HttpMessageHandler destino) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _invocador = new(destino, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _invocador.SendAsync(request, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _invocador.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Token de aplicación de Graph ficticio.</summary>
public sealed class AccesoGraphSimulado : IAccesoGraph
{
    public bool Disponible => true;

    public Task<string> CabeceraAsync(CancellationToken ct = default) => Task.FromResult(GraphCalendarioSimulado.Token);
}

/// <summary>Espera, con tiempo real y un tope, a que se cumpla algo que hace un trabajo en segundo plano.</summary>
public static class Esperar
{
    public static async Task HastaAsync(Func<bool> condicion, string que, TimeSpan? tope = null, Action? mientras = null)
    {
        var limite = DateTime.UtcNow + (tope ?? TimeSpan.FromSeconds(20));
        while (!condicion())
        {
            Assert.True(DateTime.UtcNow < limite, $"No pasó a tiempo: {que}");
            mientras?.Invoke();
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
