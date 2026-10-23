using System.Net;
using System.Net.Http.Json;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Api.Tests.EntornoApi))]

namespace Flit.Atlas.Api.Tests;

/// <summary>Postgres (con las migraciones aplicadas) y Redis reales, compartidos por las pruebas de la API.</summary>
public sealed class EntornoApi : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg16").Build();

    public RedisContainer Redis { get; } = new RedisBuilder("redis:8.6").Build();

    private int _bases;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Redis.StartAsync());
        await MigrarAsync(Postgres.GetConnectionString());
    }

    /// <summary>Base nueva y migrada, para las pruebas que necesitan partir de cero (p. ej. el primer ingreso).</summary>
    public async Task<string> BaseNuevaAsync()
    {
        var nombre = $"atlas_api_{Interlocked.Increment(ref _bases)}";
        await using (var conexion = new NpgsqlConnection(Postgres.GetConnectionString()))
        {
            await conexion.OpenAsync();
            await using var comando = new NpgsqlCommand($"CREATE DATABASE {nombre}", conexion);
            await comando.ExecuteNonQueryAsync();
        }
        var cadena = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = nombre }.ConnectionString;
        await MigrarAsync(cadena);
        return cadena;
    }

    private static async Task MigrarAsync(string cadena)
    {
        var servicios = new ServiceCollection()
            .Configure<IdentityOptions>(IdentidadDbContext.ConfigurarIdentity)
            .AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, cadena))
            .AddDbContext<IdentidadDbContext>(o => IdentidadDbContext.Configurar(o, cadena))
            .BuildServiceProvider();
        await using var alcance = servicios.CreateAsyncScope();
        await alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>().Database.MigrateAsync();
        await alcance.ServiceProvider.GetRequiredService<IdentidadDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Redis.DisposeAsync();
    }

    /// <summary>
    /// atlas-api contra los contenedores; el ambiente, las banderas, la configuración y el reloj se pueden cambiar por
    /// prueba.
    /// </summary>
    public WebApplicationFactory<Program> Api(
        string ambiente = "local", bool simulado = true, string? baseDeDatos = null,
        IReadOnlyDictionary<string, string>? configuracion = null, TimeProvider? reloj = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            foreach (var (clave, valor) in configuracion ?? new Dictionary<string, string>())
            {
                b.UseSetting(clave, valor);
            }
            b.ConfigureTestServices(s =>
            {
                // TestServer no trae IP remota: se simula un proxy de loopback para que X-Forwarded-For valga.
                s.AddSingleton<IStartupFilter, IpDeLoopback>();
                if (reloj is not null)
                {
                    s.AddSingleton(reloj);
                }
            });
            b.UseSetting("ConnectionStrings:atlas", baseDeDatos ?? Postgres.GetConnectionString());
            b.UseSetting("ConnectionStrings:redis", Redis.GetConnectionString());
            // No se usa en estas pruebas: la outbox solo abre la conexión cuando hay eventos por publicar.
            b.UseSetting("ConnectionStrings:rabbitmq", "amqp://x:x@127.0.0.1:1");
            b.UseSetting("Atlas:Ambiente", ambiente);
            b.UseSetting(ExtensionesIdentidad.ClaveSimulado, simulado ? "true" : "false");
        });

    /// <summary>
    /// Grupo de Entra nuevo que otorga el rol provisional de administración de identidad (ATLAS-ADR-0022). Las
    /// pruebas de varias clases lo piden a la vez: el rol se crea una sola vez.
    /// </summary>
    public async Task<Guid> GrupoAdministradorAsync(string? baseDeDatos = null)
    {
        var grupo = Guid.NewGuid();
        await using var plataforma = Plataforma(baseDeDatos);
        var rol = await plataforma.Roles.FirstOrDefaultAsync(r => r.Codigo == PoliticasIdentidad.RolProvisional);
        if (rol is null)
        {
            rol = new Rol { Codigo = PoliticasIdentidad.RolProvisional, Nombre = "Superadministrador", Tipo = TipoRol.Interno };
            plataforma.Roles.Add(rol);
            try
            {
                await plataforma.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                plataforma.ChangeTracker.Clear();
                rol = await plataforma.Roles.SingleAsync(r => r.Codigo == PoliticasIdentidad.RolProvisional);
            }
        }
        plataforma.MapeosGrupoEntra.Add(new MapeoGrupoEntra { GrupoId = grupo, GrupoNombre = "FLIT-Admin-Prueba", RolId = rol.Id });
        await plataforma.SaveChangesAsync();
        return grupo;
    }

    public IdentidadDbContext Identidad(string? baseDeDatos = null)
    {
        var servicios = new ServiceCollection().Configure<IdentityOptions>(IdentidadDbContext.ConfigurarIdentity).BuildServiceProvider();
        var opciones = new DbContextOptionsBuilder<IdentidadDbContext>().UseApplicationServiceProvider(servicios);
        IdentidadDbContext.Configurar(opciones, baseDeDatos ?? Postgres.GetConnectionString());
        return new IdentidadDbContext(opciones.Options);
    }

    private sealed class IpDeLoopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, siguiente) =>
            {
                http.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                return siguiente(http);
            });
            next(app);
        };
    }

    public PlataformaDbContext Plataforma(string? baseDeDatos = null)
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, baseDeDatos ?? Postgres.GetConnectionString());
        return new PlataformaDbContext(opciones.Options);
    }
}

