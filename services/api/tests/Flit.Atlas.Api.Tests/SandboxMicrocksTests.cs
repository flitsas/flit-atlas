using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Flit.Atlas.Modulos.Contratos.Sandbox;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Flit.Atlas.Api.Tests.EscenarioPortal;

namespace Flit.Atlas.Api.Tests;

/// <summary>Microcks real para <see cref="SandboxMicrocksTests"/>: la imagen «uber» nativa de Aspire (arranca en segundos).</summary>
public sealed class MicrocksReal : IAsyncLifetime
{
    public const string Imagen = "quay.io/microcks/microcks-uber:1.15.0-native";

    public IContainer Contenedor { get; } = new ContainerBuilder(Imagen)
        .WithPortBinding(8080, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/api/health")))
        .Build();

    public string Url => $"http://{Contenedor.Hostname}:{Contenedor.GetMappedPublicPort(8080)}";

    public async ValueTask InitializeAsync() => await Contenedor.StartAsync();

    public async ValueTask DisposeAsync() => await Contenedor.DisposeAsync();
}

/// <summary>
/// <b>CA de la HU 4.7</b> (plan 00 §8.8, ATLAS-ADR-0045) con Microcks real en Testcontainers: un cliente activa el sandbox de
/// su paquete, una operación compartida responde el ejemplo del spec, una no compartida responde 404 (no existe en el mock:
/// se importó el spec podado), otra organización no puede usarlo ni con la URL, revocar la compartición lo apaga y lo borra
/// de Microcks, y una revisión nueva del paquete se reimporta. Categoría <c>Microcks</c>: corre en la suite normal (y en el
/// job de .NET del CI); para saltarla en local, <c>dotnet test … -- --filter-not-trait "Categoria=Microcks"</c>.
/// </summary>
[Trait("Categoria", "Microcks")]
public sealed class SandboxMicrocksTests(EntornoApi entorno, MicrocksReal microcks) : IClassFixture<MicrocksReal>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// OpenAPI 3.1 compartible con ejemplos: <c>GET /{p}/{id}</c> (se comparte), <c>GET /{p}-internos/{id}</c> y
    /// <c>POST /{p}-pagos-internos</c> (no se comparten, pero tienen ejemplos: si llegaran al mock, responderían 200) y, desde
    /// la 1.1.0, <c>GET /{p}-extra/{id}</c>.
    /// </summary>
    private static string SpecConEjemplos(string p, string version = "1.0.0", bool conExtra = false) => $$"""
        openapi: 3.1.0
        info:
          title: API Sandbox Demo {{p}}
          description: Contrato ficticio del sandbox.
          version: {{version}}
          x-flit-visibilidad: compartible
        servers:
          - url: https://servidor-interno.ejemplo.com
        paths:
          /{{p}}/{id}:
            get:
              operationId: obtener{{p}}
              summary: Obtener un trámite de prueba
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
                  examples:
                    uno: { value: '42' }
              responses:
                '200':
                  description: El trámite.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Tramite' }
                      examples:
                        uno:
                          value: { id: '42', organismo: Organismo Demo, estado: aprobado }
          /{{p}}-internos/{id}:
            get:
              operationId: obtenerInterno{{p}}
              summary: Consulta interna
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
                  examples:
                    uno: { value: '42' }
              responses:
                '200':
                  description: Interno.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/PagoInterno' }
                      examples:
                        uno:
                          value: { cuenta: cuenta-interna-demo }
          /{{p}}-pagos-internos:
            post:
              operationId: pagar{{p}}
              summary: Pago interno
              requestBody:
                content:
                  application/json:
                    schema: { $ref: '#/components/schemas/PagoInterno' }
                    examples:
                      uno:
                        value: { cuenta: cuenta-interna-demo }
              responses:
                '200':
                  description: Pagado.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/PagoInterno' }
                      examples:
                        uno:
                          value: { cuenta: cuenta-interna-demo }
        {{(conExtra ? $$"""
          /{{p}}-extra/{id}:
            get:
              operationId: obtenerExtra{{p}}
              summary: Consulta adicional
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
                  examples:
                    uno: { value: '7' }
              responses:
                '200':
                  description: Extra.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Tramite' }
                      examples:
                        uno:
                          value: { id: '7', organismo: Organismo Demo, estado: extra }
        """ : string.Empty)}}
        components:
          schemas:
            Tramite:
              type: object
              properties:
                id: { type: string }
                organismo: { type: string }
                estado: { type: string }
            PagoInterno:
              type: object
              properties:
                cuenta: { type: string }
        """;

