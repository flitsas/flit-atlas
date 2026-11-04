using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Equipo.Tests;

/// <summary>
/// El módulo cumple el contrato de módulo (ATLAS-ADR-0026) y su modelo de datos (ATLAS-ADR-0031), probado con el usuario
/// del servicio <c>flit_atlas</c>: tablas <c>equipo_*</c>, llaves hacia <c>plataforma_persona</c>, una sola principal y una
/// sola secundaria por aplicativo, una sola revisión pendiente y las columnas con datos personales marcadas.
/// </summary>
public class ModuloEquipoTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo_y_publica_los_permisos_base_del_nucleo()
    {
        var modulo = new ModuloEquipo();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("equipo", modulo.Id);
        Assert.True(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(EquipoDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_equipo", EquipoDbContext.TablaHistorial);
        Assert.Equal([PermisosEquipo.Ver, PermisosEquipo.Gestionar, PermisosEquipo.VerContacto], modulo.Permisos.Permisos.Select(p => p.Codigo));
        Assert.Equal(ManifiestosNucleo.Equipo.Permisos, modulo.Permisos.Permisos);
        Assert.Equal("equipo.revision_trimestral", Assert.Single(modulo.TiposNotificacion).Codigo);
        // El núcleo ya no registra «equipo»: si lo hiciera, el catálogo fallaría por el módulo repetido.
        Assert.DoesNotContain(ManifiestosNucleo.Todos, m => m.Modulo == "equipo");
        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, modulo.Permisos]);
        Assert.Contains(PermisosEquipo.Ver, catalogo.PermisosDe(RolesAtlas.Lector));
        Assert.Contains(PermisosEquipo.Gestionar, catalogo.PermisosDe(RolesAtlas.LiderTecnico));
        Assert.All(RolesAtlas.Externos, r => Assert.DoesNotContain(PermisosEquipo.Ver, catalogo.PermisosDe(r)));
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Equipo();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Equipo -o Datos/Migraciones");
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

        await using var equipo = bd.Equipo();
        await equipo.Database.MigrateAsync(Ct);
        await equipo.Database.MigrateAsync(Ct);

        Assert.Empty(await equipo.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(
            ["__ef_migraciones_equipo", "equipo_responsabilidad", "equipo_responsabilidad_version", "equipo_revision_programada", "equipo_traspaso_checklist", "equipo_usuario_github"],
            (await Tablas(bd)).Except(delNucleo).Order(StringComparer.Ordinal));
    }

    private static Task<List<string>> Tablas(BaseDePrueba bd) =>
        Contenedores.ConsultarAsync(bd.Servicio, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'atlas'");

    [Fact]
    public async Task Las_columnas_con_datos_personales_estan_marcadas()
    {
        var bd = await contenedores.CrearBaseAsync();

        var pii = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT c.relname || '.' || a.attname FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'atlas'
            WHERE c.relname LIKE 'equipo_%' AND col_description(a.attrelid, a.attnum) = '@pii'
            """);

        Assert.Equal(
        [
            "equipo_responsabilidad_version.contenido", "equipo_responsabilidad_version.definido_por", "equipo_responsabilidad_version.nota",
            "equipo_revision_programada.nota", "equipo_traspaso_checklist.nota", "equipo_usuario_github.usuario",
        ], pii.Order(StringComparer.Ordinal));
    }

    private static async Task<Persona> PersonaAsync(BaseDePrueba bd)
    {
        await using var plataforma = bd.Plataforma();
        var organizacion = new Organizacion { Nombre = "FLIT Demo", Tipo = TipoOrganizacion.Interna };
        var persona = new Persona
        {
            Tipo = TipoPersona.Empleado, OrganizacionId = organizacion.Id, Nombre = "Persona Demo", Correo = $"demo.{Guid.NewGuid():N}@ejemplo.com",
        };
        plataforma.AddRange(organizacion, persona);
        await plataforma.SaveChangesAsync(Ct);
        return persona;
    }

    private static Responsabilidad Nueva(Guid aplicativo, Guid persona, TipoResponsabilidad tipo = TipoResponsabilidad.Principal) => new()
    {
        AplicativoId = aplicativo, Tipo = tipo, PersonaId = persona, VigenteDesde = new DateOnly(2026, 10, 20),
    };

    [Fact]
    public async Task Una_responsabilidad_no_puede_apuntar_a_una_persona_que_no_existe()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var equipo = bd.Equipo();

        equipo.Responsabilidades.Add(Nueva(Guid.CreateVersion7(), Guid.CreateVersion7()));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => equipo.SaveChangesAsync(Ct));
        Assert.Contains("fk_equipo_responsabilidad_plataforma_persona_persona_id", error.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hay_a_lo_sumo_una_principal_y_una_secundaria_por_aplicativo()
    {
        var bd = await contenedores.CrearBaseAsync();
        var persona = await PersonaAsync(bd);
        var aplicativo = Guid.CreateVersion7();
        await using var equipo = bd.Equipo();
        equipo.Responsabilidades.AddRange(Nueva(aplicativo, persona.Id), Nueva(aplicativo, persona.Id, TipoResponsabilidad.Secundario));
        await equipo.SaveChangesAsync(Ct);

        await using var otra = bd.Equipo();
        otra.Responsabilidades.Add(Nueva(aplicativo, persona.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => otra.SaveChangesAsync(Ct));

        Assert.Equal(["principal", "secundario"],
            await Contenedores.ConsultarAsync(bd.Servicio, "SELECT tipo FROM atlas.equipo_responsabilidad ORDER BY tipo"));
    }

    [Fact]
    public async Task Solo_hay_una_revision_pendiente_y_las_versiones_no_repiten_numero()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var equipo = bd.Equipo();
        equipo.Revisiones.Add(new RevisionProgramada { Fecha = new DateOnly(2027, 1, 20) });
        await equipo.SaveChangesAsync(Ct);

        await using var otra = bd.Equipo();
        otra.Revisiones.Add(new RevisionProgramada { Fecha = new DateOnly(2027, 4, 20) });
        await Assert.ThrowsAsync<DbUpdateException>(() => otra.SaveChangesAsync(Ct));

        await using var versiones = bd.Equipo();
        versiones.Versiones.AddRange(Version(5), Version(5));
        await Assert.ThrowsAsync<DbUpdateException>(() => versiones.SaveChangesAsync(Ct));
    }

    private static VersionReparto Version(int numero) => new()
    {
        Numero = numero, Origen = OrigenVersion.Importacion, Nota = "Prueba", Definiciones = "[]", Contenido = new FotoReparto([]).AJson(),
        Huella = new FotoReparto([]).Huella(),
    };
}
