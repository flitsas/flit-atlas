using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Flit.Atlas.Plataforma.Render;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>
/// Cliente tipado del diff de atlas-render (HU 3.5, ATLAS-ADR-0036): la petición que sale, la lectura de la respuesta y
/// de la cabecera de caché, los errores por motivo y la implementación simulada para los módulos. Nada sale a la red.
/// </summary>
public class RenderDiffTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Respuesta real de atlas-render para el par base → endpoint-eliminado de services/render/testdata (ficticio).
    private const string RespuestaRender = """
        {"tipo":"openapi","motor":"oasdiff v1.33.0","idioma":"es",
         "hashBase":"dabf4626052f8fef21745c4ce48cd8b1c9160b72cf260a304c9e2d2069af97bb",
         "hashRevision":"241e652d79dc5712da689c338005d6fd25b0135367cf11c4d8315c02d9766144",
         "versionBase":"1.0.0","versionRevision":"2.0.0","sinDiferencias":false,
         "resumen":{"total":1,"errores":1,"advertencias":0,"informativos":0,"breaking":1,"hayBreaking":true},
         "cambios":[{"regla":"api-removed-without-deprecation","nivel":"error","breaking":true,"texto":"api removida sin deprecación","metodo":"DELETE","ruta":"/tramites/{id}","operationId":"anularTramite","seccion":"paths","huella":"f62ed8c4f609","fuenteBase":{"linea":63,"columna":5}}],
         "breaking":[{"regla":"api-removed-without-deprecation","nivel":"error","breaking":true,"texto":"api removida sin deprecación","metodo":"DELETE","ruta":"/tramites/{id}","operationId":"anularTramite","seccion":"paths","huella":"f62ed8c4f609","fuenteBase":{"linea":63,"columna":5}}],
         "changelog":"# Cambios del contrato 1.0.0 → 2.0.0\n"}
        """;

    private sealed class Render(HttpStatusCode estado, string cuerpo, string tipoContenido = "application/json", string? cache = null) : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, Uri Url, JsonObject Cuerpo)> Recibidas { get; } = [];

        public Exception? Falla { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Falla is not null)
            {
                throw Falla;
            }
            var json = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Recibidas.Add((request.Method, request.RequestUri!, json));
            var respuesta = new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, tipoContenido) };
            if (cache is not null)
            {
                respuesta.Headers.Add("X-Atlas-Cache", cache);
            }
            return respuesta;
        }
    }

    private sealed class Fabrica(HttpMessageHandler manejador) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(manejador, disposeHandler: false) { BaseAddress = new Uri("http://render") };
    }

    private static readonly SolicitudDiff Solicitud = new(TiposContrato.OpenApi, "openapi: 3.0.3 # base", "openapi: 3.0.3 # revisión");

    [Fact]
    public async Task Envia_las_dos_specs_y_lee_cambios_breaking_y_cache()
    {
        using var render = new Render(HttpStatusCode.OK, RespuestaRender, cache: "acierto");
        var cliente = new ClienteRenderDiff(new Fabrica(render));

        var resultado = await cliente.CalcularAsync(Solicitud, Ct);

        var (metodo, url, cuerpo) = Assert.Single(render.Recibidas);
        Assert.Equal(HttpMethod.Post, metodo);
        Assert.Equal(new Uri("http://render/internal/v1/contratos/diff"), url);
        Assert.Equal("openapi", cuerpo["tipo"]!.GetValue<string>());
        Assert.Equal(Solicitud.Base, cuerpo["base"]!.GetValue<string>());
        Assert.Equal(Solicitud.Revision, cuerpo["revision"]!.GetValue<string>());
        Assert.Equal("es", cuerpo["idioma"]!.GetValue<string>());

        Assert.True(resultado.DesdeCache);
        Assert.Equal("oasdiff v1.33.0", resultado.Motor);
        Assert.Equal(("1.0.0", "2.0.0"), (resultado.VersionBase, resultado.VersionRevision));
        Assert.True(resultado.Resumen.HayBreaking);
        var cambio = Assert.Single(resultado.Breaking);
        Assert.Equal(("api-removed-without-deprecation", NivelesCambio.Error, "DELETE", "/tramites/{id}"),
            (cambio.Regla, cambio.Nivel, cambio.Metodo, cambio.Ruta));
        Assert.Equal("anularTramite", cambio.OperationId);
        Assert.Equal(new FuenteCambio(63, 5), cambio.FuenteBase);
        Assert.Single(resultado.Cambios);
        Assert.StartsWith("# Cambios del contrato", resultado.Changelog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_cabecera_de_cache_no_marca_desde_cache()
    {
        using var render = new Render(HttpStatusCode.OK, RespuestaRender, cache: "fallo");
        var resultado = await new ClienteRenderDiff(new Fabrica(render)).CalcularAsync(Solicitud, Ct);
        Assert.False(resultado.DesdeCache);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"title":"Diff no soportado para este tipo","status":422}""", MotivoErrorDiff.NoSoportado, null)]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"title":"La spec revision no es un OpenAPI válido","status":422,"detail":"yaml: línea 3","lado":"revision"}""", MotivoErrorDiff.SpecInvalida, "revision")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, """{"title":"Las specs superan el tamaño máximo","status":413}""", MotivoErrorDiff.Rechazado, null)]
    [InlineData(HttpStatusCode.BadRequest, """{"title":"Faltan las specs","status":400}""", MotivoErrorDiff.Rechazado, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"title":"atlas-render está ocupado","status":503}""", MotivoErrorDiff.NoDisponible, null)]
    [InlineData(HttpStatusCode.GatewayTimeout, "no es json", MotivoErrorDiff.NoDisponible, null)]
    public async Task Los_errores_de_render_llegan_con_su_motivo(HttpStatusCode estado, string cuerpo, MotivoErrorDiff motivo, string? lado)
    {
        using var render = new Render(estado, cuerpo, "application/problem+json");
        var ex = await Assert.ThrowsAsync<RenderDiffException>(() => new ClienteRenderDiff(new Fabrica(render)).CalcularAsync(Solicitud, Ct));

        Assert.Equal(motivo, ex.Motivo);
        Assert.Equal((int)estado, ex.Estado);
        Assert.Equal(lado, ex.Lado);
        // El detalle del analizador no pasa al mensaje (que va a los logs).
        Assert.DoesNotContain("yaml:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_conexion_es_no_disponible()
    {
        using var render = new Render(HttpStatusCode.OK, "") { Falla = new HttpRequestException("conexión rechazada") };
        var ex = await Assert.ThrowsAsync<RenderDiffException>(() => new ClienteRenderDiff(new Fabrica(render)).CalcularAsync(Solicitud, Ct));
        Assert.Equal(MotivoErrorDiff.NoDisponible, ex.Motivo);
    }

    [Fact]
    public void Se_registra_con_la_url_de_la_configuracion()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Plataforma:Render:Url"] = "http://render.ejemplo.local:5033" })
            .Build();
        using var proveedor = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuracion)
            .AgregarRender()
            .BuildServiceProvider();

        Assert.IsType<ClienteRenderDiff>(proveedor.GetRequiredService<IRenderDiff>());
        var cliente = proveedor.GetRequiredService<IHttpClientFactory>().CreateClient(ClienteRenderDiff.NombreCliente);
        Assert.Equal(new Uri("http://render.ejemplo.local:5033"), cliente.BaseAddress);
    }

    [Fact]
    public async Task El_simulado_imita_las_reglas_de_render()
    {
        var simulado = new RenderDiffSimulado();

        var iguales = await simulado.CalcularAsync(new SolicitudDiff(TiposContrato.OpenApi, "a", "a"), Ct);
        Assert.True(iguales.SinDiferencias);
        Assert.Empty(iguales.Cambios);
        Assert.Equal(RenderDiffSimulado.Hash("a"), iguales.HashBase);
        Assert.Equal("ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb", iguales.HashBase);

        var distintos = await simulado.CalcularAsync(new SolicitudDiff(TiposContrato.OpenApi, "a", "b"), Ct);
        Assert.Single(distintos.Cambios);
        Assert.False(distintos.Resumen.HayBreaking);

        var noSoportado = await Assert.ThrowsAsync<RenderDiffException>(() => simulado.CalcularAsync(new SolicitudDiff(TiposContrato.AsyncApi, "a", "b"), Ct));
        Assert.Equal(MotivoErrorDiff.NoSoportado, noSoportado.Motivo);
        var invalida = await Assert.ThrowsAsync<RenderDiffException>(() => simulado.CalcularAsync(new SolicitudDiff(TiposContrato.OpenApi, "a", " "), Ct));
        Assert.Equal((MotivoErrorDiff.SpecInvalida, "revision"), (invalida.Motivo, invalida.Lado));

        var breaking = new CambioContrato { Regla = "api-removed-without-deprecation", Nivel = NivelesCambio.Error, Breaking = true, Texto = "api removida" };
        simulado.Programar("v1", "v2", s => new ResultadoDiff
        {
            Tipo = s.Tipo, Motor = "simulado", Idioma = s.Idioma, HashBase = "x", HashRevision = "y",
            Resumen = new ResumenDiff(1, 1, 0, 0, 1, true), Cambios = [breaking], Breaking = [breaking],
        });
        var programado = await simulado.CalcularAsync(new SolicitudDiff(TiposContrato.OpenApi, "v1", "v2"), Ct);
        Assert.Equal("api-removed-without-deprecation", Assert.Single(programado.Breaking).Regla);
        Assert.Equal(5, simulado.Recibidas.Count);
    }
}