    private EscenarioPortal Escenario() => new(entorno, new Dictionary<string, string>
    {
        [$"{OpcionesSandbox.Seccion}:UrlMicrocks"] = microcks.Url,
        [$"{OpcionesSandbox.Seccion}:UrlPublica"] = "https://localhost",
        // Las pruebas corren el mantenimiento a mano (la base es compartida).
        [$"{OpcionesSandbox.Seccion}:TrabajoHabilitado"] = "false",
    });

    private static async Task<HttpResponseMessage> Mock(WebApplicationFactory<Program> api, HttpMethod metodo, string url, string? llave, string? json = null)
    {
        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var peticion = new HttpRequestMessage(metodo, new Uri(url).PathAndQuery);
        if (llave is not null)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llave);
        }
        if (json is not null)
        {
            peticion.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        var respuesta = await cliente.SendAsync(peticion, Ct);
        await respuesta.Content.LoadIntoBufferAsync(Ct);
        return respuesta;
    }

    /// <summary>Los servicios que tiene Microcks (directo a su API: en los ambientes nadie más que atlas-api lo alcanza).</summary>
    private async Task<List<string>> ServiciosEnMicrocksAsync()
    {
        using var cliente = new HttpClient();
        var json = await cliente.GetStringAsync($"{microcks.Url}/api/services?page=0&size=500", Ct);
        return [.. JsonDocument.Parse(json).RootElement.EnumerateArray().Select(s => s.GetProperty("name").GetString()!)];
    }

    private static string Ruta(string urlBase) => new Uri(urlBase).AbsolutePath.Split('/')[^2];

    [Fact]
    public async Task Un_cliente_activa_el_sandbox_lo_compartido_responde_el_ejemplo_lo_demas_404_otra_organizacion_no_entra_y_revocar_lo_apaga()
    {
        await using var escenario = Escenario();
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (_, slug) = await AplicativoAsync(lider, "Aplicativo Sandbox Demo");
        var prefijo = $"sbx{Guid.NewGuid().ToString("N")[..8]}";
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-sandbox", SpecConEjemplos(prefijo));
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Sandbox Demo", contrato, version, [$"GET /{prefijo}/{{id}}"]);
        var orgA = await escenario.OrganizacionAsync("Organismo Demo Sandbox A");
        var orgB = await escenario.OrganizacionAsync("Organismo Demo Sandbox B");
        var conA = await CompartirAsync(lider, paquete, orgA, sandbox: true);
        await CompartirAsync(lider, paquete, orgB, sandbox: true);
        var a = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador);
        var b = await escenario.ExternoAsync(orgB, RolesAtlas.Integrador);

        // 1. A activa: 201 con la llave (una sola vez) y la URL base del contrato; la lista no trae la llave.
        var activado = await Ok(await Enviar(a.Navegador, HttpMethod.Post, "/api/v1/portal/sandbox", new { paqueteId = paquete }), HttpStatusCode.Created);
        var llaveA = activado.GetProperty("llave").GetString()!;
        Assert.StartsWith(ServicioSandbox.PrefijoLlave, llaveA, StringComparison.Ordinal);
        var urlA = activado.GetProperty("sandbox").GetProperty("contratos")[0].GetProperty("urlBase").GetString()!;
        Assert.StartsWith("https://localhost/api/v1/contratos/sandbox/", urlA, StringComparison.Ordinal);
        Assert.Contains("$LLAVE_SANDBOX", activado.GetProperty("sandbox").GetProperty("ejemploCurl").GetString()!, StringComparison.Ordinal);
        var lista = await Ok(await a.Navegador.GetAsync("/api/v1/portal/sandbox"));
        Assert.True(lista.GetProperty("disponible").GetBoolean());
        Assert.Equal(urlA, lista.GetProperty("sandboxes")[0].GetProperty("contratos")[0].GetProperty("urlBase").GetString());
        Assert.DoesNotContain(llaveA, lista.GetRawText(), StringComparison.Ordinal);
        // El servicio en Microcks lleva la ruta opaca (128 bits), no el nombre del paquete ni de la organización.
        var rutaA = Ruta(urlA);
        Assert.Matches("^[0-9a-f]{32}$", rutaA);
        Assert.Contains(await ServiciosEnMicrocksAsync(), s => s.Contains(rutaA, StringComparison.Ordinal));

        // 2. Lo compartido responde el ejemplo del spec; lo no compartido no existe en el mock: 404.
        var compartida = await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}/42", llaveA);
        Assert.Equal(HttpStatusCode.OK, compartida.StatusCode);
        var cuerpo = JsonDocument.Parse(await compartida.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal("aprobado", cuerpo.GetProperty("estado").GetString());
        Assert.Equal("nivel-1", compartida.Headers.GetValues("X-Atlas-Sandbox").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}-internos/42", llaveA)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Mock(escenario.Api, HttpMethod.Post, $"{urlA}/{prefijo}-pagos-internos", llaveA, """{"cuenta":"cuenta-interna-demo"}""")).StatusCode);
        // Ni el spec importado tiene lo no compartido.
        Assert.DoesNotContain(await ServiciosEnMicrocksAsync(), s => s.Contains("internos", StringComparison.Ordinal));

        // 3. Sin llave, 401; con una llave que no es la suya, 404 (no dice si la ruta existe); salir del servicio, nunca 200.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}/42", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}/42", ServicioSandbox.NuevaLlave())).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/..%2F..%2Fotro/r1/{prefijo}/42", llaveA)).StatusCode);

        // 4. Otra organización: B tiene su propio sandbox del mismo paquete, con otra ruta y otra llave. Ni su llave en la URL
        // de A, ni la de A en la de B, ni su sesión (la cookie no cuenta) sirven; su lista no trae lo de A.
        var activadoB = await Ok(await Enviar(b.Navegador, HttpMethod.Post, "/api/v1/portal/sandbox", new { paqueteId = paquete }), HttpStatusCode.Created);
        var llaveB = activadoB.GetProperty("llave").GetString()!;
        var urlB = activadoB.GetProperty("sandbox").GetProperty("contratos")[0].GetProperty("urlBase").GetString()!;
        Assert.NotEqual(Ruta(urlA), Ruta(urlB));
        Assert.Equal(HttpStatusCode.OK, (await Mock(escenario.Api, HttpMethod.Get, $"{urlB}/{prefijo}/42", llaveB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}/42", llaveB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{urlB}/{prefijo}/42", llaveA)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Navegador.GetAsync(new Uri(urlA).AbsolutePath + $"/{prefijo}/42")).StatusCode);
        Assert.DoesNotContain(rutaA, (await Ok(await b.Navegador.GetAsync("/api/v1/portal/sandbox"))).GetRawText(), StringComparison.Ordinal);

        // 5. Regenerar la llave: la anterior deja de servir en ese instante (la ruta no cambia).
        var otraVez = await Ok(await Enviar(a.Navegador, HttpMethod.Post, "/api/v1/portal/sandbox", new { paqueteId = paquete }));
        var llaveA2 = otraVez.GetProperty("llave").GetString()!;
        Assert.Equal(urlA, otraVez.GetProperty("sandbox").GetProperty("contratos")[0].GetProperty("urlBase").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}/42", llaveA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}/42", llaveA2)).StatusCode);

        // 6. Revocar la compartición de A lo apaga y lo borra de Microcks en ese momento; el de B sigue.
        await Ok(await Enviar(lider, HttpMethod.Delete, $"/api/v1/contratos/paquetes/{paquete}/comparticiones/{conA}"));
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{urlA}/{prefijo}/42", llaveA2)).StatusCode);
        Assert.DoesNotContain(await ServiciosEnMicrocksAsync(), s => s.Contains(rutaA, StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await Mock(escenario.Api, HttpMethod.Get, $"{urlB}/{prefijo}/42", llaveB)).StatusCode);
        var internos = await Ok(await lider.GetAsync($"/api/v1/contratos/organizaciones/{orgA}/sandboxes"));
        Assert.Equal("apagado", internos[0].GetProperty("estado").GetString());
        Assert.Equal("revocada", internos[0].GetProperty("motivoApagado").GetString());

        // 7. Una revisión nueva del paquete (con otra operación compartida) se reimporta en la siguiente vuelta del trabajo.
        var (_, version2) = await escenario.PublicarAsync(slug, "api-sandbox", SpecConEjemplos(prefijo, "1.1.0", conExtra: true), "1.1.0");
        await Ok(await Enviar(lider, HttpMethod.Post, $"/api/v1/contratos/paquetes/{paquete}/revisiones", new
        {
            nombre = "Paquete Sandbox Demo", descripcion = "Descripción de Paquete Sandbox Demo.", nota = "Operación extra.", revisionBase = 1,
            definicion = new[] { new { contratoId = contrato, versionId = version2, operaciones = new[] { $"GET /{prefijo}/{{id}}", $"GET /{prefijo}-extra/{{id}}" }, guias = Array.Empty<object>() } },
        }), HttpStatusCode.Created);
        await Ok(await Enviar(lider, HttpMethod.Post, $"/api/v1/contratos/paquetes/{paquete}/publicacion", new { revision = 2 }));
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{urlB}/{prefijo}-extra/7", llaveB)).StatusCode);
        await using (var alcance = escenario.Api.Services.CreateAsyncScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioSandbox>().MantenerAsync(Ct);
        }
        var extra = await Mock(escenario.Api, HttpMethod.Get, $"{urlB}/{prefijo}-extra/7", llaveB);
        Assert.Equal(HttpStatusCode.OK, extra.StatusCode);
        Assert.Equal("extra", JsonDocument.Parse(await extra.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("estado").GetString());
        Assert.Equal(2, (await Ok(await b.Navegador.GetAsync("/api/v1/portal/sandbox"))).GetProperty("sandboxes")[0].GetProperty("revision").GetInt32());
        // La versión anterior del servicio se borró: en Microcks queda una sola por contrato.
        Assert.Single(await ServiciosEnMicrocksAsync(), s => s.Contains(Ruta(urlB), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Microcks_reiniciado_el_mantenimiento_vuelve_a_importar_lo_que_perdio()
    {
        await using var escenario = Escenario();
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (_, slug) = await AplicativoAsync(lider, "Aplicativo Sandbox Reinicio");
        var prefijo = $"sbr{Guid.NewGuid().ToString("N")[..8]}";
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-sandbox-reinicio", SpecConEjemplos(prefijo));
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Sandbox Reinicio", contrato, version, [$"GET /{prefijo}/{{id}}"]);
        var org = await escenario.OrganizacionAsync("Organismo Demo Sandbox Reinicio");
        await CompartirAsync(lider, paquete, org, sandbox: true);
        var a = await escenario.ExternoAsync(org, RolesAtlas.Integrador);
        var activado = await Ok(await Enviar(a.Navegador, HttpMethod.Post, "/api/v1/portal/sandbox", new { paqueteId = paquete }), HttpStatusCode.Created);
        var llave = activado.GetProperty("llave").GetString()!;
        var url = activado.GetProperty("sandbox").GetProperty("contratos")[0].GetProperty("urlBase").GetString()!;

        // Se borra a mano en Microcks (lo que pasa al reiniciarse la imagen «uber», que guarda en memoria).
        await using (var alcance = escenario.Api.Services.CreateAsyncScope())
        {
            var cliente = alcance.ServiceProvider.GetRequiredService<IClienteMicrocks>();
            var servicio = (await ServiciosEnMicrocksAsync()).Single(s => s.Contains(Ruta(url), StringComparison.Ordinal));
            using var http = new HttpClient();
            var detalle = JsonDocument.Parse(await http.GetStringAsync($"{microcks.Url}/api/services?page=0&size=500", Ct)).RootElement
                .EnumerateArray().Single(s => s.GetProperty("name").GetString() == servicio);
            await cliente.BorrarAsync(servicio, detalle.GetProperty("version").GetString()!, Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Mock(escenario.Api, HttpMethod.Get, $"{url}/{prefijo}/42", llave)).StatusCode);
        await using (var alcance = escenario.Api.Services.CreateAsyncScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioSandbox>().MantenerAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.OK, (await Mock(escenario.Api, HttpMethod.Get, $"{url}/{prefijo}/42", llave)).StatusCode);
    }
}

/// <summary>HU 4.7 sin Microcks configurado: la API lo dice claro (503) y la PWA deshabilita el botón con <c>disponible = false</c>.</summary>
public sealed class SandboxSinMicrocksTests(EntornoApi entorno)
{
    [Fact]
    public async Task Sin_Microcks_el_sandbox_no_esta_disponible_y_responde_503()
    {
        await using var escenario = new EscenarioPortal(entorno);
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (_, slug) = await AplicativoAsync(lider, "Aplicativo Sandbox Sin Microcks");
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-sin-microcks", Spec("API Sin Microcks", "sinmicrocks", "SinMicrocks"));
        var paquete = await PaquetePublicadoAsync(lider, "Paquete Sin Microcks", contrato, version, ["GET /sinmicrocks/{id}"]);
        var org = await escenario.OrganizacionAsync("Organismo Demo Sin Microcks");
        await CompartirAsync(lider, paquete, org, sandbox: true);
        var a = await escenario.ExternoAsync(org, RolesAtlas.Integrador);

        var lista = await Ok(await a.Navegador.GetAsync("/api/v1/portal/sandbox"));
        Assert.False(lista.GetProperty("disponible").GetBoolean());
        Assert.Equal("El sandbox no está disponible en este ambiente.", lista.GetProperty("motivo").GetString());
        var activar = await Enviar(a.Navegador, HttpMethod.Post, "/api/v1/portal/sandbox", new { paqueteId = paquete });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, activar.StatusCode);
        Assert.Equal("sandbox_no_disponible", (await Json(activar)).GetProperty("codigo").GetString());
        // Lo ajeno sigue siendo 404 aunque no haya Microcks (primero el acceso).
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(a.Navegador, HttpMethod.Post, "/api/v1/portal/sandbox", new { paqueteId = Guid.NewGuid() })).StatusCode);
        // Y una compartición sin sandbox, 403 con su motivo.
        var otro = await PaquetePublicadoAsync(lider, "Paquete Sin Sandbox", contrato, version, ["GET /sinmicrocks/{id}"]);
        await CompartirAsync(lider, otro, org);
        var sinSandbox = await Enviar(a.Navegador, HttpMethod.Post, "/api/v1/portal/sandbox", new { paqueteId = otro });
        Assert.Equal(HttpStatusCode.Forbidden, sinSandbox.StatusCode);
        Assert.Equal("no_permitido_por_comparticion", (await Json(sinSandbox)).GetProperty("codigo").GetString());
    }
}
