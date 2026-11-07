using Flit.Atlas.Modulos.Diagramas.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Modulos.Diagramas.Tests.Contenedores))]

namespace Flit.Atlas.Modulos.Diagramas.Tests;

/// <summary>Postgres 16 con el usuario del servicio <c>flit_atlas</c>, dueño del esquema <c>atlas</c>.</summary>
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

    /// <summary>Base nueva migrada en el orden del migrador: núcleo y luego el módulo.</summary>
    public async Task<BaseDePrueba> CrearBaseAsync()
    {
        var nombre = $"atlas_dgm_{Interlocked.Increment(ref _bases)}";
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
        await using var diagramas = bd.Diagramas();
        await diagramas.Database.MigrateAsync();
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

public sealed record BaseDePrueba(string Admin, string Servicio)
{
    public PlataformaDbContext Plataforma()
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, Servicio);
        return new PlataformaDbContext(opciones.Options);
    }

    public DiagramasDbContext Diagramas()
    {
        var opciones = new DbContextOptionsBuilder<DiagramasDbContext>();
        DiagramasDbContext.Configurar(opciones, Servicio);
        return new DiagramasDbContext(opciones.Options);
    }
}

/// <summary>El módulo cumple el contrato de módulo y su esquema hace cumplir la inmutabilidad (ATLAS-ADR-0054).</summary>
public class ModuloDiagramasTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo()
    {
        var modulo = new ModuloDiagramas();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("diagramas", modulo.Id);
        Assert.True(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(DiagramasDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_diagramas", DiagramasDbContext.TablaHistorial);
        Assert.Empty(modulo.Permisos.Permisos);
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Diagramas();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Diagramas -o Datos/Migraciones");
    }

    [Fact]
    public async Task Migra_con_el_usuario_del_servicio_y_marca_los_datos_personales()
    {
        var bd = await contenedores.CrearBaseAsync();

        var tablas = await Contenedores.ConsultarAsync(bd.Servicio,
            "SELECT table_name::text FROM information_schema.tables WHERE table_schema = 'atlas' AND table_name LIKE 'diagramas%' ORDER BY 1");
        var pii = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT c.relname || '.' || a.attname FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            WHERE c.relname LIKE 'diagramas%' AND col_description(a.attrelid, a.attnum) = '@pii' ORDER BY 1
            """);

        Assert.Equal(["diagramas_comentario", "diagramas_diagrama", "diagramas_nodo", "diagramas_relacion", "diagramas_version"], tablas);
        Assert.Equal(["diagramas_comentario.texto", "diagramas_version.nota"], pii);
    }

    [Fact]
    public async Task Una_version_no_se_cambia_ni_se_borra()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Diagramas();
        var diagrama = new Diagrama
        {
            Codigo = "flujo", AplicativoId = Guid.CreateVersion7(), Tipo = TipoDiagrama.Mermaid, Titulo = "Flujo", Visibilidad = VisibilidadDiagrama.InternoNegocio, VersionActual = 1,
        };
        contexto.Diagramas.Add(diagrama);
        contexto.Versiones.Add(new VersionDiagrama
        {
            DiagramaId = diagrama.Id, Numero = 1, Origen = OrigenVersionDiagrama.Creacion, Titulo = "Flujo", Visibilidad = VisibilidadDiagrama.InternoNegocio,
            Fuente = "graph TD; A-->B;", Nota = "Primera", AutorPersonaId = Guid.CreateVersion7(), GuardadaEn = DateTimeOffset.UtcNow, Huella = new string('a', 64),
        });
        await contexto.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlRawAsync("UPDATE atlas.diagramas_version SET fuente = 'x'", Ct));
        await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlRawAsync("DELETE FROM atlas.diagramas_version", Ct));
    }
}
