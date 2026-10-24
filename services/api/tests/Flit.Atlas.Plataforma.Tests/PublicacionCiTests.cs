using System.Security.Claims;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Publicacion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>
/// HU 3.1 (ATLAS-ADR-0035): validación del token OIDC de GitHub Actions con JWT firmados en la prueba y un JWKS simulado
/// (<see cref="GitHubActionsSimulado"/>), caché y rotación de las llaves, y las reglas de la lista blanca. Nunca contra
/// GitHub.
/// </summary>
public sealed class PublicacionCiTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GitHubActionsSimulado _github = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 23, 15, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _servicios;

    public PublicacionCiTests()
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OpcionesPublicacion.ClaveUrlPublica] = GitHubActionsSimulado.Audiencia + "/",
        }).Build();
        var servicios = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuracion)
            .AddSingleton<TimeProvider>(_reloj)
            .AddLogging();
        servicios.AddPlataformaPublicacionDePrueba(_github);
        _servicios = servicios.BuildServiceProvider();
    }

    public void Dispose()
    {
        _servicios.Dispose();
        _github.Dispose();
    }

    private ValidadorTokenGitHub Validador => _servicios.GetRequiredService<ValidadorTokenGitHub>();

    private Task<ValidadorTokenGitHub.Resultado> ValidarAsync(string token) => Validador.ValidarAsync(token, Ct);

    [Fact]
    public async Task Un_token_firmado_por_github_para_esta_audiencia_y_vigente_vale()
    {
        var resultado = await ValidarAsync(_github.Token(_reloj.GetUtcNow().AddSeconds(-10)));

        Assert.Null(resultado.Motivo);
        var publicador = PublicadorCi.Desde(new ClaimsPrincipal(resultado.Identidad!));
        Assert.NotNull(publicador);
        Assert.Equal(("flitsas-demo/api-demo", "develop", ".github/workflows/ci.yml", "push"),
            (publicador.Repositorio, publicador.Rama, publicador.Workflow, publicador.Evento));
        Assert.Equal("github:flitsas-demo/api-demo@develop", publicador.Firma);
        Assert.Equal(1, _github.PedidosJwks);
    }

    [Fact]
    public async Task Un_token_vencido_aun_no_valido_o_emitido_en_el_futuro_no_vale()
    {
        var ahora = _reloj.GetUtcNow();

        Assert.Equal("token_vencido", (await ValidarAsync(_github.Token(ahora.AddMinutes(-10), vida: TimeSpan.FromMinutes(5)))).Motivo);
        // Dentro del margen de un minuto todavía vale.
        Assert.Null((await ValidarAsync(_github.Token(ahora.AddMinutes(-5).AddSeconds(-30), vida: TimeSpan.FromMinutes(5)))).Motivo);
        Assert.Equal("token_aun_no_vale", (await ValidarAsync(_github.Token(ahora, noAntes: ahora.AddMinutes(5)))).Motivo);
        Assert.Equal("token_emitido_en_el_futuro", (await ValidarAsync(_github.Token(ahora.AddMinutes(5), noAntes: ahora))).Motivo);
        Assert.Equal("token_de_vida_demasiado_larga", (await ValidarAsync(_github.Token(ahora, vida: TimeSpan.FromHours(3)))).Motivo);
    }

    [Fact]
    public async Task Otra_audiencia_otro_emisor_otra_llave_o_sin_firma_no_valen()
    {
        var ahora = _reloj.GetUtcNow();

        Assert.NotNull((await ValidarAsync(_github.Token(ahora, audiencia: "https://dev.atlas.ejemplo.com"))).Motivo);
        Assert.NotNull((await ValidarAsync(_github.Token(ahora, emisor: "https://otro-emisor.ejemplo.com"))).Motivo);
        Assert.NotNull((await ValidarAsync(_github.Token(ahora, llaveAjena: true))).Motivo);
        Assert.NotNull((await ValidarAsync(GitHubActionsSimulado.SinFirma(ahora))).Motivo);
        Assert.Equal("token_ilegible", (await ValidarAsync("esto-no-es-un-jwt")).Motivo);
    }

    [Fact]
    public async Task Sin_audiencia_configurada_ningun_token_vale()
    {
        using var servicios = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<TimeProvider>(_reloj)
            .AddLogging()
            .AddPlataformaPublicacionDePrueba(_github)
            .BuildServiceProvider();

        var resultado = await servicios.GetRequiredService<ValidadorTokenGitHub>().ValidarAsync(_github.Token(_reloj.GetUtcNow()), Ct);

        Assert.Equal("sin_audiencia_configurada", resultado.Motivo);
    }

    [Fact]
    public async Task Las_llaves_se_cachean_se_refrescan_con_un_kid_nuevo_y_sobreviven_a_github_caido()
    {
        Assert.Null((await ValidarAsync(_github.Token(_reloj.GetUtcNow()))).Motivo);
        Assert.Null((await ValidarAsync(_github.Token(_reloj.GetUtcNow()))).Motivo);
        Assert.Equal(1, _github.PedidosJwks);

        // GitHub rota: el kid nuevo obliga a pedir otra vez, pero no antes de la espera mínima (30 s).
        _github.Rotar(retirarAnteriores: true);
        _reloj.Advance(TimeSpan.FromSeconds(10));
        Assert.NotNull((await ValidarAsync(_github.Token(_reloj.GetUtcNow()))).Motivo);
        Assert.Equal(1, _github.PedidosJwks);
        _reloj.Advance(TimeSpan.FromSeconds(30));
        Assert.Null((await ValidarAsync(_github.Token(_reloj.GetUtcNow()))).Motivo);
        Assert.Equal(2, _github.PedidosJwks);

        // GitHub caído al refrescar (pasadas 6 h): se siguen usando las últimas llaves buenas.
        _github.Caido = true;
        _reloj.Advance(TimeSpan.FromHours(7));
        Assert.Null((await ValidarAsync(_github.Token(_reloj.GetUtcNow()))).Motivo);
        Assert.Equal(3, _github.PedidosJwks);
    }

    [Fact]
    public async Task Sin_llaves_porque_github_nunca_respondio_ningun_token_vale()
    {
        _github.Caido = true;

        Assert.Equal("sin_llaves_de_github", (await ValidarAsync(_github.Token(_reloj.GetUtcNow()))).Motivo);
    }

    private static OpcionesPublicacion Opciones()
    {
        var opciones = new OpcionesPublicacion
        {
            Propietarios = [GitHubActionsSimulado.Propietario],
            WorkflowsReutilizables = [GitHubActionsSimulado.PatronWorkflowOficial],
        };
        opciones.Normalizar();
        return opciones;
    }

    private static PublicadorCi Publicador(Dictionary<string, object>? claims = null) =>
        PublicadorCi.Desde(new ClaimsPrincipal(new ClaimsIdentity(
            (claims ?? GitHubActionsSimulado.Claims()).Select(c => new Claim(c.Key, c.Value.ToString()!)), "prueba")))!;

    [Fact]
    public void Dueno_evento_y_workflow_reutilizable_se_revisan_antes_de_la_lista_blanca()
    {
        var opciones = Opciones();

        Assert.Null(ListaBlancaPublicacion.MotivoRechazoGlobal(Publicador(), opciones));
        Assert.Equal("propietario_no_permitido", ListaBlancaPublicacion.MotivoRechazoGlobal(
            Publicador(GitHubActionsSimulado.Claims(repositorio: "otra-empresa/api")), opciones));
        Assert.Equal("evento_no_permitido", ListaBlancaPublicacion.MotivoRechazoGlobal(
            Publicador(GitHubActionsSimulado.Claims(evento: "pull_request_target")), opciones));
        Assert.Equal("workflow_reutilizable_no_oficial", ListaBlancaPublicacion.MotivoRechazoGlobal(
            Publicador(GitHubActionsSimulado.Claims(jobWorkflow: "flitsas-demo/api-demo/.github/workflows/propio.yml@refs/heads/develop")), opciones));
        Assert.Equal("workflow_reutilizable_no_oficial", ListaBlancaPublicacion.MotivoRechazoGlobal(
            Publicador(GitHubActionsSimulado.Claims(jobWorkflow: "flitsas-demo/plantillas/.github/workflows/atlas-publicar.yml@refs/heads/main")), opciones));
        Assert.Equal("claims_incompletos", ListaBlancaPublicacion.MotivoRechazoGlobal(null, opciones));
    }

    [Fact]
    public void Un_token_de_un_tag_o_sin_job_workflow_ref_no_es_un_publicador()
    {
        var deTag = GitHubActionsSimulado.Claims();
        deTag["ref"] = "refs/tags/v1.0.0";
        deTag["ref_type"] = "tag";

        Assert.Null(PublicadorCi.Desde(new ClaimsPrincipal(new ClaimsIdentity(deTag.Select(c => new Claim(c.Key, c.Value.ToString()!)), "prueba"))));
        Assert.Null(PublicadorCi.Desde(new ClaimsPrincipal(new ClaimsIdentity(
            GitHubActionsSimulado.Claims(jobWorkflow: null).Select(c => new Claim(c.Key, c.Value.ToString()!)), "prueba"))));
    }

    [Fact]
    public void La_lista_blanca_dice_si_falta_el_repo_la_rama_el_workflow_el_aplicativo_o_el_tipo()
    {
        var fuentes = new[]
        {
            new FuentePublicacionLeida(Guid.NewGuid(), Guid.NewGuid(), "api-demo", "flitsas-demo/api-demo", ["develop", "release", "hotfix/*"], null, ["openapi"]),
            new FuentePublicacionLeida(Guid.NewGuid(), Guid.NewGuid(), "eventos-demo", "flitsas-demo/api-demo", ["develop"], ".github/workflows/eventos.yml", ["asyncapi"]),
        };

        Assert.True(ListaBlancaPublicacion.Autorizar(Publicador(), fuentes, "api-demo", "openapi").Autorizada);
        Assert.True(ListaBlancaPublicacion.Autorizar(Publicador(GitHubActionsSimulado.Claims(rama: "hotfix/urgente")), fuentes, "api-demo", "openapi").Autorizada);
        Assert.Equal("fuente_no_autorizada", ListaBlancaPublicacion.Autorizar(
            Publicador(GitHubActionsSimulado.Claims(repositorio: "flitsas-demo/otro")), fuentes, "api-demo", "openapi").Codigo);
        Assert.Equal("fuente_no_autorizada", ListaBlancaPublicacion.Autorizar(
            Publicador(GitHubActionsSimulado.Claims(rama: "feature/x")), fuentes, "api-demo", "openapi").Codigo);
        Assert.Equal("aplicativo_no_autorizado", ListaBlancaPublicacion.Autorizar(Publicador(), fuentes, "otro-aplicativo", "openapi").Codigo);
        Assert.Equal("tipo_no_autorizado", ListaBlancaPublicacion.Autorizar(Publicador(), fuentes, "api-demo", "proto").Codigo);
        // La fila de eventos exige su propio workflow.
        Assert.Equal("aplicativo_no_autorizado", ListaBlancaPublicacion.Autorizar(Publicador(), fuentes, "eventos-demo", "asyncapi").Codigo);
        Assert.True(ListaBlancaPublicacion.Autorizar(
            Publicador(GitHubActionsSimulado.Claims(workflow: ".github/workflows/eventos.yml")), fuentes, "eventos-demo", "asyncapi").Autorizada);
        // GitHub no distingue mayúsculas en el nombre del repo.
        Assert.True(ListaBlancaPublicacion.Autorizar(Publicador(GitHubActionsSimulado.Claims(repositorio: "flitsas-demo/API-Demo")), fuentes, "api-demo", "openapi").Autorizada);
    }

    [Theory]
    [InlineData("release", null, true)]
    [InlineData("release", "rc.1a2b3c4", false)]
    [InlineData("develop", "dev.1a2b3c4", true)]
    [InlineData("develop", "rc.1a2b3c4", false)]
    [InlineData("develop", null, false)]
    [InlineData("staging", "rc.1a2b3c4", true)]
    [InlineData("hotfix/x", "hotfix.1", true)]
    [InlineData("hotfix/x", null, false)]
    public void Cada_rama_publica_su_tipo_de_version(string rama, string? prerelease, bool vale)
    {
        Assert.Equal(vale, ListaBlancaPublicacion.MotivoVersionPorRama(rama, prerelease, Opciones()) is null);
    }

    [Theory]
    [InlineData("release", "release", true, true)]
    [InlineData("release", "Release", true, false)]
    [InlineData("hotfix/*", "hotfix/a/b", true, true)]
    [InlineData("a*c", "abbbc", true, true)]
    [InlineData("a.c", "abc", true, false)]
    [InlineData("Flitsas/*", "flitsas/x", false, true)]
    public void El_comodin_solo_es_el_asterisco(string patron, string valor, bool distinguir, bool coincide)
    {
        Assert.Equal(coincide, ListaBlancaPublicacion.Coincide(patron, valor, distinguir));
    }

    [Fact]
    public void Las_opciones_por_defecto_no_se_suman_a_las_configuradas()
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{OpcionesPublicacion.Seccion}:Propietarios:0"] = "flitsas-demo",
        }).Build();
        using var servicios = new ServiceCollection().AddSingleton<IConfiguration>(configuracion).AddLogging()
            .AddPlataformaPublicacionDePrueba(_github).BuildServiceProvider();

        var opciones = servicios.GetRequiredService<IOptionsMonitor<OpcionesPublicacion>>().CurrentValue;

        Assert.Equal(["flitsas-demo"], opciones.Propietarios);
        Assert.Equal(["push", "workflow_dispatch"], opciones.Eventos);
        Assert.Equal([OpcionesPublicacion.WorkflowOficial], opciones.WorkflowsReutilizables);
        Assert.Equal("https://token.actions.githubusercontent.com/.well-known/jwks", opciones.UrlLlavesEfectiva);
        Assert.Equal("https://atlas.ejemplo.com", opciones.AudienciaEfectiva("https://atlas.ejemplo.com/"));
    }
}

internal static class ServiciosPublicacionDePrueba
{
    /// <summary>Lo mismo que registra <c>AgregarPlataforma</c> para la publicación, con el JWKS de GitHub simulado.</summary>
    public static IServiceCollection AddPlataformaPublicacionDePrueba(this IServiceCollection servicios, GitHubActionsSimulado github)
    {
        servicios.AgregarPublicacionCi();
        servicios.AddHttpClient(LlavesGitHubActions.NombreCliente).ConfigurePrimaryHttpMessageHandler(() => new NoDesechar(github));
        return servicios;
    }

    /// <summary>La fábrica desecha el manejador al rotarlo; el simulado vive lo que dura la prueba.</summary>
    private sealed class NoDesechar(HttpMessageHandler interno) : DelegatingHandler(interno)
    {
        // Sin desechar el interno (eso hace Dispose(true) en DelegatingHandler).
        protected override void Dispose(bool disposing) => base.Dispose(false);
    }
}
