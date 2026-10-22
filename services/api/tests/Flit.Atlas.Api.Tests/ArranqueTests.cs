using System.Net;
using Flit.Atlas.ServiceDefaults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Flit.Atlas.Api.Tests;

public class ArranqueTests
{
    [Fact]
    public void Sin_configuracion_el_arranque_falla_nombrando_todas_las_variables()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Anula cualquier valor que venga del entorno de quien corre las pruebas.
            b.UseSetting("ConnectionStrings:atlas", "");
            b.UseSetting("ConnectionStrings:redis", "");
            b.UseSetting("ConnectionStrings:rabbitmq", "");
            b.UseSetting("Atlas:Ambiente", "");
        });

        var ex = Assert.Throws<ConfiguracionFaltanteException>(() => factory.CreateClient());

        Assert.Equal(Dependencias.ClavesRequeridas, ex.Claves);
    }

    [Fact]
    public async Task La_sonda_de_vida_responde_sin_depender_de_la_base_ni_de_las_colas()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Destinos que no existen: la sonda de vida no debe tocarlos.
            b.UseSetting("ConnectionStrings:atlas", "Host=127.0.0.1;Port=1;Database=atlas;Username=x;Password=x");
            b.UseSetting("ConnectionStrings:redis", "127.0.0.1:1,abortConnect=false");
            b.UseSetting("ConnectionStrings:rabbitmq", "amqp://x:x@127.0.0.1:1");
            b.UseSetting("Atlas:Ambiente", "local");
        });
        using var client = factory.CreateClient();

        using var respuesta = await client.GetAsync(new Uri(Extensiones.RutaVida, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