/// <summary>
/// Cliente HTTPS que maneja la cookie a mano: así las pruebas ven sus atributos y pueden reusar una cookie vieja.
/// </summary>
public sealed class Navegador(HttpClient http) : IDisposable
{
    public string? Cookie { get; set; }

    /// <summary>IP del cliente que el proxy de loopback informa en <c>X-Forwarded-For</c> (usa IPs de documentación).</summary>
    public string? Ip { get; set; }

    /// <summary>User-Agent del navegador simulado.</summary>
    public string? Agente { get; set; }

    public HttpResponseMessage? UltimaRespuesta { get; private set; }

    public static Navegador De(WebApplicationFactory<Program> api) =>
        new(api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        }));

    public Task<HttpResponseMessage> EntrarSimuladoAsync(string correo, string nombre, params Guid[] grupos) =>
        EnviarAsync(HttpMethod.Post, "/api/v1/identidad/simulado/entrar", JsonContent.Create(new { correo, nombre, grupos }));

    /// <summary>Vuelve a entrar porque una acción sensible pidió reautenticación.</summary>
    public Task<HttpResponseMessage> ReingresarSimuladoAsync(string correo, string nombre, params Guid[] grupos) =>
        EnviarAsync(HttpMethod.Post, "/api/v1/identidad/simulado/entrar",
            JsonContent.Create(new { correo, nombre, grupos, reautenticar = true }));

    public Task<HttpResponseMessage> GetAsync(string ruta) => EnviarAsync(HttpMethod.Get, ruta);

    public async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string ruta, HttpContent? cuerpo = null, bool csrf = true, string? origen = null)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
        if (csrf)
        {
            peticion.Headers.Add(EndpointsIdentidad.CabeceraCsrf, "1");
        }
        if (origen is not null)
        {
            peticion.Headers.Add("Origin", origen);
        }
        if (Cookie is not null)
        {
            peticion.Headers.Add("Cookie", Cookie);
        }
        if (Ip is not null)
        {
            peticion.Headers.Add("X-Forwarded-For", Ip);
        }
        if (Agente is not null)
        {
            peticion.Headers.TryAddWithoutValidation("User-Agent", Agente);
        }
        var respuesta = await http.SendAsync(peticion, TestContext.Current.CancellationToken);
        if (respuesta.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var atlas = cookies.FirstOrDefault(c => c.StartsWith(Identidad.Sesiones.ClaimsAtlas.NombreCookie + "=", StringComparison.Ordinal));
            if (atlas is not null)
            {
                var par = atlas.Split(';')[0];
                Cookie = par.EndsWith('=') ? null : par;
            }
        }
        UltimaRespuesta = respuesta;
        return respuesta;
    }

    public void Dispose() => http.Dispose();
}

/// <summary>Reloj real con un desfase que la prueba puede adelantar (p. ej. para vencer la ventana de reautenticación).</summary>
public sealed class RelojDePrueba : TimeProvider
{
    private TimeSpan _desfase;

    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + _desfase;

    public void Adelantar(TimeSpan tiempo) => _desfase += tiempo;
}
