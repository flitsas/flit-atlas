using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Flit.Atlas.Plataforma.Render;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>Cliente del exporte de lienzos de atlas-render (HU 6.8, ATLAS-ADR-0054 §7). Nada sale a la red.</summary>
public class RenderLienzoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ModeloExporte Modelo = new(
        [new NodoExporte("a", "sistema", "Trámites"), new NodoExporte("b", "actor", "Radicador")],
        [new RelacionExporte("b", "a", null)]);

    private sealed class Render(HttpStatusCode estado, string cuerpo = "") : HttpMessageHandler
    {
        public Exception? Falla { get; init; }

        public Uri? Url { get; private set; }

        public JsonObject? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Falla is not null)
            {
                throw Falla;
            }
            Url = request.RequestUri;
            Cuerpo = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "image/svg+xml") };
        }
    }

    private sealed class Fabrica(HttpMessageHandler manejador) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(manejador, disposeHandler: false) { BaseAddress = new Uri("http://render") };
    }

    [Fact]
    public async Task Envia_el_modelo_y_devuelve_el_svg()
    {
        using var render = new Render(HttpStatusCode.OK, "<svg></svg>");

        var svg = await new ClienteRenderLienzo(new Fabrica(render)).SvgAsync(Modelo, Ct);

        Assert.Equal("<svg></svg>", Encoding.UTF8.GetString(svg));
        Assert.Equal(new Uri("http://render/internal/v1/diagramas/lienzo/svg"), render.Url);
        Assert.Equal("Trámites", render.Cuerpo!["nodos"]![0]!["etiqueta"]!.GetValue<string>());
        Assert.Equal("b", render.Cuerpo["relaciones"]![0]!["origen"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    public async Task Clasifica_las_respuestas_de_error(HttpStatusCode estado, bool noDisponible)
    {
        using var render = new Render(estado);

        var error = await Assert.ThrowsAsync<RenderLienzoException>(() => new ClienteRenderLienzo(new Fabrica(render)).SvgAsync(Modelo, Ct));

        Assert.Equal(noDisponible, error.NoDisponible);
        Assert.Equal((int)estado, error.Estado);
    }

    [Fact]
    public async Task Sin_atlas_render_desplegado_queda_no_disponible()
    {
        using var render = new Render(HttpStatusCode.OK) { Falla = new HttpRequestException("Sin conexión") };

        var error = await Assert.ThrowsAsync<RenderLienzoException>(() => new ClienteRenderLienzo(new Fabrica(render)).SvgAsync(Modelo, Ct));

        Assert.True(error.NoDisponible);
    }
}
