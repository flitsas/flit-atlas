using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 4.10 (ATLAS-ADR-0048): cabeceras de seguridad de atlas-api, informes de violaciones de la CSP sin datos personales
/// y límites de tasa del resto de la API (por persona, más estricto para externos, por organización en el portal y por
/// IP sin sesión). Los de las exportaciones están en <c>ExportacionesTests</c>.
/// </summary>
public sealed class EndurecimientoTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IpNueva() => $"2001:db8::48:{Random.Shared.Next(1, 0xffff):x}";

    private static string CorreoNuevo() => $"endurecimiento.{Guid.NewGuid():N}@ejemplo.com";

    // --- Cabeceras ---

    [Theory]
    [InlineData("/api/v1/version")]
    [InlineData("/api/v1/plataforma/yo")]
    [InlineData("/health/live")]
    [InlineData("/no-existe")]
    public async Task Toda_respuesta_de_la_API_trae_las_cabeceras_de_seguridad(string ruta)
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();

        var respuesta = await navegador.GetAsync(ruta);

        string Cabecera(string nombre) => Assert.Single(respuesta.Headers.GetValues(nombre));
        var csp = Cabecera("Content-Security-Policy");
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", csp, StringComparison.Ordinal);
        Assert.Contains($"report-uri {EndpointsReportesCsp.Ruta}", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.Equal($"csp=\"{EndpointsReportesCsp.Ruta}\"", Cabecera("Reporting-Endpoints"));
        Assert.Equal("nosniff", Cabecera("X-Content-Type-Options"));
        Assert.Equal("no-referrer", Cabecera("Referrer-Policy"));
        Assert.Contains("microphone=(self)", Cabecera("Permissions-Policy"), StringComparison.Ordinal);
        Assert.Contains("camera=()", Cabecera("Permissions-Policy"), StringComparison.Ordinal);
        Assert.Equal("same-origin", Cabecera("Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", Cabecera("Cross-Origin-Resource-Policy"));
        // En local no hay HSTS (la API corre en http detrás de Vite).
        Assert.False(respuesta.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public void HSTS_solo_fuera_de_local_y_en_HTTPS_y_una_cabecera_propia_se_conserva()
    {
        static DefaultHttpContext Contexto(bool https)
        {
            var http = new DefaultHttpContext();
            http.Request.Scheme = https ? "https" : "http";
            return http;
        }

        var qa = Contexto(https: true);
        var qaHttp = Contexto(https: false);
        var local = Contexto(https: true);
        var propia = Contexto(https: true);
        propia.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

        CabecerasSeguridad.Aplicar(qa, conHsts: true);
        CabecerasSeguridad.Aplicar(qaHttp, conHsts: true);
        CabecerasSeguridad.Aplicar(local, conHsts: false);
        CabecerasSeguridad.Aplicar(propia, conHsts: true);

        Assert.Equal("max-age=31536000; includeSubDomains", qa.Response.Headers.StrictTransportSecurity.ToString());
        Assert.False(qaHttp.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.False(local.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", propia.Response.Headers.ContentSecurityPolicy.ToString());
    }

    // --- Informes de la CSP ---

    private static StringContent Cuerpo(string json, string tipo) => new(json, Encoding.UTF8, new MediaTypeHeaderValue(tipo));

    private const string InformeReportUri = """
        {"csp-report":{"document-uri":"https://qa.atlas.flitsas.com/portal/paquetes/1?correo=ana@ejemplo.com#token-secreto",
        "referrer":"https://qa.atlas.flitsas.com/","violated-directive":"script-src-elem","effective-directive":"script-src-elem",
        "original-policy":"default-src 'self'","disposition":"enforce","blocked-uri":"https://cdn.ejemplo.com/x.js?clave=123",
        "line-number":12,"column-number":7,"source-file":"https://qa.atlas.flitsas.com/assets/index-abc.js?v=1",
        "status-code":200,"script-sample":"alert('ana@ejemplo.com')"}}
        """;

    [Fact]
    public async Task Un_informe_de_la_CSP_se_registra_sin_sesion_ni_cabecera_antifalsificacion_y_sin_datos_personales()
    {
        var registros = new RegistrosDePrueba();
        await using var api = entorno.Api(servicios: s => s.AddSingleton<ILoggerProvider>(registros));
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();

        var reportUri = await navegador.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta, Cuerpo(InformeReportUri, "application/csp-report"), csrf: false, origen: "null");
        var reportingApi = await navegador.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta, Cuerpo("""
            [{"type":"csp-violation","age":1,"url":"https://qa.atlas.flitsas.com/contratos","user_agent":"Navegador Demo",
              "body":{"documentURL":"https://qa.atlas.flitsas.com/contratos","effectiveDirective":"require-trusted-types-for",
              "blockedURL":"trusted-types-sink","disposition":"report","sourceFile":"https://qa.atlas.flitsas.com/sw-registro.js","lineNumber":3,"columnNumber":1,"sample":"x"}},
             {"type":"deprecation","body":{}}]
            """, "application/reports+json"), csrf: false);

        Assert.Equal(HttpStatusCode.NoContent, reportUri.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reportingApi.StatusCode);
        var bloqueada = Assert.Single(registros.Mensajes, m => m.Nivel == LogLevel.Warning && m.Categoria == "Flit.Atlas.Csp");
        Assert.Equal(
            "Violación de CSP (bloqueada): script-src-elem bloqueó https://cdn.ejemplo.com en /portal/paquetes/1 (fuente https://qa.atlas.flitsas.com/assets/index-abc.js:12:7)",
            bloqueada.Texto);
        var informada = Assert.Single(registros.Mensajes, m => m.Nivel == LogLevel.Information && m.Categoria == "Flit.Atlas.Csp");
        Assert.StartsWith("Violación de CSP (solo informe): require-trusted-types-for informó trusted-types-sink en /contratos", informada.Texto, StringComparison.Ordinal);
        foreach (var prohibido in new[] { "ejemplo.com/x.js", "clave=", "correo", "token-secreto", "alert", "Navegador Demo", "v=1", navegador.Ip })
        {
            Assert.All(registros.Mensajes.Where(m => m.Categoria == "Flit.Atlas.Csp"), m => Assert.DoesNotContain(prohibido, m.Texto, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Un_informe_invalido_se_rechaza_y_los_informes_tienen_limite_por_IP()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:ReportesCspPorIp"] = "4",
        });
        using var navegador = Navegador.De(api);
        using var otra = Navegador.De(api);
        navegador.Ip = IpNueva();
        otra.Ip = IpNueva();

        var tipo = await navegador.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta, Cuerpo(InformeReportUri, "text/plain"), csrf: false);
        var roto = await navegador.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta, Cuerpo("{no es json", "application/csp-report"), csrf: false);
        var grande = await navegador.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta,
            Cuerpo($"{{\"csp-report\":{{\"document-uri\":\"{new string('a', EndpointsReportesCsp.TamanoMaximo)}\"}}}}", "application/csp-report"), csrf: false);
        var cuarto = await navegador.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta, Cuerpo(InformeReportUri, "application/csp-report"), csrf: false);
        var quinto = await navegador.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta, Cuerpo(InformeReportUri, "application/csp-report"), csrf: false);
        var deOtraIp = await otra.EnviarAsync(HttpMethod.Post, EndpointsReportesCsp.Ruta, Cuerpo(InformeReportUri, "application/csp-report"), csrf: false);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, tipo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, roto.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cuarto.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, quinto.StatusCode);
        Assert.True(quinto.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.NoContent, deOtraIp.StatusCode);
    }

    [Theory]
    [InlineData("inline", "inline")]
    [InlineData("eval", "eval")]
    [InlineData("data:image/png;base64,AAAA", "data")]
    [InlineData("https://ejemplo.com:8443/ruta/secreta?x=1", "https://ejemplo.com:8443")]
    [InlineData("wss://ejemplo.com/hub", "wss://ejemplo.com")]
    [InlineData("javascript:alert(1)", "otro")]
    [InlineData("", "ninguno")]
    public void Lo_bloqueado_queda_en_su_origen_o_su_palabra_clave(string valor, string esperado) =>
        Assert.Equal(esperado, EndpointsReportesCsp.Bloqueado(valor));

    [Fact]
    public void Del_Reporting_API_se_registran_a_lo_sumo_diez_informes()
    {
        var uno = """{"type":"csp-violation","body":{"effectiveDirective":"img-src","blockedURL":"https://ejemplo.com/a.png","disposition":"enforce"}}""";
        var cuerpo = Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat(uno, 25)) + "]");

        var violaciones = EndpointsReportesCsp.Leer(cuerpo);

        Assert.Equal(EndpointsReportesCsp.InformesMaximos, violaciones.Count);
        Assert.All(violaciones, v => Assert.Equal(("img-src", "https://ejemplo.com", false), (v.Directiva, v.Bloqueado, v.SoloInforme)));
    }

    // --- Límites de tasa del resto de la API ---

    private static async Task AssertLimiteAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.InRange(int.Parse(Assert.Single(respuesta.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(Ct));
        Assert.Equal(LimitacionTasa.Codigo, cuerpo.RootElement.GetProperty("codigo").GetString());
        Assert.Equal("Demasiadas peticiones. Intenta de nuevo en un momento.", cuerpo.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Un_empleado_tiene_limite_por_persona_aunque_cambie_de_IP()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:PorEmpleado"] = "3",
        });
        using var navegador = Navegador.De(api);
        using var otro = Navegador.De(api);
        navegador.Ip = IpNueva();
        otro.Ip = navegador.Ip;
        await navegador.EntrarSimuladoAsync(CorreoNuevo(), "Empleada Demo");
        await otro.EntrarSimuladoAsync(CorreoNuevo(), "Empleado Demo");

        var respuestas = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            navegador.Ip = IpNueva();
            respuestas.Add(await navegador.GetAsync("/api/v1/plataforma/banderas"));
        }

        Assert.All(respuestas.Take(3), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await AssertLimiteAsync(respuestas[3]);
        // Otra persona (aunque comparta la IP) y las rutas de salud no se ven afectadas.
        Assert.Equal(HttpStatusCode.OK, (await otro.GetAsync("/api/v1/plataforma/banderas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Los_externos_tienen_un_limite_mas_estricto_y_el_portal_uno_por_organizacion()
    {
        await using var escenario = new EscenarioPortal(entorno, new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:PorExterno"] = "3",
            [$"{OpcionesLimiteTasa.Seccion}:PortalPorOrganizacion"] = "4",
        });
        var orgA = await escenario.OrganizacionAsync("Organismo Demo A");
        var orgB = await escenario.OrganizacionAsync("Organismo Demo B");
        var a1 = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador);
        var a2 = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador);
        var a3 = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador);
        var b = await escenario.ExternoAsync(orgB, RolesAtlas.Integrador);

        // Por persona: la cuarta petición de a1 a la API (fuera del portal) se corta.
        var deA1 = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            deA1.Add(await a1.Navegador.GetAsync("/api/v1/plataforma/banderas"));
        }
        // Por organización en el portal: a2 y a3 suman 4 entre las dos; la quinta se corta, aunque cada una lleve menos de 3.
        var portal = new List<HttpResponseMessage>
        {
            await a2.Navegador.GetAsync("/api/v1/portal/inicio"),
            await a2.Navegador.GetAsync("/api/v1/portal/inicio"),
            await a3.Navegador.GetAsync("/api/v1/portal/inicio"),
            await a3.Navegador.GetAsync("/api/v1/portal/inicio"),
        };
        var quinta = await a3.Navegador.GetAsync("/api/v1/portal/inicio");
        var deB = await b.Navegador.GetAsync("/api/v1/portal/inicio");

        Assert.All(deA1.Take(3), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await AssertLimiteAsync(deA1[3]);
        Assert.All(portal, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await AssertLimiteAsync(quinta);
        Assert.Equal(HttpStatusCode.OK, deB.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_el_limite_es_por_IP()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:AnonimoPorIp"] = "2",
        });
        using var navegador = Navegador.De(api);
        using var otra = Navegador.De(api);
        navegador.Ip = IpNueva();
        otra.Ip = IpNueva();

        var respuestas = new List<HttpResponseMessage>();
        for (var i = 0; i < 3; i++)
        {
            respuestas.Add(await navegador.GetAsync("/api/v1/version"));
        }

        Assert.All(respuestas.Take(2), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await AssertLimiteAsync(respuestas[2]);
        Assert.Equal(HttpStatusCode.OK, (await otra.GetAsync("/api/v1/version")).StatusCode);
        // Las rutas de identidad tienen sus propios límites (ATLAS-ADR-0022), no este.
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/v1/identidad/proveedores")).StatusCode);
    }

    [Fact]
    public async Task El_proxy_del_sandbox_tiene_limite_por_llave_y_por_IP()
    {
        await using var api = entorno.Api(configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesLimiteTasa.Seccion}:SandboxPorLlave"] = "2",
            [$"{OpcionesLimiteTasa.Seccion}:SandboxPorIp"] = "3",
        });
        using var cliente = api.CreateClient();
        const string ruta = "/api/v1/contratos/sandbox/tramites-demo/api-demo/tramites/1";
        async Task<HttpResponseMessage> PedirAsync(string llave, string ip)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llave);
            peticion.Headers.Add("X-Forwarded-For", ip);
            return await cliente.SendAsync(peticion, Ct);
        }
        var ip = IpNueva();
        var otraIp = IpNueva();

        var conLlaveA = new[] { await PedirAsync("llave-demo-a", ip), await PedirAsync("llave-demo-a", ip), await PedirAsync("llave-demo-a", ip) };
        var llaveAEnOtraIp = await PedirAsync("llave-demo-a", otraIp);
        var llaveB = await PedirAsync("llave-demo-b", ip);
        var llaveC = await PedirAsync("llave-demo-c", ip);

        Assert.All(conLlaveA.Take(2), r => Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode));
        await AssertLimiteAsync(conLlaveA[2]);
        // La llave cuenta en todas las IP; la IP, con cualquier llave: la tercera rechazada por la llave no le suma, así que
        // B es la tercera de la IP y C la cuarta.
        await AssertLimiteAsync(llaveAEnOtraIp);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, llaveB.StatusCode);
        await AssertLimiteAsync(llaveC);
    }

    /// <summary>Guarda los mensajes del log para revisar qué se escribe.</summary>
    private sealed class RegistrosDePrueba : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<(string Categoria, LogLevel Nivel, string Texto)> Cola { get; } = new();

        public IEnumerable<(string Categoria, LogLevel Nivel, string Texto)> Mensajes => Cola;

        public ILogger CreateLogger(string categoryName) => new Registro(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Registro(RegistrosDePrueba dueno, string categoria) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                dueno.Cola.Enqueue((categoria, logLevel, formatter(state, exception)));
        }
    }
}
