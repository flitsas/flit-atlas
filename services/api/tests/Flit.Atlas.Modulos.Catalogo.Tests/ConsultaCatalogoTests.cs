using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Modulos.Catalogo.Semilla;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo.Tests;

/// <summary>CA de la HU 2.1: búsqueda y filtros por grupo, estado y criticidad, resueltos en PostgreSQL.</summary>
public class ConsultaCatalogoTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<BaseDePrueba> SembradaAsync()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Catalogo();
        await SemillaCatalogo.AplicarAsync(contexto, SemillaCatalogo.Cargar(), Ct);
        return bd;
    }

    private static async Task<List<string>> SlugsAsync(BaseDePrueba bd, FiltroCatalogo filtro)
    {
        await using var contexto = bd.Catalogo();
        return await ConsultaCatalogo.Filtrar(contexto.Aplicativos, filtro).Select(a => a.Slug).OrderBy(s => s).ToListAsync(Ct);
    }

    [Fact]
    public async Task El_texto_no_distingue_tildes_ni_mayusculas_y_busca_en_nombre_descripcion_repo_y_etiquetas()
    {
        var bd = await SembradaAsync();

        Assert.Equal(["flit2-tramites"], await SlugsAsync(bd, new FiltroCatalogo(Texto: "TRAMITES")));
        Assert.Contains("flit2-diagnostico", await SlugsAsync(bd, new FiltroCatalogo(Texto: "diagnóstico")));
        // Descripción.
        Assert.Equal(["sentinel"], await SlugsAsync(bd, new FiltroCatalogo(Texto: "whatsapp")));
        Assert.Equal(["flitsas-maps", "infraestructura-flit-egress"], await SlugsAsync(bd, new FiltroCatalogo(Texto: "[por verificar]")));
        // Repo.
        Assert.Contains("flito-api", await SlugsAsync(bd, new FiltroCatalogo(Texto: "flitsas/flito")));
        // Etiqueta exacta.
        Assert.Contains("legado-back-api-external-auth", await SlugsAsync(bd, new FiltroCatalogo(Texto: "api-externa")));
        // Los comodines de LIKE se buscan como texto.
        Assert.Empty(await SlugsAsync(bd, new FiltroCatalogo(Texto: "%")));
    }

    [Fact]
    public async Task Filtra_por_grupo_estado_criticidad_y_tipo_combinables()
    {
        var bd = await SembradaAsync();
        var semilla = SemillaCatalogo.Cargar();

        var legados = await SlugsAsync(bd, new FiltroCatalogo(Grupos: [GrupoAplicativo.Legado]));
        var planeados = await SlugsAsync(bd, new FiltroCatalogo(Estados: [EstadoAplicativo.Planeado]));
        var productos = await SlugsAsync(bd, new FiltroCatalogo(Tipos: [TipoAplicativo.Producto]));
        var satelitesYInfra = await SlugsAsync(bd, new FiltroCatalogo(Grupos: [GrupoAplicativo.Satelite, GrupoAplicativo.Infraestructura], Tipos: [TipoAplicativo.Producto]));

        Assert.Equal(29, legados.Count);
        Assert.Contains("atlas-render", planeados);
        Assert.Contains("flit2-diagnostico", planeados);
        Assert.DoesNotContain("flit2-tramites", planeados);
        Assert.Equal(semilla.Aplicativos.Count(a => a.Tipo == "producto"), productos.Count);
        Assert.Contains("infraestructura", satelitesYInfra);
        Assert.DoesNotContain("flit2", satelitesYInfra);

        await using (var cambio = bd.Catalogo())
        {
            (await cambio.Aplicativos.SingleAsync(a => a.Slug == "flit2-identidad", Ct)).Criticidad = Criticidad.Alta;
            await cambio.SaveChangesAsync(Ct);
        }
        Assert.Equal(["flit2-identidad"], await SlugsAsync(bd, new FiltroCatalogo(Criticidades: [Criticidad.Alta])));
        Assert.Equal(["flit2-identidad"], await SlugsAsync(bd, new FiltroCatalogo(Texto: "identidad", Criticidades: [Criticidad.Alta], Grupos: [GrupoAplicativo.Flit2])));
        Assert.Empty(await SlugsAsync(bd, new FiltroCatalogo(Criticidades: [Criticidad.Alta], Grupos: [GrupoAplicativo.Legado])));
    }

    [Fact]
    public async Task La_jerarquia_arma_la_ruta_y_los_descendientes()
    {
        var bd = await SembradaAsync();
        await using var contexto = bd.Catalogo();
        var jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, Ct);
        var servicio = await contexto.Aplicativos.SingleAsync(a => a.Slug == "flit2-core-identity", Ct);
        var producto = await contexto.Aplicativos.SingleAsync(a => a.Slug == "flit2", Ct);

        Assert.Equal(["flit2", "flit2-identidad"], jerarquia.Ancestros(servicio.Id).Select(a => a.Slug));
        Assert.Equal("flit 2.0 › identidad › core-identity", jerarquia.Ruta(servicio.Id, servicio.Nombre));
        Assert.Contains(servicio.Id, jerarquia.Descendientes(producto.Id));
        Assert.Equal(0, jerarquia.Hijos(servicio.Id));
    }
}
