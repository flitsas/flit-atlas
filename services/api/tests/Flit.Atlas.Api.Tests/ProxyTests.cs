using Microsoft.Extensions.Configuration;

namespace Flit.Atlas.Api.Tests;

public class ProxyTests
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
}
