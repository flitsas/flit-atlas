using System.Net.Http.Json;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
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

    /// <summary>atlas-api contra los contenedores; el ambiente y las banderas se pueden cambiar por prueba.</summary>
    public WebApplicationFactory<Program> Api(string ambiente = "local", bool simulado = true, string? baseDeDatos = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:atlas", baseDeDatos ?? Postgres.GetConnectionString());
            b.UseSetting("ConnectionStrings:redis", Redis.GetConnectionString());
            // No se usa en estas pruebas: la outbox solo abre la conexión cuando hay eventos por publicar.
            b.UseSetting("ConnectionStrings:rabbitmq", "amqp://x:x@127.0.0.1:1");
            b.UseSetting("Atlas:Ambiente", ambiente);
            b.UseSetting(ExtensionesIdentidad.ClaveSimulado, simulado ? "true" : "false");
        });

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
