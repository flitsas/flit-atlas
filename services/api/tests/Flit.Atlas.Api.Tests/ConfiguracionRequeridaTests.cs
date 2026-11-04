using Flit.Atlas.ServiceDefaults;
using Microsoft.Extensions.Configuration;

namespace Flit.Atlas.Api.Tests;

public class ConfiguracionRequeridaTests
{
    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    [Fact]
    public void Con_todas_las_claves_no_falla()
    {
        var config = Config(new() { ["ConnectionStrings:atlas"] = "Host=x", ["ConnectionStrings:redis"] = "x" });

        ConfiguracionRequerida.Validar(config, "ConnectionStrings:atlas", "ConnectionStrings:redis");
    }

    [Fact]
    public void Nombra_todas_las_claves_que_faltan_como_variables_de_entorno()
    {
        var config = Config(new() { ["ConnectionStrings:atlas"] = "Host=x", ["ConnectionStrings:redis"] = "  " });

        var ex = Assert.Throws<ConfiguracionFaltanteException>(() =>
            ConfiguracionRequerida.Validar(config, "ConnectionStrings:atlas", "ConnectionStrings:redis", "ConnectionStrings:rabbitmq"));

        Assert.Equal(["ConnectionStrings:redis", "ConnectionStrings:rabbitmq"], ex.Claves);
        Assert.Contains("ConnectionStrings__redis, ConnectionStrings__rabbitmq", ex.Message, StringComparison.Ordinal);
    }
}
