using Flit.Atlas.Modulos.Hola.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Hola.Tests;

/// <summary>
/// El módulo de ejemplo cumple el contrato de módulo (ATLAS-ADR-0016 y ATLAS-ADR-0026) y el aislamiento por organización
/// (ATLAS-ADR-0023), probado con el usuario del servicio <c>flit_atlas</c>, dueño de las tablas.
/// </summary>
public class ModuloHolaTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo()
    {
        var modulo = new ModuloHola();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("hola", modulo.Id);
        Assert.False(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(HolaDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_hola", HolaDbContext.TablaHistorial);
        Assert.Equal([PermisosHola.Ver, PermisosHola.Gestionar], modulo.Permisos.Permisos.Select(p => p.Codigo));
    }

    [Fact]
    public void El_registro_rechaza_modulos_que_chocan()
    {
        Assert.Throws<InvalidOperationException>(() => ExtensionesModulos.Validar([new ModuloHola(), new ModuloHola()]));
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Hola();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Hola -o Datos/Migraciones");
    }

    [Fact]
    public async Task Se_migra_despues_del_nucleo_con_el_usuario_del_servicio_y_repetirlo_no_cambia_nada()
    {
        var bd = await contenedores.CrearBaseAsync(migrar: false);
        await using (var plataforma = bd.Plataforma())
        {
            await plataforma.Database.MigrateAsync(Ct);
        }
        var delNucleo = await Tablas(bd);

        await using var hola = bd.Hola();
        await hola.Database.MigrateAsync(Ct);
        await hola.Database.MigrateAsync(Ct);

        Assert.Empty(await hola.Database.GetPendingMigrationsAsync(Ct));
        // El módulo solo agrega tablas con su prefijo y su propio historial de migraciones.
        Assert.Equal(["__ef_migraciones_hola", "hola_saludo"], (await Tablas(bd)).Except(delNucleo).Order(StringComparer.Ordinal));
    }

    private static Task<List<string>> Tablas(BaseDePrueba bd) =>
        Contenedores.ConsultarAsync(bd.Servicio, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'atlas'");

    [Fact]
    public async Task La_tabla_tiene_RLS_forzada_su_politica_y_marca_los_datos_personales()
    {
        var bd = await contenedores.CrearBaseAsync();

        var rls = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT c.relname || ':' || c.relrowsecurity || ':' || c.relforcerowsecurity || ':' || p.polname
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'atlas'
            JOIN pg_policy p ON p.polrelid = c.oid
            WHERE c.relname = 'hola_saludo'
            """);
        var pii = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT a.attname FROM pg_attribute a
            WHERE a.attrelid = 'atlas.hola_saludo'::regclass AND col_description(a.attrelid, a.attnum) = '@pii'
            """);

        Assert.Equal(["hola_saludo:true:true:organizacion_visible"], rls);
        Assert.Equal(["texto"], pii);
    }

    private sealed record Escenario(BaseDePrueba Bd, Guid OrgA, Guid OrgB);

    /// <summary>Dos organizaciones cliente con un saludo cada una, escritos por el sistema.</summary>
    private async Task<Escenario> EscenarioAsync()
    {
        var bd = await contenedores.CrearBaseAsync();
        var a = new Organizacion { Nombre = "Organismo Demo A", Tipo = TipoOrganizacion.Cliente };
        var b = new Organizacion { Nombre = "Organismo Demo B", Tipo = TipoOrganizacion.Cliente };
        await using (var plataforma = bd.Plataforma())
        {
            plataforma.AddRange(a, b);
            await plataforma.SaveChangesAsync(Ct);
        }
        await using var sistema = bd.Hola();
        sistema.Saludos.AddRange(
            new Saludo { OrganizacionId = a.Id, AutorPersonaId = Guid.NewGuid(), Texto = "Hola desde A" },
            new Saludo { OrganizacionId = b.Id, AutorPersonaId = Guid.NewGuid(), Texto = "Hola desde B" });
        await sistema.SaveChangesAsync(Ct);
        return new Escenario(bd, a.Id, b.Id);
    }

    [Fact]
    public async Task Con_el_predicado_un_externo_de_A_solo_ve_los_saludos_de_A()
    {
        var e = await EscenarioAsync();
        await using var externoA = e.Bd.Hola(AlcanceDatos.DeOrganizaciones(e.OrgA));

        Assert.Equal(["Hola desde A"], await externoA.Saludos.Select(s => s.Texto).ToListAsync(Ct));
    }

    [Fact]
    public async Task Sin_el_predicado_RLS_sigue_ocultando_los_saludos_de_B()
    {
        var e = await EscenarioAsync();
        await using var externoA = e.Bd.Hola(AlcanceDatos.DeOrganizaciones(e.OrgA));

        var todos = await externoA.Saludos.IgnoreQueryFilters().Select(s => s.OrganizacionId).ToListAsync(Ct);

        Assert.Equal([e.OrgA], todos);
    }

    [Fact]
    public async Task RLS_impide_escribir_un_saludo_en_otra_organizacion()
    {
        var e = await EscenarioAsync();
        await using var externoA = e.Bd.Hola(AlcanceDatos.DeOrganizaciones(e.OrgA));
        externoA.Saludos.Add(new Saludo { OrganizacionId = e.OrgB, AutorPersonaId = Guid.NewGuid(), Texto = "Intruso" });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => externoA.SaveChangesAsync(Ct));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Sin_alcance_no_se_ve_ningun_saludo_y_el_sistema_los_ve_todos()
    {
        var e = await EscenarioAsync();
        await using var ninguno = e.Bd.Hola(AlcanceDatos.Ninguno);
        await using var sistema = e.Bd.Hola();

        Assert.Equal(0, await ninguno.Saludos.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(2, await sistema.Saludos.CountAsync(Ct));
    }

    [Fact]
    public async Task Un_saludo_no_puede_apuntar_a_una_organizacion_que_no_existe()
    {
        var e = await EscenarioAsync();
        await using var sistema = e.Bd.Hola();
        sistema.Saludos.Add(new Saludo { OrganizacionId = Guid.NewGuid(), AutorPersonaId = Guid.NewGuid(), Texto = "Huérfano" });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => sistema.SaveChangesAsync(Ct));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
