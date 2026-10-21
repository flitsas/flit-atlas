using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Plataforma.Tests.Contenedores))]

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>
/// Postgres 16 con pgvector (la misma imagen del AppHost) y RabbitMQ reales, compartidos por todas las pruebas.
/// Cada prueba pide su propia base con <see cref="CrearBaseAsync"/>.
/// </summary>
public sealed class Contenedores : IAsyncLifetime
{
    /// <summary>Usuario del servicio, creado como lo hace <c>flit/deploy/postgres/servicio-con-esquema-propio.sql</c>.</summary>
    public const string UsuarioServicio = "flit_atlas";

    // Valor ficticio solo para el contenedor efímero de pruebas.
    private const string ClaveServicio = "prueba-local";

    private int _bases;

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg16").Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:4.3").Build();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());

        // El rol es del clúster: se crea una vez. Mismos atributos que el script de flit.
        await EjecutarAsync(Postgres.GetConnectionString(), $"""
            CREATE ROLE {UsuarioServicio} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
                PASSWORD '{ClaveServicio}';
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }

    /// <summary>
    /// Base nueva con el esquema <c>atlas</c> del usuario del servicio, como en dev/qa/pdn: dueño de su esquema, sin
    /// crear en <c>public</c> y con <c>search_path</c> en su esquema.
    /// </summary>
    public async Task<BaseDePrueba> CrearBaseAsync(bool migrar = true)
    {
        var nombre = $"atlas_prueba_{Interlocked.Increment(ref _bases)}";
        await EjecutarAsync(Postgres.GetConnectionString(), $"CREATE DATABASE {nombre};");

        var admin = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = nombre }.ConnectionString;
        await EjecutarAsync(admin, $"""
            CREATE SCHEMA IF NOT EXISTS atlas AUTHORIZATION {UsuarioServicio};
            GRANT CONNECT ON DATABASE {nombre} TO {UsuarioServicio};
            ALTER ROLE {UsuarioServicio} IN DATABASE {nombre} SET search_path = atlas;
            REVOKE CREATE ON SCHEMA public FROM {UsuarioServicio};
            """);

        var servicio = new NpgsqlConnectionStringBuilder(admin)
        {
            Username = UsuarioServicio,
            Password = ClaveServicio,
            // Cada prueba crea su base: sin pool, para poder correr en paralelo sin agotar conexiones.
            Pooling = false,
        }.ConnectionString;

        var bd = new BaseDePrueba(admin, servicio);
        if (migrar)
        {
            await using var contexto = bd.Contexto();
            await contexto.Database.MigrateAsync();
        }
        return bd;
    }

    public static async Task EjecutarAsync(string cadena, string sql)
    {
        await using var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }
}

/// <param name="Admin">Superusuario del contenedor (hace de administrador de la base).</param>
/// <param name="Servicio">Usuario del servicio, <c>flit_atlas</c>: el que usan la app y el migrador.</param>
public sealed record BaseDePrueba(string Admin, string Servicio)
{
    public PlataformaDbContext Contexto(string? cadena = null)
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, cadena ?? Servicio);
        return new PlataformaDbContext(opciones.Options);
    }
}
