using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Catalogo.Tests;

/// <summary>
/// El catálogo cumple el contrato de módulo (ATLAS-ADR-0016 y ATLAS-ADR-0026) y sus tablas se migran con el usuario del
/// servicio <c>flit_atlas</c> después del núcleo (ATLAS-ADR-0028).
/// </summary>
public class ModuloCatalogoTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Cumple_el_contrato_de_modulo_y_usa_los_permisos_de_la_lista_base()
    {
        var modulo = new ModuloCatalogo();

        ExtensionesModulos.Validar([modulo]);
        Assert.Equal("catalogo", modulo.Id);
        Assert.True(modulo.ActivoPorDefecto);
        Assert.Equal(typeof(CatalogoDbContext), modulo.ContextoDatos);
        Assert.Equal("__ef_migraciones_catalogo", CatalogoDbContext.TablaHistorial);
        Assert.Equal(["atlas.catalogo.ver", "atlas.catalogo.gestionar", "atlas.catalogo.editar"], modulo.Permisos.Permisos.Select(p => p.Codigo));
        Assert.Equal([TiposAlcance.Aplicativo], modulo.Permisos.TiposAlcance);
        // Las concesiones de la lista base no cambian al mudarse a su módulo.
        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, modulo.Permisos]);
        Assert.All(RolesAtlas.Empleados, r => Assert.Contains(PermisosCatalogo.Ver, catalogo.PermisosDe(r)));
        Assert.All(RolesAtlas.Externos, r => Assert.DoesNotContain(catalogo.PermisosDe(r), p => p.StartsWith("atlas.catalogo.", StringComparison.Ordinal)));
        Assert.Equal([RolesAtlas.LiderTecnico], modulo.Permisos.Permisos.Single(p => p.Codigo == PermisosCatalogo.Gestionar).ConcedidoA);
        Assert.Equal([RolesAtlas.LiderTecnico, RolesAtlas.Responsable], modulo.Permisos.Permisos.Single(p => p.Codigo == PermisosCatalogo.Editar).ConcedidoA);
    }

    [Fact]
    public void El_nucleo_ya_no_declara_el_catalogo_para_que_no_haya_dos_manifiestos()
    {
        Assert.DoesNotContain(ManifiestosNucleo.Todos, m => m.Modulo == ModuloCatalogo.IdModulo);
    }

    [Fact]
    public async Task El_modelo_no_tiene_cambios_sin_migracion()
    {
        await using var contexto = new BaseDePrueba("Host=x", "Host=x").Catalogo();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "Hay cambios en el modelo sin migración: dotnet ef migrations add <Nombre> -p services/api/src/Flit.Atlas.Modulos.Catalogo -o Datos/Migraciones");
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

        await using var catalogo = bd.Catalogo();
        await catalogo.Database.MigrateAsync(Ct);
        await catalogo.Database.MigrateAsync(Ct);

        Assert.Empty(await catalogo.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(["__ef_migraciones_catalogo", "catalogo_aplicativo", "catalogo_fuente_publicacion", "catalogo_semilla"], (await Tablas(bd)).Except(delNucleo).Order(StringComparer.Ordinal));
    }

    private static Task<List<string>> Tablas(BaseDePrueba bd) =>
        Contenedores.ConsultarAsync(bd.Servicio, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'atlas'");

    [Fact]
    public async Task La_base_rechaza_un_producto_con_padre_un_hijo_sin_padre_y_un_slug_invalido()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Catalogo();
        var producto = new Aplicativo { Slug = "producto-demo", Nombre = "Producto Demo", Tipo = TipoAplicativo.Producto, Grupo = GrupoAplicativo.Satelite };
        contexto.Aplicativos.Add(producto);
        await contexto.SaveChangesAsync(Ct);

        async Task<string> FallaAsync(Aplicativo aplicativo)
        {
            await using var otro = bd.Catalogo();
            otro.Aplicativos.Add(aplicativo);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => otro.SaveChangesAsync(Ct));
            return Assert.IsType<PostgresException>(error.InnerException).SqlState;
        }

        Assert.Equal(PostgresErrorCodes.CheckViolation, await FallaAsync(new Aplicativo
        {
            Slug = "con-padre", Nombre = "Con padre", Tipo = TipoAplicativo.Producto, Grupo = GrupoAplicativo.Satelite, PadreId = producto.Id,
        }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await FallaAsync(new Aplicativo
        {
            Slug = "huerfano", Nombre = "Huérfano", Tipo = TipoAplicativo.Servicio, Grupo = GrupoAplicativo.Satelite,
        }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await FallaAsync(new Aplicativo
        {
            Slug = "Con Espacios", Nombre = "Slug inválido", Tipo = TipoAplicativo.Producto, Grupo = GrupoAplicativo.Satelite,
        }));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, await FallaAsync(new Aplicativo
        {
            Slug = "producto-demo", Nombre = "Repetido", Tipo = TipoAplicativo.Producto, Grupo = GrupoAplicativo.Satelite,
        }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await FallaAsync(new Aplicativo
        {
            Slug = "padre-inexistente", Nombre = "Padre inexistente", Tipo = TipoAplicativo.Modulo, Grupo = GrupoAplicativo.Satelite, PadreId = Guid.NewGuid(),
        }));
    }

    [Fact]
    public async Task Un_padre_con_hijos_no_se_puede_borrar_en_la_base()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Catalogo();
        var producto = new Aplicativo { Slug = "producto-demo", Nombre = "Producto Demo", Tipo = TipoAplicativo.Producto, Grupo = GrupoAplicativo.Satelite };
        var modulo = new Aplicativo { Slug = "modulo-demo", Nombre = "Módulo Demo", Tipo = TipoAplicativo.Modulo, Grupo = GrupoAplicativo.Satelite, PadreId = producto.Id };
        contexto.Aplicativos.AddRange(producto, modulo);
        await contexto.SaveChangesAsync(Ct);

        await using var otro = bd.Catalogo();
        otro.Aplicativos.Remove(await otro.Aplicativos.SingleAsync(a => a.Id == producto.Id, Ct));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => otro.SaveChangesAsync(Ct));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
