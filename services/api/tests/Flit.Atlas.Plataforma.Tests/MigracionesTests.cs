using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Flit.Atlas.Plataforma.Tests;

public class MigracionesTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Se_aplican_con_el_usuario_del_servicio_y_repetirlas_no_cambia_nada()
    {
        var bd = await contenedores.CrearBaseAsync(migrar: false);
        await using var contexto = bd.Contexto();

        await contexto.Database.MigrateAsync(Ct);
        await contexto.Database.MigrateAsync(Ct);

        Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(contexto.Database.GetMigrations(), await contexto.Database.GetAppliedMigrationsAsync(Ct));
    }

    [Fact]
    public async Task El_script_idempotente_se_puede_aplicar_dos_veces()
    {
        var bd = await contenedores.CrearBaseAsync(migrar: false);
        await using var contexto = bd.Contexto();
        var script = contexto.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        await Contenedores.EjecutarAsync(bd.Servicio, script);
        await Contenedores.EjecutarAsync(bd.Servicio, script);

        Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(Ct));
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Contexto();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Plataforma -o Datos/Migraciones");
    }

    [Fact]
    public async Task Solo_crea_tablas_con_su_prefijo_en_el_esquema_atlas()
    {
        var bd = await contenedores.CrearBaseAsync();

        var tablas = await ConsultarAsync(bd.Servicio, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'atlas'");
        var fuera = await ConsultarAsync(bd.Admin, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");

        Assert.All(tablas, t => Assert.True(
            t.StartsWith(PlataformaDbContext.Prefijo, StringComparison.Ordinal) || t == PlataformaDbContext.TablaHistorial, t));
        Assert.Contains(PlataformaDbContext.TablaAuditoria, tablas);
        Assert.Contains("plataforma_outbox", tablas);
        Assert.Empty(fuera);
    }

    [Fact]
    public async Task Las_entidades_usan_uuid_v7_snake_case_timestamptz_y_marcan_los_datos_personales()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Contexto();
        var organizacion = new Organizacion { Nombre = "Organismo Demo", Tipo = TipoOrganizacion.Cliente };
        var persona = new Persona
        {
            Tipo = TipoPersona.Externo,
            OrganizacionId = organizacion.Id,
            Nombre = "Persona Demo",
            Correo = "  Persona.Demo@Ejemplo.com ",
        };
        contexto.AddRange(organizacion, persona);
        await contexto.SaveChangesAsync(Ct);

        await using var otro = bd.Contexto();
        var leida = await otro.Personas.SingleAsync(p => p.Id == persona.Id, Ct);
        Assert.Equal(7, leida.Id.Version);
        Assert.Equal("persona.demo@ejemplo.com", leida.Correo);
        Assert.NotEqual(default, leida.CreadoEn);
        Assert.Equal(TimeSpan.Zero, leida.CreadoEn.Offset);

        var columnas = await ConsultarAsync(bd.Servicio, """
            SELECT column_name || ':' || data_type FROM information_schema.columns
            WHERE table_schema = 'atlas' AND table_name = 'plataforma_persona'
            """);
        Assert.Contains("creado_en:timestamp with time zone", columnas);
        Assert.Contains("entra_oid:uuid", columnas);
        Assert.Contains("tipo:character varying", columnas);

        var pii = await ConsultarAsync(bd.Servicio, """
            SELECT a.attname FROM pg_attribute a
            WHERE a.attrelid = 'atlas.plataforma_persona'::regclass AND col_description(a.attrelid, a.attnum) = '@pii'
            """);
        Assert.Equal(["correo", "nombre", "telefono"], pii.Order(StringComparer.Ordinal));

        // Con RLS (ATLAS-ADR-0023) el usuario del servicio solo ve filas con el alcance fijado en la sesión.
        var tipo = await ConsultarAsync(bd.Servicio, "SELECT tipo FROM atlas.plataforma_organizacion", comoSistema: true);
        Assert.Equal(["cliente"], tipo);
    }

    /// <param name="comoSistema">Fija antes <c>atlas.alcance_total = on</c>, como lo hace la app para el sistema (RLS).</param>
    internal static async Task<List<string>> ConsultarAsync(string cadena, string sql, bool comoSistema = false)
    {
        await using var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync();
        if (comoSistema)
        {
            await using var alcance = new NpgsqlCommand("SELECT set_config('atlas.alcance_total', 'on', false)", conexion);
            await alcance.ExecuteNonQueryAsync();
        }
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
