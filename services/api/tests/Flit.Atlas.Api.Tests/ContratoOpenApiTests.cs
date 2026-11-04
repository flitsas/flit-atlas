using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

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
            b.UseSetting("Atlas:Proxy:RedesConfiables", "10.0.0.0/8");
            b.ConfigureTestServices(s => s.SinRegistroDeEventosDeWindows());
            b.UseSetting(ExtensionesIdentidad.ClaveSimulado, ambiente is "local" or "dev" ? "true" : "false");
            b.UseSetting($"{ExtensionesIdentidad.SeccionEntra}:TenantId", "00000000-0000-0000-0000-00000000a71a");
            b.UseSetting($"{ExtensionesIdentidad.SeccionEntra}:ClientId", "00000000-0000-0000-0000-00000000c11e");
            b.UseSetting("Atlas:UrlPublica", "https://qa.atlas.ejemplo.com");
            b.UseSetting("Plataforma:Correo:Proveedor", "smtp");
            b.UseSetting("Plataforma:Correo:Remitente", "atlas@ejemplo.com");
            b.UseSetting("Plataforma:Correo:Smtp:Host", "127.0.0.1");
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

    /// <summary>
    /// OpenAPI trata como la misma ruta dos plantillas que solo difieren en el nombre del parámetro (<c>/x/{slug}</c> y
    /// <c>/x/{id}</c>), y la publicación en Atlas rechaza el spec con 422 (<c>path signature MUST be unique</c>). Las
    /// operaciones de una misma ruta deben usar el mismo nombre (<c>[FromRoute(Name = …)]</c>).
    /// </summary>
    [Fact]
    public async Task Ninguna_ruta_repite_su_firma_con_otro_nombre_de_parametro()
    {
        var yaml = await GenerarAsync();

        var repetidas = Regex.Matches(yaml, @"^  '?(/[^'\n]*?)'?:\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .GroupBy(ruta => Regex.Replace(ruta, @"\{[^}]+\}", "{}"), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(" y ", g))
            .ToList();

        Assert.True(repetidas.Count == 0, $"Rutas con la misma firma: {string.Join("; ", repetidas)}.");
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
            {
                b.UseSetting(ExtensionesIdentidad.ClaveCertificadoProteccion, certificado);
                // Sin extensión de uso de llave, el mismo certificado sirve para firmar y cifrar tokens (HU 1.3c).
                b.UseSetting(OpcionesTokens.ClaveCertificadosFirma, certificado);
                b.UseSetting(OpcionesTokens.ClaveCertificadosCifrado, certificado);
            });
            using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            using var respuesta = await cliente.GetAsync(new Uri("/openapi/v1.yaml", UriKind.Relative), Ct);
            using var jwks = await cliente.GetAsync(new Uri(ServidorTokens.RutaJwks, UriKind.Relative), Ct);

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            // El contrato del protocolo sí se publica: descubrimiento y JWKS con la llave del certificado montado.
            Assert.Equal(HttpStatusCode.OK, jwks.StatusCode);
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
