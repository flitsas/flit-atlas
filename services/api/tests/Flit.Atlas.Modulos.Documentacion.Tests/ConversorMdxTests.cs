using Flit.Atlas.Modulos.Documentacion.Exportaciones;

namespace Flit.Atlas.Modulos.Documentacion.Tests;

/// <summary>El MDX a Markdown estático para exportar (ATLAS-ADR-0053 §7): nada se evalúa y el código queda intacto.</summary>
public class ConversorMdxTests
{
    private static Task<string> Convertir(string mdx, List<ComponenteVivo>? vistos = null) =>
        ConversorMdx.ConvertirAsync(mdx, c =>
        {
            vistos?.Add(c);
            return Task.FromResult($"[[{c.Nombre}]]");
        });

    [Fact]
    public async Task Un_componente_vivo_se_reemplaza_con_sus_atributos()
    {
        var vistos = new List<ComponenteVivo>();

        var salida = await Convertir("Antes\n\n<Contrato id=\"api-demo\" operacion='crear' />\n\n<SoporteDeTurno/>\n\nDespués", vistos);

        Assert.Equal("Antes\n\n[[Contrato]]\n\n[[SoporteDeTurno]]\n\nDespués\n", salida);
        Assert.Equal("api-demo", vistos[0].Atributos["id"]);
        Assert.Equal("crear", vistos[0].Atributos["operacion"]);
        Assert.Empty(vistos[1].Atributos);
    }

    [Fact]
    public async Task Los_bloques_y_el_codigo_en_linea_quedan_intactos()
    {
        const string Mdx = "Usa `<Contrato id=\"x\"/>` así:\n\n```mdx\n<Responsables aplicativo=\"flito\"/>\n```\n\n~~~\n<SoporteDeTurno/>\n~~~\n";
        var vistos = new List<ComponenteVivo>();

        Assert.Equal(Mdx, await Convertir(Mdx, vistos));
        Assert.Empty(vistos);
    }

    [Fact]
    public async Task Una_etiqueta_escapada_no_es_un_componente()
    {
        var vistos = new List<ComponenteVivo>();

        Assert.Equal("Escribe \\<Contrato/> así\n", await Convertir("Escribe \\<Contrato/> así", vistos));
        Assert.Empty(vistos);
    }

    [Theory]
    [InlineData("<Alerta tipo=\"peligro\">\n\nNo borres.\n\n</Alerta>", "**Cuidado**\n\n\nNo borres.\n")]
    [InlineData("<Alerta titulo=\"Ojo *fuerte*\">\n\nTexto.\n\n</Alerta>", "**Ojo \\*fuerte\\***\n\n\nTexto.\n")]
    [InlineData("<Alerta>\n\nTexto.\n\n</Alerta>", "**Nota**\n\n\nTexto.\n")]
    public async Task Una_alerta_deja_su_titulo_en_negrilla_y_su_contenido(string mdx, string esperado) =>
        Assert.Equal(esperado, await Convertir(mdx));

    [Fact]
    public async Task Las_pestanas_dejan_sus_titulos_y_su_contenido()
    {
        var salida = await Convertir("<Pestanas>\n\n## Web\n\nPágina.\n\n## Móvil\n\nApp.\n\n</Pestanas>\n");

        Assert.Contains("## Web\n\nPágina.", salida, StringComparison.Ordinal);
        Assert.Contains("## Móvil\n\nApp.", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("Pestanas", salida, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_etiqueta_fuera_de_la_lista_queda_como_texto()
    {
        var vistos = new List<ComponenteVivo>();

        // atlas-docs escapa el HTML al generar; aquí no se interpreta.
        Assert.Equal("<script>alert(1)</script>\n", await Convertir("<script>alert(1)</script>", vistos));
        Assert.Empty(vistos);
    }

    [Theory]
    [InlineData("Ana *la* [jefa]", "Ana \\*la\\* \\[jefa\\]")]
    [InlineData("<b>x</b>", "\\<b\\>x\\</b\\>")]
    [InlineData("a\nb", "a b")]
    public void Un_valor_no_se_lee_como_Markdown(string valor, string esperado) =>
        Assert.Equal(esperado, ConversorMdx.Escapar(valor));
}
