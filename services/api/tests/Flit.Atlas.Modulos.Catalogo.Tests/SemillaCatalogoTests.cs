using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Modulos.Catalogo.Semilla;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo.Tests;

/// <summary>
/// CA de la HU 2.1: la semilla se puede correr dos veces sin duplicar. Además: trae los productos y módulos del documento
/// de responsables y el inventario del plan 00 §2.1, no pisa lo editado, no revive lo eliminado y no trae personas.
/// </summary>
public partial class SemillaCatalogoTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Productos del §3.1 del documento de responsables (versión 5).</summary>
    private static readonly string[] ProductosDelDocumento =
        ["Vigía", "Certify", "FLITO", "Firmas", "Sentinel", "Atlas", "People", "flit-scheduler", "Infraestructura"];

    /// <summary>Módulos de FLIT 2 del §3.2 del documento de responsables.</summary>
    private static readonly string[] ModulosDeFlit2 =
    [
        "Trámites", "Diagnóstico", "Comparendos", "Administradores", "Usuarios", "Identidad", "Suite", "Migración", "Consultas",
        "Notificaciones", "ICT", "Integraciones", "Log", "Reportes", "Ayuda", "Doctor FLIT", "Plataforma",
    ];

    [Fact]
    public void La_semilla_es_valida_y_trae_el_documento_de_responsables_y_el_inventario_del_plan()
    {
        var semilla = SemillaCatalogo.Cargar();
        var porSlug = semilla.Aplicativos.ToDictionary(a => a.Slug);

        var productos = semilla.Aplicativos.Where(a => a.Tipo == "producto").Select(a => a.Nombre).ToList();
        Assert.All(ProductosDelDocumento, p => Assert.Contains(p, productos));
        Assert.Contains("FLIT 2.0", productos);

        var modulos = semilla.Aplicativos.Where(a => a.Tipo == "modulo" && a.Padre == "flit2").Select(a => a.Nombre).ToList();
        Assert.Equal(ModulosDeFlit2.Order(StringComparer.Ordinal), modulos.Order(StringComparer.Ordinal));

        // Plan 00 §2.1: los servicios de FLIT 2.0 (bajo el producto o uno de sus módulos), los 27 legados Back* y FrontNextJS.
        var deFlit2 = semilla.Aplicativos.Where(a => a.Tipo == "servicio" && (a.Padre == "flit2" || porSlug[a.Padre!].Padre == "flit2"))
            .Select(a => a.Nombre).ToList();
        Assert.All(["core-api", "core-identity", "core-consultas", "core-notificaciones", "core-comparendos", "core-ict", "python-ml",
            "frontend", "frontend-hub", "frontend-comparendos", "core-diagnostico", "frontend-diagnostico", "core-plantilla"],
            s => Assert.Contains(s, deFlit2));
        var legados = semilla.Aplicativos.Where(a => a.Padre == "legados-v1").Select(a => a.Nombre).ToList();
        Assert.Equal(27, legados.Count(n => n.StartsWith("Back", StringComparison.Ordinal)));
        Assert.Contains("FrontNextJS", legados);
        Assert.All(["flit-firmas", "flit-scheduler", "flit-certify", "flit-egress", "flitsas-maps", "flit-infra-studio", "flit-gitops"],
            s => Assert.Contains(semilla.Aplicativos, a => a.Nombre == s));
    }

    [Fact]
    public void Sentinel_y_People_traen_la_descripcion_que_dio_el_lider_y_lo_pendiente_queda_marcado()
    {
        var semilla = SemillaCatalogo.Cargar();
        string? Descripcion(string slug) => semilla.Aplicativos.Single(a => a.Slug == slug).Descripcion;

        // Confirmadas por el líder técnico (23/10/2026): ya no llevan la marca.
        Assert.StartsWith("Aplicativo para el líder técnico que gestiona los PR", Descripcion("sentinel"), StringComparison.Ordinal);
        Assert.Equal("Aplicativo interno para la gestión integral de empleados.", Descripcion("people"));
        Assert.All(["sentinel", "people"], slug => Assert.DoesNotContain(Valores.PorVerificar, Descripcion(slug), StringComparison.Ordinal));
        // Lo que sigue sin confirmar conserva la marca.
        Assert.All(["flitsas-maps", "infraestructura-flit-egress"], slug => Assert.Contains(Valores.PorVerificar, Descripcion(slug), StringComparison.Ordinal));
    }

    [Fact]
    public void La_semilla_no_trae_nombres_de_personas_ni_correos()
    {
        // Las personas del documento de responsables son la HU 2.3: aquí solo hay datos de la empresa.
        var texto = string.Join('\n', SemillaCatalogo.Cargar().Aplicativos.Select(a => $"{a.Nombre} {a.Descripcion} {string.Join(' ', a.Etiquetas ?? [])}"));
        foreach (var apellido in new[] { "Copete", "Montoya", "Mejía", "Chica", "Londoño", "Cárdenas", "Amado" })
        {
            // Palabra completa: «Programador» no es «Amado».
            Assert.DoesNotMatch(new Regex($@"\b{apellido}\b", RegexOptions.IgnoreCase), texto);
        }
        Assert.DoesNotMatch(Correo(), texto);
    }

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.]+")]
    private static partial Regex Correo();

    [Fact]
    public void Una_semilla_con_la_jerarquia_rota_falla_nombrando_la_entrada()
    {
        var mala = new ArchivoSemilla(1,
        [
            new EntradaSemilla("producto-demo", "Producto Demo", "producto", Grupo: "satelite"),
            new EntradaSemilla("servicio-demo", "Servicio Demo", "servicio", Padre: "producto-demo"),
            new EntradaSemilla("modulo-demo", "Módulo Demo", "modulo", Padre: "servicio-demo"),
        ]);

        var error = Assert.Throws<InvalidOperationException>(() => SemillaCatalogo.Validar(mala));

        Assert.Contains("modulo-demo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correrla_dos_veces_no_duplica_nada()
    {
        var bd = await contenedores.CrearBaseAsync();
        var semilla = SemillaCatalogo.Cargar();

        ResultadoSemilla primera, segunda;
        await using (var contexto = bd.Catalogo())
        {
            primera = await SemillaCatalogo.AplicarAsync(contexto, semilla, Ct);
        }
        await using (var contexto = bd.Catalogo())
        {
            segunda = await SemillaCatalogo.AplicarAsync(contexto, semilla, Ct);
        }

        await using var lectura = bd.Catalogo();
        Assert.Equal(semilla.Aplicativos.Count, primera.Creados);
        Assert.Equal(new ResultadoSemilla(0, semilla.Aplicativos.Count, 0, 0), segunda);
        Assert.Equal(semilla.Aplicativos.Count, await lectura.Aplicativos.CountAsync(Ct));
        Assert.Equal(semilla.Aplicativos.Count, await lectura.SemillaAplicada.CountAsync(Ct));
        // Los hijos heredan el grupo de su producto.
        var comparendos = await lectura.Aplicativos.SingleAsync(a => a.Slug == "flit2-core-comparendos", Ct);
        Assert.Equal(GrupoAplicativo.Flit2, comparendos.Grupo);
        Assert.Equal((await lectura.Aplicativos.SingleAsync(a => a.Slug == "flit2-comparendos", Ct)).Id, comparendos.PadreId);
    }

    [Fact]
    public async Task No_pisa_lo_editado_no_revive_lo_eliminado_y_respeta_lo_creado_a_mano()
    {
        var bd = await contenedores.CrearBaseAsync();
        var semilla = SemillaCatalogo.Cargar();
        await using (var manual = bd.Catalogo())
        {
            // Alguien creó «vigia» a mano antes de la primera corrida.
            manual.Aplicativos.Add(new Aplicativo { Slug = "vigia", Nombre = "Vigía (a mano)", Tipo = TipoAplicativo.Producto, Grupo = GrupoAplicativo.Infraestructura });
            await manual.SaveChangesAsync(Ct);
        }
        await using (var contexto = bd.Catalogo())
        {
            var primera = await SemillaCatalogo.AplicarAsync(contexto, semilla, Ct);
            Assert.Equal(1, primera.Existentes);
        }
        await using (var cambios = bd.Catalogo())
        {
            (await cambios.Aplicativos.SingleAsync(a => a.Slug == "flito", Ct)).Descripcion = "Editada en Atlas.";
            cambios.Aplicativos.Remove(await cambios.Aplicativos.SingleAsync(a => a.Slug == "flit2-paquete-ui", Ct));
            await cambios.SaveChangesAsync(Ct);
        }
        await using (var contexto = bd.Catalogo())
        {
            await SemillaCatalogo.AplicarAsync(contexto, semilla, Ct);
        }

        await using var lectura = bd.Catalogo();
        Assert.Equal("Editada en Atlas.", (await lectura.Aplicativos.SingleAsync(a => a.Slug == "flito", Ct)).Descripcion);
        Assert.False(await lectura.Aplicativos.AnyAsync(a => a.Slug == "flit2-paquete-ui", Ct));
        var vigia = await lectura.Aplicativos.SingleAsync(a => a.Slug == "vigia", Ct);
        Assert.Equal("Vigía (a mano)", vigia.Nombre);
        // Su servicio de la semilla cuelga del creado a mano y toma su grupo.
        Assert.Equal(GrupoAplicativo.Infraestructura, (await lectura.Aplicativos.SingleAsync(a => a.Slug == "vigia-flit-infra-studio", Ct)).Grupo);
    }

    [Fact]
    public async Task Una_entrada_cuyo_padre_se_elimino_se_omite_y_se_reintenta()
    {
        var bd = await contenedores.CrearBaseAsync();
        var v1 = new ArchivoSemilla(1, [new EntradaSemilla("producto-demo", "Producto Demo", "producto", Grupo: "satelite")]);
        var v2 = new ArchivoSemilla(2,
        [
            .. v1.Aplicativos,
            new EntradaSemilla("producto-demo-api", "API Demo", "servicio", Padre: "producto-demo"),
        ]);
        await using (var contexto = bd.Catalogo())
        {
            await SemillaCatalogo.AplicarAsync(contexto, v1, Ct);
        }
        await using (var borrar = bd.Catalogo())
        {
            borrar.Aplicativos.Remove(await borrar.Aplicativos.SingleAsync(Ct));
            await borrar.SaveChangesAsync(Ct);
        }

        await using var nuevo = bd.Catalogo();
        var resultado = await SemillaCatalogo.AplicarAsync(nuevo, v2, Ct);

        Assert.Equal(new ResultadoSemilla(0, 1, 0, 1), resultado);
        Assert.False(await nuevo.Aplicativos.AnyAsync(Ct));
        Assert.False(await nuevo.SemillaAplicada.AnyAsync(s => s.Slug == "producto-demo-api", Ct));
    }
}
