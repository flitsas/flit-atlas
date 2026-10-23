using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flit.Atlas.Identidad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// <c>contracts/openapi/atlas.v1.yaml</c> es el documento que genera el código (HU 1.7, ATLAS-ADR-0021). Si un PR cambia
/// un endpoint sin regenerarlo, estas pruebas fallan. Para regenerarlo:
/// <c>ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests</c>.
/// </summary>
public class ContratoOpenApiTests
{
    private const string VariableActualizar = "ATLAS_ACTUALIZAR_CONTRATOS";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string RutaContrato => Path.Combine(Repositorio.Raiz, "contracts", "openapi", "atlas.v1.yaml");

    /// <summary>
    /// atlas-api como queda en qa y pdn (Entra configurado) pero en local, el único ambiente que sirve el documento.
    /// Tiene el proveedor simulado encendido a propósito: no debe aparecer en el contrato. No toca la base, Redis ni
    /// RabbitMQ: generar el documento no abre conexiones.
    /// </summary>
    internal static WebApplicationFactory<Program> ApiComoQa(string ambiente = "local", Action<IWebHostBuilder>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:atlas", "Host=127.0.0.1;Port=1;Database=atlas;Username=x;Password=x");
            b.UseSetting("ConnectionStrings:redis", "127.0.0.1:1,abortConnect=false");
            b.UseSetting("ConnectionStrings:rabbitmq", "amqp://x:x@127.0.0.1:1");
            b.UseSetting("Atlas:Ambiente", ambiente);
            b.UseSetting(ExtensionesIdentidad.ClaveSimulado, ambiente is "local" or "dev" ? "true" : "false");
            b.UseSetting($"{ExtensionesIdentidad.SeccionEntra}:TenantId", "00000000-0000-0000-0000-00000000a71a");
            b.UseSetting($"{ExtensionesIdentidad.SeccionEntra}:ClientId", "00000000-0000-0000-0000-00000000c11e");
            extra?.Invoke(b);
        });

    private static async Task<string> GenerarAsync()
    {
        await using var api = ApiComoQa();
        using var cliente = api.CreateClient();
        var yaml = await cliente.GetStringAsync(new Uri("/openapi/v1.yaml", UriKind.Relative), Ct);
        return Normalizar(yaml);
    }

    /// <summary>Saltos de línea LF y un solo salto al final, como exige el .editorconfig del repo.</summary>
    private static string Normalizar(string texto) => texto.ReplaceLineEndings("\n").TrimEnd() + "\n";

    [Fact]
    public async Task El_contrato_versionado_coincide_con_el_que_genera_el_codigo()
    {
        var generado = await GenerarAsync();

        if (Environment.GetEnvironmentVariable(VariableActualizar) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaContrato)!);
            await File.WriteAllTextAsync(RutaContrato, generado, Ct);
            return;
        }

        Assert.True(File.Exists(RutaContrato), $"No existe {RutaContrato}. Genéralo con {VariableActualizar}=1.");
        var versionado = Normalizar(await File.ReadAllTextAsync(RutaContrato, Ct));
        Assert.True(
            versionado == generado,
            $"contracts/openapi/atlas.v1.yaml no coincide con el código. Regenéralo con {VariableActualizar}=1 dotnet test " +
            "--project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests " +
            "y versiona el cambio.");
    }

    [Fact]
    public async Task El_contrato_es_openapi_3_1_con_las_operaciones_publicas_y_sin_el_proveedor_simulado()
    {
        var yaml = await GenerarAsync();

        Assert.StartsWith("openapi: '3.1", yaml.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("operationId: ObtenerUsuarioActual", yaml, StringComparison.Ordinal);
        Assert.Contains("operationId: EntrarConMicrosoft", yaml, StringComparison.Ordinal);
        Assert.Contains("operationId: CerrarSesion", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("/simulado/", yaml, StringComparison.Ordinal);
        Assert.Contains("name: __Host-atlas", yaml, StringComparison.Ordinal);
        Assert.Contains("name: X-Atlas-Csrf", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", yaml, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("qa")]
    [InlineData("pdn")]
    public async Task En_qa_y_pdn_el_documento_no_se_sirve(string ambiente)
    {
        var certificado = CertificadoTemporal();
        try
        {
            await using var api = ApiComoQa(ambiente, b =>
                b.UseSetting(ExtensionesIdentidad.ClaveCertificadoProteccion, certificado));
            using var cliente = api.CreateClient();

            using var respuesta = await cliente.GetAsync(new Uri("/openapi/v1.yaml", UriKind.Relative), Ct);

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }
        finally
        {
            File.Delete(certificado);
        }
    }

    /// <summary>qa y pdn exigen un certificado para Data Protection: uno autofirmado de un solo uso basta.</summary>
    private static string CertificadoTemporal()
    {
        using var llave = RSA.Create(2048);
        var solicitud = new CertificateRequest("CN=Atlas Pruebas", llave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = solicitud.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var ruta = Path.Combine(Path.GetTempPath(), $"atlas-pruebas-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(ruta, certificado.Export(X509ContentType.Pfx));
        return ruta;
    }
}

/// <summary>Ubica la raíz del repo (donde está <c>Flit.Atlas.slnx</c>) desde la carpeta de salida de las pruebas.</summary>
public static class Repositorio
{
    public static string Raiz { get; } = Buscar();

    private static string Buscar()
    {
        for (var carpeta = new DirectoryInfo(AppContext.BaseDirectory); carpeta is not null; carpeta = carpeta.Parent)
        {
            if (File.Exists(Path.Combine(carpeta.FullName, "Flit.Atlas.slnx")))
            {
                return carpeta.FullName;
            }
        }
        throw new InvalidOperationException("No se encontró Flit.Atlas.slnx encima de " + AppContext.BaseDirectory);
    }
}
