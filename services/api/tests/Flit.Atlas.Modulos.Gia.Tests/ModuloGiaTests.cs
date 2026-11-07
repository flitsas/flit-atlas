using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Modulos.Gia.Semilla;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Flit.Atlas.Modulos.Gia.Tests.Contenedores))]

namespace Flit.Atlas.Modulos.Gia.Tests;

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
        var nombre = $"atlas_gia_{Interlocked.Increment(ref _bases)}";
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
        await using var gia = bd.Gia();
        await gia.Database.MigrateAsync();
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

    public GiaDbContext Gia()
    {
        var opciones = new DbContextOptionsBuilder<GiaDbContext>();
        GiaDbContext.Configurar(opciones, Servicio);
        return new GiaDbContext(opciones.Options);
    }
}

/// <summary>El módulo cumple el contrato, su esquema hace cumplir la inmutabilidad y la semilla no inventa nada (ATLAS-ADR-0055).</summary>
public class ModuloGiaTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo_y_viene_apagado()
    {
        var modulo = new ModuloGia();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("gia", modulo.Id);
        Assert.False(modulo.ActivoPorDefecto);
        Assert.Equal("__ef_migraciones_gia", GiaDbContext.TablaHistorial);
    }

    [Fact]
    public void El_manifiesto_conserva_los_codigos_y_las_concesiones()
    {
        var permisos = new ModuloGia().Permisos.Permisos.ToDictionary(p => p.Codigo);

        Assert.Equal(["atlas.gia.auditar", "atlas.gia.gestionar", "atlas.gia.politicas.ver", "atlas.gia.ver"], permisos.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(RolesAtlas.Empleados, permisos[PermisosGia.PoliticasVer].ConcedidoA);
        Assert.Equal([RolesAtlas.GobiernoIa], permisos[PermisosGia.Gestionar].ConcedidoA);
        Assert.Equal([RolesAtlas.GobiernoIa, RolesAtlas.Auditor], permisos[PermisosGia.Ver].ConcedidoA);
        Assert.Equal([RolesAtlas.GobiernoIa, RolesAtlas.Auditor], permisos[PermisosGia.Auditar].ConcedidoA);
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Gia();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Gia -o Datos/Migraciones");
    }

    [Fact]
    public async Task Migra_con_el_usuario_del_servicio_y_marca_los_datos_personales()
    {
        var bd = await contenedores.CrearBaseAsync();

        var tablas = await Contenedores.ConsultarAsync(bd.Servicio,
            "SELECT table_name::text FROM information_schema.tables WHERE table_schema = 'atlas' AND table_name LIKE 'gia%' ORDER BY 1");
        var pii = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT c.relname || '.' || a.attname FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            WHERE c.relname LIKE 'gia%' AND col_description(a.attrelid, a.attnum) = '@pii' ORDER BY 1
            """);

        Assert.Equal(["gia_aceptacion_politica", "gia_asiento", "gia_caso_uso", "gia_caso_uso_version", "gia_eipd", "gia_evaluacion_riesgo",
            "gia_herramienta", "gia_incidente", "gia_politica", "gia_rol_gobierno", "gia_voto_evaluacion"], tablas);
        Assert.Equal(["gia_aceptacion_politica.ip", "gia_asiento.correo", "gia_asiento.usuario_nombre", "gia_caso_uso_version.motivo", "gia_incidente.descripcion", "gia_voto_evaluacion.comentario"], pii);
    }

    [Fact]
    public async Task La_semilla_es_idempotente_y_no_pisa_lo_editado()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Gia();

        Assert.Equal((13, 7), await SemillaGia.AplicarAsync(contexto, TimeProvider.System, Ct));
        var caso = await contexto.Casos.SingleAsync(c => c.Codigo == "flit2-ocr-documentos", Ct);
        caso.Nombre = "Editado";
        await contexto.SaveChangesAsync(Ct);

        Assert.Equal((0, 0), await SemillaGia.AplicarAsync(contexto, TimeProvider.System, Ct));
        Assert.Equal("Editado", (await contexto.Casos.AsNoTracking().SingleAsync(c => c.Codigo == "flit2-ocr-documentos", Ct)).Nombre);
    }

    [Fact]
    public async Task Lo_sembrado_entra_en_borrador_sin_dueno_ni_aprobacion()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Gia();
        await SemillaGia.AplicarAsync(contexto, TimeProvider.System, Ct);

        var casos = await contexto.Casos.AsNoTracking().ToListAsync(Ct);
        var politicas = await contexto.Politicas.AsNoTracking().ToListAsync(Ct);

        Assert.All(casos, c =>
        {
            Assert.Equal(ValoresGia.EstadoBorrador, c.Estado);
            Assert.Null(c.DuenoPersonaId);
            Assert.Null(c.AprobadoPor);
            Assert.Null(c.NivelRiesgo);
            Assert.NotEmpty(ReglasGia.FaltantesFicha(c));
        });
        Assert.All(politicas, p =>
        {
            Assert.Equal(ValoresGia.PoliticaBorrador, p.Estado);
            Assert.Null(p.AprobadaPor);
            Assert.Contains(ReglasGia.MarcaPendiente, p.Contenido, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void El_riesgo_preliminar_coincide_con_el_resumen_del_plan()
    {
        // Plan 02 §7.3, en el orden de CU-01 a CU-13.
        string[] esperado = ["alto", "medio", "alto", "alto", "bajo", "medio_alto", "alto", "medio", "medio", "alto", "medio", "medio_alto", "medio"];

        Assert.Equal(esperado, SemillaGia.Casos.Select(c => c.RiesgoPreliminar));
    }

    [Fact]
    public void La_semilla_no_trae_personas_ni_el_proyecto_de_gcp()
    {
        var texto = string.Join('\n', SemillaGia.Casos.SelectMany(c => new[] { c.Nombre, c.Aplicacion, c.Proposito, c.Detalle })
            .Concat(SemillaGia.Politicas.SelectMany(p => new[] { p.Titulo, p.Extiende, p.Insumo })));

        Assert.DoesNotContain("744152990081", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Mejía", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("gcp-json-key", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_historial_de_la_ficha_no_se_cambia_ni_se_borra()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Gia();
        await SemillaGia.AplicarAsync(contexto, TimeProvider.System, Ct);

        await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlRawAsync("UPDATE atlas.gia_caso_uso_version SET motivo = 'x'", Ct));
        await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlRawAsync("DELETE FROM atlas.gia_caso_uso_version", Ct));
    }

    [Fact]
    public async Task Solo_puede_haber_una_version_vigente_por_politica()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Gia();
        contexto.Politicas.Add(new Politica { Codigo = "POL-X", Version = "1.0", Titulo = "X", Contenido = "x", Estado = ValoresGia.PoliticaVigente });
        contexto.Politicas.Add(new Politica { Codigo = "POL-X", Version = "2.0", Titulo = "X", Contenido = "x", Estado = ValoresGia.PoliticaVigente });

        await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
    }
}
