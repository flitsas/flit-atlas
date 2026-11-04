using Flit.Atlas.Modulos.Catalogo.Semilla;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Publicacion;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo.Tests;

/// <summary>
/// HU 3.10 (ATLAS-ADR-0035 §2): la lista blanca inicial trae una fila por aplicativo de FLIT 2.0 al que pertenecen los
/// contratos de <c>flitsas/flit</c>, con sus ramas, su workflow y sus tipos; se puede correr dos veces sin duplicar y
/// autoriza cada contrato de la matriz de <c>.github/workflows/atlas-publicar-contratos.yml</c>.
/// </summary>
public class SemillaFuentesTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string RepoFlit = "flitsas/flit";
    private const string WorkflowFlit = ".github/workflows/atlas-publicar-contratos.yml";

    /// <summary>Los 23 contratos de la matriz del workflow de <c>flit</c>: aplicativo y tipo.</summary>
    private static readonly (string Contrato, string Aplicativo, string Tipo)[] MatrizFlit =
    [
        ("core-api", "flit2-core-api", "openapi"),
        ("core-ict", "flit2-core-ict", "openapi"),
        ("external-api", "flit2-core-api", "openapi"),
        ("platform", "flit2-core-api", "openapi"),
        ("python-ml", "flit2-python-ml", "openapi"),
        ("consultas-events", "flit2-core-consultas", "asyncapi"),
        ("domain-events", "flit2-core-api", "asyncapi"),
        ("notificaciones-events", "flit2-core-notificaciones", "asyncapi"),
        ("plataforma-events", "flit2-core-identity", "asyncapi"),
        ("tramites-events", "flit2-core-api", "asyncapi"),
        ("comparendos", "flit2-core-comparendos", "proto"),
        ("consultas-administracion", "flit2-core-consultas", "proto"),
        ("consultas-certificaciones", "flit2-core-consultas", "proto"),
        ("consultas", "flit2-core-consultas", "proto"),
        ("consultas-fuentes-comparendos", "flit2-core-consultas", "proto"),
        ("consultas-resultado", "flit2-core-consultas", "proto"),
        ("consultas-validacion-identidad", "flit2-core-consultas", "proto"),
        ("identidad", "flit2-core-identity", "proto"),
        ("notificaciones-mensajes-muertos", "flit2-core-notificaciones", "proto"),
        ("notificaciones", "flit2-core-notificaciones", "proto"),
        ("platform-errores", "flit2-plataforma", "proto"),
        ("platform-tipos", "flit2-plataforma", "proto"),
        ("tramites-catalogos", "flit2-core-api", "proto"),
    ];

    [Fact]
    public void Trae_una_fila_de_flit_por_aplicativo_con_sus_ramas_su_workflow_y_sus_tipos()
    {
        var semilla = SemillaFuentes.Cargar();
        var deFlit = semilla.Fuentes.Where(f => f.Repositorio == RepoFlit).ToList();
        var aplicativos = SemillaCatalogo.Cargar().Aplicativos.ToDictionary(a => a.Slug);

        Assert.Equal(23, MatrizFlit.Length);
        // Una fila por aplicativo de la matriz, con exactamente los tipos de sus contratos.
        var esperados = MatrizFlit.GroupBy(c => c.Aplicativo)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Tipo).Distinct().Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        Assert.Equal(esperados.Keys.Order(StringComparer.Ordinal), deFlit.Select(f => f.Aplicativo).Order(StringComparer.Ordinal));
        Assert.All(deFlit, f =>
        {
            Assert.Equal(esperados[f.Aplicativo], f.Tipos.Order(StringComparer.Ordinal));
            // flit despliega develop → dev, staging → qa y release → pdn (cd.yml de flit), como los canales de Atlas.
            Assert.Equal(["develop", "staging", "release"], f.Ramas);
            Assert.Equal(WorkflowFlit, f.Workflow);
            // El aplicativo lo crea la semilla del catálogo y es de FLIT 2.0.
            Assert.True(aplicativos.TryGetValue(f.Aplicativo, out var aplicativo), f.Aplicativo);
            Assert.StartsWith("flit2-", aplicativo!.Slug, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Correrla_dos_veces_crea_las_filas_de_flit_sin_duplicar_y_autoriza_cada_contrato_de_la_matriz()
    {
        var bd = await contenedores.CrearBaseAsync();
        var fuentes = SemillaFuentes.Cargar();
        await using (var contexto = bd.Catalogo())
        {
            await SemillaCatalogo.AplicarAsync(contexto, SemillaCatalogo.Cargar(), Ct);
        }

        ResultadoSemillaFuentes primera, segunda;
        await using (var contexto = bd.Catalogo())
        {
            primera = await SemillaFuentes.AplicarAsync(contexto, fuentes, Ct);
        }
        await using (var contexto = bd.Catalogo())
        {
            segunda = await SemillaFuentes.AplicarAsync(contexto, fuentes, Ct);
        }

        Assert.Equal(new ResultadoSemillaFuentes(fuentes.Fuentes.Count, 0, 0), primera);
        Assert.Equal(new ResultadoSemillaFuentes(0, fuentes.Fuentes.Count, 0), segunda);

        await using var lectura = bd.Catalogo();
        Assert.Equal(fuentes.Fuentes.Count, await lectura.FuentesPublicacion.CountAsync(Ct));
        var filas = await lectura.FuentesPublicacion.AsNoTracking()
            .Where(f => f.Repositorio == RepoFlit)
            .Join(lectura.Aplicativos.AsNoTracking(), f => f.AplicativoId, a => a.Id, (f, a) => new { Fuente = f, a.Slug })
            .ToListAsync(Ct);
        Assert.Equal(8, filas.Count);
        Assert.Equal(filas.Count, filas.Select(f => f.Slug).Distinct(StringComparer.Ordinal).Count());

        var leidas = filas.Select(f => new FuentePublicacionLeida(
            f.Fuente.Id, f.Fuente.AplicativoId, f.Slug, f.Fuente.Repositorio, f.Fuente.Ramas, f.Fuente.Workflow, f.Fuente.Tipos)).ToList();
        foreach (var rama in new[] { "develop", "staging", "release" })
        {
            var publicador = Publicador(rama, WorkflowFlit);
            Assert.All(MatrizFlit, c =>
            {
                var autorizacion = ListaBlancaPublicacion.Autorizar(publicador, leidas, c.Aplicativo, c.Tipo);
                Assert.True(autorizacion.Fuente is not null, $"{c.Contrato} ({c.Aplicativo}, {c.Tipo}) en {rama}: {autorizacion.Mensaje}");
            });
        }
        // Otra rama u otro workflow del repo no publican.
        Assert.Equal("fuente_no_autorizada", ListaBlancaPublicacion.Autorizar(Publicador("main", WorkflowFlit), leidas, "flit2-core-api", "openapi").Codigo);
        Assert.Equal("fuente_no_autorizada",
            ListaBlancaPublicacion.Autorizar(Publicador("develop", ".github/workflows/ci.yml"), leidas, "flit2-core-api", "openapi").Codigo);
        // Ni un tipo que el aplicativo no tiene.
        Assert.Equal("tipo_no_autorizado", ListaBlancaPublicacion.Autorizar(Publicador("develop", WorkflowFlit), leidas, "flit2-core-ict", "proto").Codigo);
    }

    private static PublicadorCi Publicador(string rama, string workflow) => new(
        RepoFlit, "flitsas", rama, new string('a', 40), workflow,
        "flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v1", "push", "1", "1");
}
