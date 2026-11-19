using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Flit.Atlas.Api.Tests.Exportaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Api.Tests.Carga;

/// <summary>
/// Prueba de carga de la HU 4.10 (ATLAS-ADR-0048), reproducible en cualquier máquina con Docker: atlas-api real en
/// Kestrel con Postgres, Redis, RabbitMQ, RustFS y atlas-docs construido desde su Dockerfile (lo mismo que
/// <see cref="ExportacionesPuntaAPuntaTests"/>), con un escenario ficticio sembrado (el contrato ICT enriquecido en un
/// paquete publicado y compartido con «Organismo Demo»), y k6 corriendo <c>tests/carga/atlas.js</c> contra él: 15
/// empleados y 5 externos navegando (inicio, catálogo, contratos y portal) más 2 exportaciones simultáneas del paquete en
/// los siete formatos. Falla si k6 no cumple sus umbrales (p95 de las lecturas &lt; 500 ms antes y durante las
/// exportaciones, error &lt; 1 %, exportaciones listas). Se omite si no está <c>ATLAS_CARGA_K6</c> (ruta del binario de
/// k6): ver <c>tests/carga/README.md</c>.
/// </summary>
public sealed class PruebaCargaTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const int Empleados = 15;
    private const int Externos = 5;

    [Fact]
    public async Task Veinte_personas_navegando_y_dos_exportaciones_simultaneas_no_degradan_la_API()
    {
        var k6 = Environment.GetEnvironmentVariable("ATLAS_CARGA_K6");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(k6), "Prueba de carga: define ATLAS_CARGA_K6 con la ruta del binario de k6 (tests/carga/README.md).");

        await using var contenedores = new ContenedoresConDocs();
        await contenedores.InitializeAsync();

        // 1. Escenario en la base compartida, con atlas-api en TestServer (la CI de GitHub simulada publica el contrato).
        Guid paquete;
        var usuariosEmpleados = new List<Guid>();
        var usuariosExternos = new List<Guid>();
        await using (var escenario = new EscenarioPortal(entorno, contenedores.Configuracion(conBroker: false)))
        {
            var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
            var (_, slug) = await EscenarioPortal.AplicativoAsync(lider, "Trámites Demo Carga");
            var spec = await File.ReadAllTextAsync(ContratosDePrueba.RutaIct, Ct);
            var (contrato, version) = await escenario.PublicarAsync(slug, "core-ict-carga", spec, version: "1.1.0");
            var operaciones = await EscenarioPortal.Ok(await lider.GetAsync($"/api/v1/contratos/paquetes/versiones/{version}/operaciones"));
            var publicas = operaciones.GetProperty("elementos").EnumerateArray()
                .Where(o => o.GetProperty("publica").GetBoolean()).Select(o => o.GetProperty("clave").GetString()!).ToArray();
            Assert.NotEmpty(publicas);
            paquete = await EscenarioPortal.PaquetePublicadoAsync(lider, "Paquete ICT Organismo Demo", contrato, version, publicas);
            var organizacion = await escenario.OrganizacionAsync("Organismo Demo");
            await EscenarioPortal.CompartirAsync(lider, paquete, organizacion, exportar: true);

            var personas = new List<Guid>();
            for (var i = 0; i < Empleados; i++)
            {
                personas.Add((await escenario.EmpleadoAsync(RolesAtlas.Desarrollador)).PersonaId);
            }
            var externas = new List<Guid>();
            for (var i = 0; i < Externos; i++)
            {
                externas.Add((await escenario.ExternoAsync(organizacion, RolesAtlas.Integrador, extras: ["exportar"])).PersonaId);
            }
            await using var identidad = entorno.Identidad();
            usuariosEmpleados.AddRange(await identidad.Users.Where(u => personas.Contains(u.PersonaId)).Select(u => u.Id).ToListAsync(Ct));
            usuariosExternos.AddRange(await identidad.Users.Where(u => externas.Contains(u.PersonaId)).Select(u => u.Id).ToListAsync(Ct));
        }

        // 2. atlas-api en Kestrel, con su outbox y su consumo de RabbitMQ: la de k6. Los límites de tasa son los de siempre.
        var puerto = PuertoLibre();
        await using var api = entorno.Api(configuracion: contenedores.Configuracion(conBroker: true));
        api.UseKestrel(o => o.Listen(IPAddress.Loopback, puerto));
        api.StartServer();
        var url = $"http://127.0.0.1:{puerto}";

        using var http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
        async Task<string> CookieAsync(Guid usuario, string ip)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, Navegador.RutaIngresoPrueba) { Content = JsonContent.Create(new PedidoIngresoPrueba(usuario)) };
            peticion.Headers.Add("X-Forwarded-For", ip);
            using var respuesta = await http.SendAsync(peticion, Ct);
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
            return respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(Identidad.Sesiones.ClaimsAtlas.NombreCookie + "=", StringComparison.Ordinal)).Split(';')[0];
        }
        var internos = new List<string>();
        foreach (var (usuario, i) in usuariosEmpleados.Select((u, i) => (u, i)))
        {
            internos.Add(await CookieAsync(usuario, $"198.51.100.{i + 1}"));
        }
        var externos = new List<string>();
        foreach (var (usuario, i) in usuariosExternos.Select((u, i) => (u, i)))
        {
            externos.Add(await CookieAsync(usuario, $"198.51.100.{Empleados + i + 1}"));
        }

        // 3. k6: los datos (cookies e ids) van en un archivo temporal, nunca en el repo.
        var carpeta = Directory.CreateTempSubdirectory("atlas-carga-");
        var datos = Path.Combine(carpeta.FullName, "datos.json");
        await File.WriteAllTextAsync(datos, JsonSerializer.Serialize(new { internos, externos, paqueteId = paquete }), Ct);
        var resumen = Path.Combine(carpeta.FullName, "resumen.json");
        var inicio = new ProcessStartInfo(k6!)
        {
            ArgumentList = { "run", "--quiet", "--summary-export", resumen, Path.Combine(Repositorio.Raiz, "tests", "carga", "atlas.js") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["ATLAS_URL"] = url,
                ["ATLAS_CARGA_DATOS"] = datos,
                ["ATLAS_CARGA_IP_POR_VU"] = "1",
                ["ATLAS_CARGA_DURACION"] = Environment.GetEnvironmentVariable("ATLAS_CARGA_DURACION") ?? "3m",
                ["K6_NO_USAGE_REPORT"] = "true",
            },
        };
        using var proceso = Process.Start(inicio)!;
        var salida = proceso.StandardOutput.ReadToEndAsync(Ct);
        var errores = proceso.StandardError.ReadToEndAsync(Ct);
        await proceso.WaitForExitAsync(Ct);
        var texto = await salida + await errores;
        await File.WriteAllTextAsync(Path.Combine(carpeta.FullName, "salida.txt"), texto, Ct);
        TestContext.Current.SendDiagnosticMessage($"Resultados de k6 en {carpeta.FullName}");

        Assert.True(proceso.ExitCode == 0, $"k6 terminó con {proceso.ExitCode} (umbrales sin cumplir o error). Resumen en {resumen}.\n{texto}");
    }

    private static int PuertoLibre()
    {
        using var escucha = new TcpListener(IPAddress.Loopback, 0);
        escucha.Start();
        return ((IPEndPoint)escucha.LocalEndpoint).Port;
    }
}
