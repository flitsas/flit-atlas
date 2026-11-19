using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Guias;
using Flit.Atlas.Modulos.Contratos.Specs;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>
/// Reglas de las guías (HU 3.7, ATLAS-ADR-0040): validación, versiones a las que aplican, huella, quién edita y quién
/// aprueba lo compartible, y la inmutabilidad de las revisiones en la base.
/// </summary>
public class GuiasTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.0-dev.1", "1.0.0", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1", 0)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void Compara_versiones_con_la_precedencia_de_semver(string a, string b, int esperado)
    {
        Assert.Equal(esperado, Math.Sign(VersionSemantica.Comparar(VersionSemantica.Leer(a)!, VersionSemantica.Leer(b)!)));
        Assert.Equal(-esperado, Math.Sign(VersionSemantica.Comparar(VersionSemantica.Leer(b)!, VersionSemantica.Leer(a)!)));
    }

    [Theory]
    [InlineData(null, null, "0.1.0", true)]
    [InlineData("2.0.0", null, "1.9.9", false)]
    [InlineData("2.0.0", null, "2.0.0", true)]
    [InlineData(null, "2.0.0", "2.0.0", true)]
    [InlineData(null, "2.0.0", "2.0.1-dev.1", false)]
    [InlineData("2.0.0", "2.9.9", "2.4.0-rc.1", true)]
    [InlineData("2.0.0", "2.9.9", "2.0.0-dev.1", false)]
    public void Una_guia_aplica_a_las_versiones_de_su_rango_o_a_todas(string? desde, string? hasta, string version, bool aplica) =>
        Assert.Equal(aplica, ReglasGuias.AplicaA(desde, hasta, VersionSemantica.Leer(version)!));

    [Fact]
    public void Normaliza_lo_que_llega_y_la_huella_ignora_la_nota()
    {
        var (foto, nota, errores) = ReglasGuias.Leer("  Cómo autenticarse  ", "# Paso 1\r\nPide el token.\r\n", "  ", " 1.2.0 ", "", "interna", " Primera versión ");

        Assert.Empty(errores);
        Assert.Equal(("Cómo autenticarse", "# Paso 1\nPide el token.\n", null, "1.2.0", null, VisibilidadGuia.Interna),
            (foto!.Titulo, foto.Contenido, foto.Operacion, foto.AplicaDesde, foto.AplicaHasta, foto.Visibilidad));
        Assert.Equal("Primera versión", nota);
        var otraNota = ReglasGuias.Leer("Cómo autenticarse", "# Paso 1\nPide el token.\n", null, "1.2.0", null, "interna", "Otra nota").Foto!;
        Assert.Equal(foto.Huella(), otraNota.Huella());
        Assert.Matches("^[0-9a-f]{64}$", foto.Huella());
        Assert.NotEqual(foto.Huella(), (foto with { Visibilidad = VisibilidadGuia.Compartible }).Huella());
    }

    [Fact]
    public void Rechaza_campos_vacios_versiones_invalidas_un_rango_al_reves_y_visibilidades_desconocidas()
    {
        var (foto, _, errores) = ReglasGuias.Leer(" ", "  ", null, "uno", "1.0.0", "publica", "");

        Assert.Null(foto);
        Assert.Equal(["aplicaDesde", "contenido", "nota", "titulo", "visibilidad"], errores.Keys.Order(StringComparer.Ordinal));
        var alReves = ReglasGuias.Leer("T", "C", null, "2.0.0", "1.0.0", "compartible", "n").Errores;
        Assert.Equal(["aplicaHasta"], alReves.Keys);
        var largo = ReglasGuias.Leer(new string('t', ContratosDbContext.LargoTituloGuia + 1), new string('c', ContratosDbContext.LargoContenidoGuia + 1),
            null, null, null, "interna", new string('n', ContratosDbContext.LargoNotaGuia + 1)).Errores;
        Assert.Equal(["contenido", "nota", "titulo"], largo.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Edita_el_lider_o_el_responsable_del_aplicativo_o_de_un_ancestro_y_aprueba_solo_el_responsable()
    {
        var producto = new AplicativoLeido(Guid.CreateVersion7(), "producto-demo", "Producto Demo", "producto", null, "satelite", "activo", null, null);
        var servicio = new AplicativoLeido(Guid.CreateVersion7(), "servicio-demo", "Servicio Demo", "servicio", producto.Id, "satelite", "activo", null, null);
        var otro = new AplicativoLeido(Guid.CreateVersion7(), "otro-demo", "Otro Demo", "producto", null, "satelite", "activo", null, null);
        var aplicativos = new[] { producto, servicio, otro }.ToDictionary(a => a.Id);
        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, new ModuloContratos().Permisos]);

        PermisosEfectivos Con(params ConcesionRol[] concesiones) => PermisosEfectivos.Desde(catalogo, concesiones);
        ConcesionRol Global(string rol) => new(rol, null, AlcanceAsignacion.Global, null, "manual");
        ConcesionRol ResponsableDe(Guid id) => new(RolesAtlas.Responsable, null, new AlcanceAsignacion(TiposAlcance.Aplicativo, id.ToString("D")), null, "implicito");

        var lider = Con(Global(RolesAtlas.Lector), Global(RolesAtlas.LiderTecnico));
        var desarrollador = Con(Global(RolesAtlas.Lector), Global(RolesAtlas.Desarrollador));
        var delProducto = Con(Global(RolesAtlas.Lector), Global(RolesAtlas.Desarrollador), ResponsableDe(producto.Id));
        var delOtro = Con(Global(RolesAtlas.Lector), ResponsableDe(otro.Id));
        var superadmin = Con(Global(RolesAtlas.Superadmin));

        Assert.Equal((true, false), (ReglasGuias.PuedeEditar(lider, servicio.Id, aplicativos), ReglasGuias.PuedeAprobar(lider, servicio.Id, aplicativos)));
        Assert.Equal((false, false), (ReglasGuias.PuedeEditar(desarrollador, servicio.Id, aplicativos), ReglasGuias.PuedeAprobar(desarrollador, servicio.Id, aplicativos)));
        // El responsable del producto edita y aprueba las guías de los contratos de su servicio, no las de otro producto.
        Assert.Equal((true, true), (ReglasGuias.PuedeEditar(delProducto, servicio.Id, aplicativos), ReglasGuias.PuedeAprobar(delProducto, servicio.Id, aplicativos)));
        Assert.Equal((false, false), (ReglasGuias.PuedeEditar(delOtro, servicio.Id, aplicativos), ReglasGuias.PuedeAprobar(delOtro, servicio.Id, aplicativos)));
        // §7.2: lo compartible lo aprueba el principal o el secundario; ni el superadministrador lo hace sin serlo.
        Assert.Equal((true, false), (ReglasGuias.PuedeEditar(superadmin, servicio.Id, aplicativos), ReglasGuias.PuedeAprobar(superadmin, servicio.Id, aplicativos)));
    }

    [Fact]
    public async Task La_base_rechaza_cambiar_una_revision_y_un_numero_repetido()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Contratos();
        var contrato = new Contrato
        {
            AplicativoId = Guid.NewGuid(), Codigo = "api-guias", Tipo = TipoContrato.Openapi, Nombre = "API Demo", Repositorio = "flitsas-demo/api-guias",
        };
        var guia = new GuiaContrato { ContratoId = contrato.Id, Titulo = "Guía demo", RevisionActual = 1 };
        contexto.AddRange(contrato, guia, Revision(guia.Id, 1));
        await contexto.SaveChangesAsync(Ct);

        foreach (var cambio in new[] { "contenido = 'otro'", "nota = 'otra'", "huella = repeat('b', 64)", "numero = 9" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_guia_revision SET {cambio} WHERE guia_id = '{guia.Id}'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }

        contexto.RevisionesGuia.Add(Revision(guia.Id, 1));
        var repetida = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (repetida.InnerException as PostgresException)?.SqlState);

        // La revisión publicada no puede ser una que no existe.
        var publicada = await Assert.ThrowsAsync<PostgresException>(() =>
            Contenedores.EjecutarAsync(bd.Servicio, $"UPDATE atlas.contratos_guia SET revision_publicada = 2 WHERE id = '{guia.Id}'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, publicada.SqlState);
    }

    private static RevisionGuia Revision(Guid guia, int numero)
    {
        var foto = new FotoGuia("Guía demo", "# Demo", null, null, null, VisibilidadGuia.Interna);
        return new RevisionGuia
        {
            GuiaId = guia, Numero = numero, Origen = OrigenRevision.Creacion, Titulo = foto.Titulo, Contenido = foto.Contenido,
            Visibilidad = foto.Visibilidad, Nota = "Creación", AutorPersonaId = Guid.NewGuid(), GuardadaEn = DateTimeOffset.UtcNow, Huella = foto.Huella(),
        };
    }
}
