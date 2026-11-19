using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Flit.Atlas.Plataforma.Microsoft365;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>
/// Aviso en un canal de Teams por el webhook de un flujo de Workflows (HU 2.10, ATLAS-ADR-0033): la tarjeta adaptable, los
/// canales sin URL o sin HTTPS apagados y los errores sin la URL (es la credencial). Nada sale a Microsoft.
/// </summary>
public class NotificadorTeamsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Url = "https://flujo-teams.ejemplo.com/workflows/soporte/disparador?firma=ficticia";

    private sealed class Flujo(HttpStatusCode estado) : HttpMessageHandler
    {
        public List<(Uri Url, JsonObject Cuerpo)> Recibidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibidos.Add((request.RequestUri!, (await request.Content!.ReadFromJsonAsync<JsonObject>(cancellationToken))!));
            var respuesta = new HttpResponseMessage(estado);
            if (estado == HttpStatusCode.TooManyRequests)
            {
                respuesta.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            }
            return respuesta;
        }
    }

    private sealed class Fabrica(HttpMessageHandler manejador) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(manejador, disposeHandler: false);
    }

    private static NotificadorTeamsWorkflows Notificador(Flujo flujo, params (string Canal, string Url)[] canales)
    {
        var opciones = new OpcionesTeams();
        foreach (var (canal, url) in canales)
        {
            opciones.Canales[canal] = url;
        }
        return new NotificadorTeamsWorkflows(new Fabrica(flujo), new Monitor(opciones));
    }

    private sealed class Monitor(OpcionesTeams valor) : IOptionsMonitor<OpcionesTeams>
    {
        public OpcionesTeams CurrentValue => valor;

        public OpcionesTeams Get(string? name) => valor;

        public IDisposable? OnChange(Action<OpcionesTeams, string?> listener) => null;
    }

    [Fact]
    public async Task Publica_una_tarjeta_adaptable_con_datos_y_boton()
    {
        using var flujo = new Flujo(HttpStatusCode.Accepted);
        var teams = Notificador(flujo, ("soporte", Url), ("otro", "http://inseguro.ejemplo.com/x"), ("roto", "no es una url"));

        Assert.True(teams.Configurado("soporte"));
        Assert.True(teams.Configurado("SOPORTE"));
        Assert.False(teams.Configurado("otro"));
        Assert.False(teams.Configurado("roto"));
        Assert.False(teams.Configurado("nadie"));

        await teams.EnviarAsync("soporte", new MensajeTeams("Cambio de turno aprobado", "Pasa de Ana Demo a Bruno Demo.",
            [new HechoTeams("Equipo", "Desarrollo TI")], new Uri("https://atlas.ejemplo.com/soporte"), "Ver el calendario"), Ct);

        var (destino, cuerpo) = Assert.Single(flujo.Recibidos);
        Assert.Equal(new Uri(Url), destino);
        Assert.Equal("message", cuerpo["type"]!.GetValue<string>());
        var adjunto = cuerpo["attachments"]![0]!;
        Assert.Equal("application/vnd.microsoft.card.adaptive", adjunto["contentType"]!.GetValue<string>());
        var tarjeta = adjunto["content"]!;
        Assert.Equal(("AdaptiveCard", "1.4"), (tarjeta["type"]!.GetValue<string>(), tarjeta["version"]!.GetValue<string>()));
        Assert.Equal("Cambio de turno aprobado", tarjeta["body"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("Desarrollo TI", tarjeta["body"]![2]!["facts"]![0]!["value"]!.GetValue<string>());
        Assert.Equal(("Action.OpenUrl", "https://atlas.ejemplo.com/soporte"),
            (tarjeta["actions"]![0]!["type"]!.GetValue<string>(), tarjeta["actions"]![0]!["url"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Un_rechazo_lanza_sin_la_url_y_con_el_retry_after()
    {
        using var flujo = new Flujo(HttpStatusCode.TooManyRequests);
        var teams = Notificador(flujo, ("soporte", Url));

        var error = await Assert.ThrowsAsync<AvisoTeamsNoEnviadoException>(() => teams.EnviarAsync("soporte", new MensajeTeams("t", "x"), Ct));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.Estado);
        Assert.Equal(TimeSpan.FromSeconds(90), error.ReintentarEn);
        Assert.DoesNotContain("ejemplo.com", error.Message, StringComparison.Ordinal);

        var sinCanal = await Assert.ThrowsAsync<AvisoTeamsNoEnviadoException>(() => teams.EnviarAsync("nadie", new MensajeTeams("t", "x"), Ct));
        Assert.Contains("no está configurado", sinCanal.Message, StringComparison.Ordinal);
    }
}
