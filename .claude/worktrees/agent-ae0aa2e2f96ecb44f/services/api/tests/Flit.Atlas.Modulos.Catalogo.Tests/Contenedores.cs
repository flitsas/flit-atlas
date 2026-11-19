using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Modulos.Catalogo.Tests.Contenedores))]

namespace Flit.Atlas.Modulos.Catalogo.Tests;

/// <summary>
/// Postgres 16 con pgvector (la imagen del AppHost) y el usuario del servicio <c>flit_atlas</c>, dueño del esquema
/// <c>atlas</c>, como en dev/qa/pdn. Cada prueba pide su base con <see cref="CrearBaseAsync"/>.
/// </summary>
public sealed class Contenedores : IAsyncLifetime
{
    public const string UsuarioServicio = "flit_atlas";

    // Valor ficticio solo para el contenedor efímero de pruebas.
    private const string ClaveServicio = "prueba-local";

    private int _bases;

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg16").Build();

    public async ValueTask InitializeAsync()
    {
        await Postgres.StartAsync();
        await EjecutarAsync(Postgres.GetConnectionString(), $"""
            CREATE ROLE {UsuarioServicio} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
                PASSWORD '{ClaveServicio}';
            """);
    }

    public async ValueTask DisposeAsync() => await Postgres.DisposeAsync();

    /// <summary>Base nueva con el esquema del usuario del servicio; con <paramref name="migrar"/>, núcleo y luego el módulo.</summary>
    public async Task<BaseDePrueba> CrearBaseAsync(bool migrar = true)
    {
        var nombre = $"atlas_catalogo_{Interlocked.Increment(ref _bases)}";
        await EjecutarAsync(Postgres.GetConnectionString(), $"CREATE DATABASE {nombre};");
        var admin = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = nombre }.ConnectionString;
        await EjecutarAsync(admin, $"""
            CREATE SCHEMA IF NOT EXISTS atlas AUTHORIZATION {UsuarioServicio};
            GRANT CONNECT ON DATABASE {nombre} TO {UsuarioServicio};
            ALTER ROLE {UsuarioServicio} IN DATABASE {nombre} SET search_path = atlas;
            REVOKE CREATE ON SCHEMA public FROM {UsuarioServicio};
            """);
        var servicio = new NpgsqlConnectionStringBuilder(admin) { Username = UsuarioServicio, Password = ClaveServicio, Pooling = false }.ConnectionString;

        var bd = new BaseDePrueba(admin, servicio);
        if (migrar)
        {
            // El orden del migrador: el núcleo primero (la política RLS del módulo usa su función).
            await using (var plataforma = bd.Plataforma())
            {
                await plataforma.Database.MigrateAsync();
            }
            await using var catalogo = bd.Catalogo();
            await catalogo.Database.MigrateAsync();
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

    public static async Task<List<string>> ConsultarAsync(string cadena, string sql)
    {
        await using var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        var filas = new List<string>();
        while (await lector.ReadAsync())
        {
            filas.Add(lector.GetString(0));
        }
        return filas;
    }
}

/// <param name="Admin">Superusuario del contenedor (hace de administrador de la base).</param>
/// <param name="Servicio">Usuario del servicio, <c>flit_atlas</c>: el que usan la app y el migrador.</param>
public sealed record BaseDePrueba(string Admin, string Servicio)
{
    public PlataformaDbContext Plataforma()
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, Servicio);
        return new PlataformaDbContext(opciones.Options);
    }

    /// <summary>Contexto del módulo con el usuario del servicio y, si se da, un alcance de datos fijo.</summary>
    public CatalogoDbContext Catalogo(AlcanceDatos? alcance = null)
    {
        var opciones = new DbContextOptionsBuilder<CatalogoDbContext>();
        CatalogoDbContext.Configurar(opciones, Servicio);
        return new CatalogoDbContext(opciones.Options, alcance is null ? null : new AlcanceFijo(alcance));
    }

    private sealed class AlcanceFijo(AlcanceDatos alcance) : IAlcanceDatos
    {
        public AlcanceDatos Actual => alcance;
    }
}
