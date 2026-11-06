using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Modulos.Documentacion.Tests.Contenedores))]

namespace Flit.Atlas.Modulos.Documentacion.Tests;

/// <summary>
/// Postgres 16 (la imagen del AppHost) con el usuario del servicio <c>flit_atlas</c>, dueño del esquema <c>atlas</c>. A
/// propósito sin las extensiones <c>unaccent</c> ni <c>vector</c>: la migración no debe depender de ellas (ATLAS-ADR-0053 §7).
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

    /// <summary>Base nueva con el esquema del usuario del servicio, migrada en el orden del migrador: núcleo y luego el módulo.</summary>
    public async Task<BaseDePrueba> CrearBaseAsync()
    {
        var nombre = $"atlas_docs_{Interlocked.Increment(ref _bases)}";
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
        await using (var plataforma = bd.Plataforma())
        {
            await plataforma.Database.MigrateAsync();
        }
        await using var documentacion = bd.Documentacion();
        await documentacion.Database.MigrateAsync();
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
/// <param name="Servicio">Usuario del servicio, <c>flit_atlas</c>.</param>
public sealed record BaseDePrueba(string Admin, string Servicio)
{
    public PlataformaDbContext Plataforma()
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, Servicio);
        return new PlataformaDbContext(opciones.Options);
    }

    public DocumentacionDbContext Documentacion()
    {
        var opciones = new DbContextOptionsBuilder<DocumentacionDbContext>();
        DocumentacionDbContext.Configurar(opciones, Servicio);
        return new DocumentacionDbContext(opciones.Options);
    }
}
