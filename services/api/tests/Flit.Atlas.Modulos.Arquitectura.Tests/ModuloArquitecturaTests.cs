using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Modulos.Arquitectura.Tests.Contenedores))]

namespace Flit.Atlas.Modulos.Arquitectura.Tests;

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
        var nombre = $"atlas_arq_{Interlocked.Increment(ref _bases)}";
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
        await using var arquitectura = bd.Arquitectura();
        await arquitectura.Database.MigrateAsync();
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

public sealed record BaseDePrueba(string Admin, string Servicio)
{
    public PlataformaDbContext Plataforma()
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, Servicio);
        return new PlataformaDbContext(opciones.Options);
    }

    public ArquitecturaDbContext Arquitectura()
    {
        var opciones = new DbContextOptionsBuilder<ArquitecturaDbContext>();
        ArquitecturaDbContext.Configurar(opciones, Servicio);
        return new ArquitecturaDbContext(opciones.Options);
    }
}

/// <summary>El módulo cumple el contrato de módulo y su esquema hace cumplir la inmutabilidad (ATLAS-ADR-0056).</summary>
public class ModuloArquitecturaTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo_y_conserva_los_codigos_y_concesiones_del_manifiesto()
    {
        var modulo = new ModuloArquitectura();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("arquitectura", modulo.Id);
        Assert.True(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(ArquitecturaDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_arquitectura", ArquitecturaDbContext.TablaHistorial);

        var permisos = modulo.Permisos.Permisos.ToDictionary(p => p.Codigo, p => p.ConcedidoA.Order(StringComparer.Ordinal).ToList());
        Assert.Equal(2, permisos.Count);
        Assert.Equal(new[] { RolesAtlas.LiderTecnico, RolesAtlas.Desarrollador, RolesAtlas.AsesorComercial }.Order(StringComparer.Ordinal), permisos["atlas.arquitectura.ver"]);
        Assert.Equal(new[] { RolesAtlas.LiderTecnico, RolesAtlas.Desarrollador }.Order(StringComparer.Ordinal), permisos["atlas.arquitectura.ver_despliegue"]);
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Arquitectura();
        Assert.False(contexto.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migra_con_el_usuario_del_servicio()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Arquitectura();
        Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(Ct));
        Assert.Empty(await contexto.Publicaciones.ToListAsync(Ct));
    }

    private static async Task<(ArquitecturaDbContext Contexto, Guid Publicacion)> SembrarAsync(BaseDePrueba bd)
    {
        var contexto = bd.Arquitectura();
        var proyecto = new ProyectoArquitectura { Codigo = "demo", AplicativoId = Guid.NewGuid(), Repositorio = "flitsas-demo/demo" };
        contexto.Proyectos.Add(proyecto);
        var publicacion = new PublicacionArquitectura
        {
            ProyectoId = proyecto.Id, Tipo = TipoPublicacion.Modelo, Canal = "dev", Cluster = string.Empty, Rama = "develop", CommitSha = new string('a', 40),
            Estado = EstadoPublicacion.Borrador, Visibilidad = VisibilidadArquitectura.InternoTi, Contenido = "{}", Sha256 = new string('b', 64),
            PublicadoPor = "github:flitsas-demo/demo@develop", PublicadoEn = DateTimeOffset.UtcNow,
        };
        contexto.Publicaciones.Add(publicacion);
        contexto.Elementos.Add(new ElementoArquitectura
        {
            PublicacionId = publicacion.Id, ElementoId = "demo.web", Kind = "container", Titulo = "Portal", Etiquetas = [], Metadatos = "{}", Enlaces = "[]",
        });
        await contexto.SaveChangesAsync(Ct);
        return (contexto, publicacion.Id);
    }

    [Fact]
    public async Task Una_publicacion_y_sus_proyecciones_no_se_cambian_ni_se_borran()
    {
        var bd = await contenedores.CrearBaseAsync();
        var (contexto, id) = await SembrarAsync(bd);
        await using (contexto)
        {
            var cambiar = await Assert.ThrowsAsync<PostgresException>(() =>
                contexto.Database.ExecuteSqlAsync($"UPDATE atlas.arquitectura_publicacion SET contenido = 'otro' WHERE id = {id}", Ct));
            Assert.Contains("inmutables", cambiar.MessageText, StringComparison.Ordinal);
            await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlAsync($"DELETE FROM atlas.arquitectura_publicacion WHERE id = {id}", Ct));
            await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlAsync($"UPDATE atlas.arquitectura_elemento SET titulo = 'otro'", Ct));
        }
    }

    [Fact]
    public async Task Dos_publicaciones_del_mismo_sha_y_canal_chocan_y_un_modelo_no_lleva_cluster()
    {
        var bd = await contenedores.CrearBaseAsync();
        var (contexto, _) = await SembrarAsync(bd);
        await using (contexto)
        {
            var proyecto = await contexto.Proyectos.SingleAsync(Ct);
            PublicacionArquitectura Otra(string cluster) => new()
            {
                ProyectoId = proyecto.Id, Tipo = TipoPublicacion.Modelo, Canal = "dev", Cluster = cluster, Rama = "develop", CommitSha = new string('a', 40),
                Estado = EstadoPublicacion.Borrador, Visibilidad = VisibilidadArquitectura.InternoTi, Contenido = "{}", Sha256 = new string('c', 64),
                PublicadoPor = "github:flitsas-demo/demo@develop", PublicadoEn = DateTimeOffset.UtcNow,
            };
            contexto.Publicaciones.Add(Otra(string.Empty));
            await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
            contexto.ChangeTracker.Clear();
            contexto.Publicaciones.Add(Otra("cluster-demo"));
            await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
        }
    }
}
