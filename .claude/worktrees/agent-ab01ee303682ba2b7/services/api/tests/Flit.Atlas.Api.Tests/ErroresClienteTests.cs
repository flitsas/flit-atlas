using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Flit.Atlas.Identidad.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// Reporte de errores de la PWA (<c>POST /api/v1/plataforma/errores-cliente</c>): público, con límite de tasa y tope
/// de tamaño, y sin datos personales en el log.
/// </summary>
public class ErroresClienteTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IpNueva() => $"2001:db8::{Random.Shared.Next(1, 0xffff):x}:{Random.Shared.Next(1, 0xffff):x}";

    private static object Reporte(string referencia = "ABCD2345", string? mensaje = null, string? ruta = "/administracion/roles") => new
    {
        referencia,
        nombre = "TypeError",
        mensaje = mensaje ?? "Cannot read properties of undefined (reading 'nombre')",
        pila = "TypeError: x\n    at Roles (https://localhost/assets/Roles-Bx12cd.js:12:3456)",
        ruta,
        version = "0.1.0+abc1234",
    };

    [Fact]
    public async Task Sin_sesion_registra_una_advertencia_con_la_referencia_y_sin_datos_personales()
    {
        var logs = new CapturaDeLogs();
        await using var base_ = entorno.Api();
        await using var api = base_.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();

        var respuesta = await navegador.EnviarAsync(HttpMethod.Post, EndpointsErroresCliente.Ruta, JsonContent.Create(Reporte(
            mensaje: "No se encontró a usuaria.demo@ejemplo.com con cédula 1012345678 y token eyJhbGciOiJIUzI1NiJ9abcdefghijklmnopqrstuvwxyz0123",
            ruta: "/invitacion?x=1#token-de-un-solo-uso")));

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<EndpointsErroresCliente.ErrorClienteRegistrado>(Ct);
        Assert.Equal("ABCD2345", cuerpo!.Referencia);
        var entrada = Assert.Single(logs.Entradas, e => e.Categoria == "Flit.Atlas.ErroresCliente");
        Assert.Equal(LogLevel.Warning, entrada.Nivel);
        Assert.Contains("ABCD2345", entrada.Texto, StringComparison.Ordinal);
        Assert.Contains("TypeError", entrada.Texto, StringComparison.Ordinal);
        Assert.Contains("ruta /invitacion,", entrada.Texto, StringComparison.Ordinal);
        Assert.Contains("versión 0.1.0+abc1234", entrada.Texto, StringComparison.Ordinal);
        // Línea y columna de la pila se conservan (sirven con el mapa de fuentes).
        Assert.Contains(":12:3456", entrada.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("ejemplo.com", entrada.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("1012345678", entrada.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", entrada.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("token-de-un-solo-uso", entrada.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("x=1", entrada.Texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_referencia_invalida_se_reemplaza_por_una_del_servidor()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();

        var respuesta = await navegador.EnviarAsync(HttpMethod.Post, EndpointsErroresCliente.Ruta,
            JsonContent.Create(Reporte(referencia: "<script>alert(1)</script>")));

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<EndpointsErroresCliente.ErrorClienteRegistrado>(Ct);
        Assert.Matches("^[2-9A-HJKMNP-Z]{8}$", cuerpo!.Referencia);
    }

    [Fact]
    public async Task Rechaza_cuerpos_grandes_o_invalidos_y_exige_la_cabecera_antifalsificacion()
    {
        var logs = new CapturaDeLogs();
        await using var base_ = entorno.Api();
        await using var api = base_.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();

        var grande = await navegador.EnviarAsync(HttpMethod.Post, EndpointsErroresCliente.Ruta,
            JsonContent.Create(Reporte(mensaje: new string('x', EndpointsErroresCliente.TamanoMaximo))));
        var invalido = await navegador.EnviarAsync(HttpMethod.Post, EndpointsErroresCliente.Ruta,
            new StringContent("{no es json", Encoding.UTF8, "application/json"));
        var sinCsrf = await navegador.EnviarAsync(HttpMethod.Post, EndpointsErroresCliente.Ruta, JsonContent.Create(Reporte()), csrf: false);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sinCsrf.StatusCode);
        Assert.DoesNotContain(logs.Entradas, e => e.Categoria == "Flit.Atlas.ErroresCliente");
    }

    [Fact]
    public async Task Tiene_limite_de_tasa_por_ip()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string> { [$"{OpcionesLimiteTasa.Seccion}:ErroresClientePorIp"] = "2" });
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();

        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            estados.Add((await navegador.EnviarAsync(HttpMethod.Post, EndpointsErroresCliente.Ruta, JsonContent.Create(Reporte()))).StatusCode);
        }
        using var otraIp = Navegador.De(api);
        otraIp.Ip = IpNueva();
        var deOtraIp = await otraIp.EnviarAsync(HttpMethod.Post, EndpointsErroresCliente.Ruta, JsonContent.Create(Reporte()));

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], estados);
        Assert.Equal(HttpStatusCode.Accepted, deOtraIp.StatusCode);
    }

    [Theory]
    [InlineData("/administracion/roles", "/administracion/roles")]
    [InlineData("/invitacion#abc", "/invitacion")]
    [InlineData("/buscar?q=usuaria.demo@ejemplo.com", "/buscar")]
    [InlineData("https://ejemplo.com/x", null)]
    [InlineData("//ejemplo.com/x", null)]
    [InlineData("", null)]
    public void La_ruta_queda_sin_host_consulta_ni_fragmento(string ruta, string? esperada) =>
        Assert.Equal(esperada, EndpointsErroresCliente.LimpiarRuta(ruta));

    [Fact]
    public void Limpiar_recorta_quita_controles_y_tacha_lo_personal()
    {
        Assert.Equal(10, EndpointsErroresCliente.Limpiar(new string('a', 50), 10)!.Length);
        Assert.Equal("una línea más", EndpointsErroresCliente.Limpiar("una\nlínea\u0007 más", 100)!.Replace("  ", " ", StringComparison.Ordinal));
        Assert.Equal("a\nb", EndpointsErroresCliente.Limpiar("a\r\nb", 100, multilinea: true)!.Replace(" ", string.Empty, StringComparison.Ordinal));
        Assert.Equal("llamar al [tachado]", EndpointsErroresCliente.Limpiar("llamar al 3001234567", 100));
        Assert.Equal("de [tachado]", EndpointsErroresCliente.Limpiar("de alguien@ejemplo.com", 100));
        Assert.Equal("index.js:123456:78", EndpointsErroresCliente.Limpiar("index.js:123456:78", 100));
        Assert.Null(EndpointsErroresCliente.Limpiar("   ", 100));
    }

    private sealed record Entrada(string Categoria, LogLevel Nivel, string Texto);

    /// <summary>Guarda lo que se escribe en el log para revisar nivel, valores y que no haya datos personales.</summary>
    private sealed class CapturaDeLogs : ILoggerProvider
    {
        public ConcurrentQueue<Entrada> Entradas { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Registrador(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Registrador(CapturaDeLogs captura, string categoria) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var valores = state is IEnumerable<KeyValuePair<string, object?>> pares ? string.Join(" ", pares.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                captura.Entradas.Enqueue(new Entrada(categoria, logLevel, $"{formatter(state, exception)} {valores}"));
            }
        }
    }
}
