using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Modulos.Equipo.GitHub;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Flit.Atlas.Modulos.Equipo.Tests;

/// <summary>
/// HU 2.4 (ATLAS-ADR-0031): bloque CODEOWNERS sugerido por repo y carpeta, comparación con el real, ficha de responsables
/// y el cliente de solo lectura de GitHub contra un GitHub simulado (manejador HTTP falso; nada sale a internet).
/// </summary>
public class CodeownersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Uno = Guid.CreateVersion7();
    private static readonly Guid Dos = Guid.CreateVersion7();

    private static (List<AplicativoLeido> Catalogo, Dictionary<Guid, IReadOnlyList<ResponsableCodigo>> Responsables) Escenario()
    {
        var catalogo = DocumentoFicticio.Catalogo();
        var comparendos = catalogo.Single(a => a.Slug == "flit2-comparendos");
        var flit2 = catalogo.Single(a => a.Slug == "flit2");
        var responsables = new Dictionary<Guid, IReadOnlyList<ResponsableCodigo>>
        {
            [flit2.Id] = [new("principal", Uno, "Persona Demo Uno", "demo-uno")],
            // Sin usuario de GitHub el secundario: la línea lo marca.
            [comparendos.Id] = [new("secundario", Dos, "Persona Demo Dos", null), new("principal", Uno, "Persona Demo Uno", "demo-uno")],
        };
        return (catalogo, responsables);
    }

    [Fact]
    public void Genera_un_bloque_por_repo_con_el_repo_completo_primero_y_hereda_responsables()
    {
        var (catalogo, responsables) = Escenario();

        var bloques = Codeowners.Generar(catalogo, responsables, 5);

        var flit = Assert.Single(bloques, b => b.Repo == "flitsas/flit");
        Assert.Equal(["*", "/services/core-comparendos/"], flit.Lineas.Select(l => l.Patron));
        var raiz = flit.Lineas[0];
        Assert.Equal(["@demo-uno"], raiz.Propietarios);
        var carpeta = flit.Lineas[1];
        // El servicio no tiene responsables: hereda los del módulo Comparendos (principal primero).
        Assert.Equal("flit2-comparendos", carpeta.HeredadoDe?.Slug);
        Assert.Equal(["@demo-uno"], carpeta.Propietarios);
        Assert.Equal(["Persona Demo Dos"], carpeta.SinUsuario);
        Assert.Contains("* @demo-uno\n", flit.Texto, StringComparison.Ordinal);
        Assert.Contains("/services/core-comparendos/ @demo-uno\n", flit.Texto, StringComparison.Ordinal);
        Assert.Contains("# Sin usuario de GitHub en Atlas: Persona Demo Dos", flit.Texto, StringComparison.Ordinal);
        Assert.Contains("(reparto versión 5)", flit.Texto, StringComparison.Ordinal);
        // Un repo sin responsables no genera bloque.
        Assert.DoesNotContain(bloques, b => b.Repo == "flitsas/flito");
    }

    [Fact]
    public void Compara_con_el_codeowners_real_patron_por_patron()
    {
        var (catalogo, responsables) = Escenario();
        var bloque = Codeowners.Generar(catalogo, responsables, 5).Single(b => b.Repo == "flitsas/flit");
        const string Real = """
            # Comentario
            * @Demo-Uno
            services/core-comparendos @otra-persona # carpeta sin barras
            /docs/ @demo-uno
            """;

        var diferencias = Codeowners.Comparar(bloque, Real);

        Assert.Equal(
            [("*", "igual"), ("/docs/", "solo_en_github"), ("/services/core-comparendos/", "distinto")],
            diferencias.Select(d => (d.Patron, d.Estado)));
        Assert.All(Codeowners.Comparar(bloque, null), d => Assert.Equal("falta_en_github", d.Estado));
    }

    [Fact]
    public void La_ficha_de_responsables_trae_principal_secundario_escalamiento_y_definiciones()
    {
        var (catalogo, responsables) = Escenario();
        var comparendos = catalogo.Single(a => a.Slug == "flit2-comparendos");
        var definiciones = DocumentoResponsables.Leer(DocumentoFicticio.Texto).Definiciones;

        var ficha = Codeowners.Ficha(comparendos, responsables[comparendos.Id], null, "flitsas/flit", definiciones, 5, new DateOnly(2026, 10, 20));

        Assert.StartsWith("## Ficha de responsables\n", ficha, StringComparison.Ordinal);
        Assert.Contains("| Responsable principal | Persona Demo Uno | @demo-uno |", ficha, StringComparison.Ordinal);
        Assert.Contains("| Responsable secundario | Persona Demo Dos | — |", ficha, StringComparison.Ordinal);
        Assert.Contains("**A quién escalar:**", ficha, StringComparison.Ordinal);
        Assert.Contains("- Dedicación orientativa: ~20 % de su tiempo en ese módulo.", ficha, StringComparison.Ordinal);
        Assert.Contains("_Reparto versión 5 del 20/10/2026", ficha, StringComparison.Ordinal);
    }

    /// <summary>GitHub simulado: responde por ruta y guarda lo que le pidieron.</summary>
    private sealed class GitHubSimulado(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Peticiones { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Peticiones.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private static ClienteGitHub Cliente(GitHubSimulado github, OpcionesGitHub opciones) =>
        new(new HttpClient(github), Options.Create(opciones), new CacheTokenGitHub(), TimeProvider.System);

    [Fact]
    public async Task Con_token_lee_el_primer_codeowners_que_encuentra()
    {
        using var github = new GitHubSimulado(p => p.RequestUri!.AbsolutePath switch
        {
            "/repos/flitsas/flit/contents/.github/CODEOWNERS" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/repos/flitsas/flit/contents/CODEOWNERS" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("* @demo-uno\n") },
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });
        var cliente = Cliente(github, new OpcionesGitHub { Token = "token-de-prueba" });

        var real = await cliente.LeerCodeownersAsync("flitsas/flit", Ct);

        Assert.True(cliente.Configurado);
        Assert.Equal("token", cliente.Modo);
        Assert.Equal(("CODEOWNERS", "* @demo-uno\n"), (real.Ruta, real.Contenido));
        Assert.All(github.Peticiones, p =>
        {
            Assert.Equal("Bearer", p.Headers.Authorization?.Scheme);
            Assert.Equal("token-de-prueba", p.Headers.Authorization?.Parameter);
            Assert.Equal(HttpMethod.Get, p.Method);
        });
    }

    [Fact]
    public async Task Sin_codeowners_responde_vacio_y_un_403_es_un_error_legible()
    {
        using var vacio = new GitHubSimulado(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var real = await Cliente(vacio, new OpcionesGitHub { Token = "token-de-prueba" }).LeerCodeownersAsync("flitsas/flit", Ct);
        Assert.Equal((null, null), (real.Ruta, real.Contenido));
        Assert.Equal(3, vacio.Peticiones.Count);

        using var negado = new GitHubSimulado(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var error = await Assert.ThrowsAsync<GitHubNoDisponibleException>(() =>
            Cliente(negado, new OpcionesGitHub { Token = "token-de-prueba" }).LeerCodeownersAsync("flitsas/flit", Ct));
        Assert.Contains("403", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_configuracion_no_llama_a_github()
    {
        using var github = new GitHubSimulado(_ => throw new InvalidOperationException("No debía llamar."));
        var cliente = Cliente(github, new OpcionesGitHub());

        Assert.False(cliente.Configurado);
        Assert.Equal("ninguno", cliente.Modo);
        await Assert.ThrowsAsync<GitHubNoDisponibleException>(() => cliente.LeerCodeownersAsync("flitsas/flit", Ct));
        Assert.Empty(github.Peticiones);
    }

    [Fact]
    public async Task Con_la_github_app_firma_un_jwt_pide_el_token_de_instalacion_y_lo_reutiliza()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        string? jwt = null;
        using var github = new GitHubSimulado(p =>
        {
            if (p.RequestUri!.AbsolutePath == "/app/installations/77/access_tokens")
            {
                jwt = p.Headers.Authorization?.Parameter;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent($$"""{"token":"token-instalacion","expires_at":"{{DateTimeOffset.UtcNow.AddHours(1):O}}"}""", Encoding.UTF8, "application/json"),
                };
            }
            Assert.Equal("token-instalacion", p.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("* @demo-uno\n") };
        });
        var cliente = Cliente(github, new OpcionesGitHub { AppId = "12345", InstalacionId = "77", LlavePrivada = pem });

        await cliente.LeerCodeownersAsync("flitsas/flit", Ct);
        await cliente.LeerCodeownersAsync("flitsas/flito", Ct);

        Assert.Equal("app", cliente.Modo);
        Assert.Single(github.Peticiones, p => p.Method == HttpMethod.Post);
        var token = new JsonWebToken(jwt);
        Assert.Equal("12345", token.Issuer);
        Assert.Equal("RS256", token.Alg);
        Assert.True(token.ValidTo <= DateTime.UtcNow.AddMinutes(10));
        // La firma es de la llave de la App.
        var validacion = await new JsonWebTokenHandler().ValidateTokenAsync(jwt, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateAudience = false, ValidIssuer = "12345", IssuerSigningKey = new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa.ExportParameters(false)),
        });
        Assert.True(validacion.IsValid, validacion.Exception?.Message);
    }
}
