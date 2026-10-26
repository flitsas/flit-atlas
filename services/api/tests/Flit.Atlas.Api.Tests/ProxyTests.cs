using Microsoft.Extensions.Configuration;

namespace Flit.Atlas.Api.Tests;

public class ProxyTests(EntornoApi entorno)
{
    private static IConfiguration Con(string? redes) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [Dependencias.ProxyRedesConfiables] = redes })
            .Build();

    [Fact]
    public void Sin_redes_configuradas_solo_se_confia_en_loopback()
    {
        Assert.Empty(Dependencias.RedesConfiables(Con(null)));
        Assert.Empty(Dependencias.RedesConfiables(Con("  ")));
    }

    [Fact]
    public void Las_redes_se_leen_separadas_por_coma()
    {
        var redes = Dependencias.RedesConfiables(Con("192.0.2.0/24, 2001:db8::/32"));

        Assert.Equal(["192.0.2.0/24", "2001:db8::/32"], redes.Select(r => r.ToString()));
    }

    [Fact]
    public void Una_red_invalida_detiene_el_arranque_nombrando_la_variable()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Dependencias.RedesConfiables(Con("192.0.2.0/24,no-es-red")));

        Assert.Contains("Atlas__Proxy__RedesConfiables", error.Message, StringComparison.Ordinal);
        Assert.Contains("no-es-red", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("qa")]
    [InlineData("pdn")]
    public void En_qa_y_pdn_sin_redes_confiables_el_arranque_falla_con_un_mensaje_claro(string ambiente)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Dependencias.ExigirRedesConfiables(Con(null), ambiente));

        Assert.Contains("Atlas__Proxy__RedesConfiables", error.Message, StringComparison.Ordinal);
        Assert.Contains("X-Forwarded-For", error.Message, StringComparison.Ordinal);
        Dependencias.ExigirRedesConfiables(Con("192.0.2.0/24"), ambiente);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("dev")]
    public void En_local_y_dev_basta_el_loopback(string ambiente) => Dependencias.ExigirRedesConfiables(Con(null), ambiente);

    /// <summary>F4-SEC-13 (ATLAS-ADR-0049): atlas-api de qa no arranca sin Atlas__Proxy__RedesConfiables.</summary>
    [Fact]
    public void Atlas_api_de_qa_sin_redes_confiables_no_arranca()
    {
        using var api = entorno.Api("qa", simulado: false, configuracion: new Dictionary<string, string> { [Dependencias.ProxyRedesConfiables] = "" });

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());

        Assert.Contains("Atlas__Proxy__RedesConfiables", error.Message, StringComparison.Ordinal);
    }
}
