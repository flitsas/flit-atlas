using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Tests;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Publicacion;
using Flit.Atlas.Plataforma.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 3.1 y 3.2 (ATLAS-ADR-0035) con atlas-api completo: la publicación desde la CI con el token OIDC de GitHub Actions
/// firmado en la prueba (JWKS simulado, <see cref="GitHubActionsSimulado"/>) y la lista blanca del catálogo, la
/// inmutabilidad (201, 200 idempotente, 409, 422), el índice, la auditoría, el evento en la outbox, la lectura interna y
/// la administración de la lista blanca. La prueba parametrizada de <c>AutorizacionTests</c> recorre además todos estos
/// endpoints con todos los roles.
/// </summary>
public sealed class ModuloContratosTests(EntornoApi entorno) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GitHubActionsSimulado _github = new();

    public void Dispose() => _github.Dispose();

    private sealed record Referencia(Guid Id, string Slug, string Nombre);

    private sealed record Fuente(Guid Id, Referencia Aplicativo, string Repositorio, List<string> Ramas, string? Workflow, List<string> Tipos, string? Nota);

    private sealed record Publicada(Guid ContratoId, Guid VersionId, string Aplicativo, string Contrato, string Tipo, string Version, bool EsEstable,
        string Sha256, int Operaciones, bool TieneBreaking, List<string> Advertencias, bool Creada);

    private sealed record Error(string Codigo, string Mensaje, List<string>? Errores);

    private sealed record Aplicativo(Guid Id, string? Slug, string? Nombre);

    private sealed record VersionResumen(Guid Id, string Version, bool EsEstable, string Sha256, string Formato, string Repositorio, string Rama,
        string Commit, string? Workflow, string WorkflowDelJob, string? Ejecucion, string PublicadoPor, bool TieneBreaking, string Estado);

    private sealed record ContratoResumen(Guid Id, string Codigo, string Nombre, string Tipo, Aplicativo Aplicativo, int Versiones,
        VersionResumen? UltimaVersion, VersionResumen? UltimaEstable);

    private sealed record Listado(int Total, List<ContratoResumen> Elementos);

    private sealed record Operacion(string Clave, string Clase, string? Metodo, string? Ruta, string? OperationId, bool Publica);

    private WebApplicationFactory<Program> Api() => entorno.Api(
        configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesPublicacion.Seccion}:Audiencia"] = GitHubActionsSimulado.Audiencia,
            [$"{OpcionesPublicacion.Seccion}:Propietarios:0"] = GitHubActionsSimulado.Propietario,
            [$"{OpcionesPublicacion.Seccion}:WorkflowsReutilizables:0"] = GitHubActionsSimulado.PatronWorkflowOficial,
        },
        servicios: s => s.AddPlataformaPublicacionDePruebaEnApi(_github));

    private async Task<Navegador> EmpleadoAsync(WebApplicationFactory<Program> api, string? rol = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::35:{Random.Shared.Next(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync($"contratos.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo", grupos)).StatusCode);
        return navegador;
    }

    private static string Unico(string prefijo) => $"{prefijo}-{Guid.NewGuid().ToString("N")[..10]}";

    /// <summary>Un aplicativo nuevo y su fuente en la lista blanca, creados por el líder técnico con la API.</summary>
    private static async Task<(string Aplicativo, string Repositorio)> FuenteAsync(Navegador lider, string[] tipos, string[]? ramas = null, string? workflow = null)
    {
        var slug = Unico("api-demo");
        var creado = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos", JsonContent.Create(new
        {
            slug, nombre = "API Demo", tipo = "producto", grupo = "satelite", estado = "activo", criticidad = "media", manejaDatosPersonales = false,
        }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var repositorio = $"{GitHubActionsSimulado.Propietario}/{slug}";
        var fuente = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion", JsonContent.Create(new
        {
            aplicativo = slug, repositorio, ramas = ramas ?? ["develop", "release"], workflow, tipos, nota = "Prueba de la HU 3.1.",
        }));
        Assert.Equal(HttpStatusCode.Created, fuente.StatusCode);
        return (slug, repositorio);
    }

    /// <param name="csrf">Con la cabecera antifalsificación de la PWA (sin ella y sin Bearer, la antifalsificación responde 403).</param>
    private static async Task<HttpResponseMessage> PublicarAsync(WebApplicationFactory<Program> api, string? token, object cuerpo, bool csrf = false)
    {
        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contratos/publicaciones") { Content = JsonContent.Create(cuerpo) };
        if (token is not null)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (csrf)
        {
            peticion.Headers.Add(Identidad.EndpointsIdentidad.CabeceraCsrf, "1");
        }
        return await cliente.SendAsync(peticion, Ct);
    }

    private string Token(string repositorio, string rama = "develop", string evento = "push", string? jobWorkflow = GitHubActionsSimulado.WorkflowOficial,
        TimeSpan? edad = null) =>
        _github.Token(DateTimeOffset.UtcNow - (edad ?? TimeSpan.FromSeconds(5)),
            GitHubActionsSimulado.Claims(repositorio: repositorio, rama: rama, evento: evento, jobWorkflow: jobWorkflow));

    private static object Cuerpo(string aplicativo, string contrato = "api-externa", string tipo = "openapi", string? version = "1.2.0-dev.a1b2c3d",
        string spec = SpecsDeEjemplo.OpenApiYaml, string? changelog = null) =>
        new { aplicativo, contrato, tipo, version, spec, changelog };

    [Fact]
    public async Task Publica_un_openapi_201_luego_200_idempotente_y_409_con_otro_hash_y_lo_deja_consultable()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (aplicativo, repositorio) = await FuenteAsync(lider, ["openapi"]);
        const string changelog = """[{"id":"api-path-removed","text":"Se quitó una ruta.","level":3}]""";

        var creada = await PublicarAsync(api, Token(repositorio), Cuerpo(aplicativo, changelog: changelog));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var publicada = (await creada.Content.ReadFromJsonAsync<Publicada>(Ct))!;
        Assert.True(publicada.Creada);
        Assert.Equal(("1.2.0-dev.a1b2c3d", false, 3, true), (publicada.Version, publicada.EsEstable, publicada.Operaciones, publicada.TieneBreaking));
        Assert.Equal(Modulos.Contratos.Specs.AnalizadorSpec.Sha256(SpecsDeEjemplo.OpenApiYaml), publicada.Sha256);
        Assert.Equal($"/api/v1/contratos/{publicada.ContratoId}/versiones/1.2.0-dev.a1b2c3d", creada.Headers.Location?.OriginalString);

        // La misma versión con el mismo contenido: 200 y nada nuevo.
        var otraVez = await PublicarAsync(api, Token(repositorio), Cuerpo(aplicativo, changelog: changelog));
        Assert.Equal(HttpStatusCode.OK, otraVez.StatusCode);
        var repetida = (await otraVez.Content.ReadFromJsonAsync<Publicada>(Ct))!;
        Assert.Equal((publicada.VersionId, false), (repetida.VersionId, repetida.Creada));

        // La misma versión con otro contenido: 409, hay que subir la versión.
        var cambiada = await PublicarAsync(api, Token(repositorio), Cuerpo(aplicativo, spec: SpecsDeEjemplo.OpenApiYaml.Replace("Obtener un trámite", "Consultar un trámite", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.Conflict, cambiada.StatusCode);
        Assert.Equal("version_existente", (await cambiada.Content.ReadFromJsonAsync<Error>(Ct))!.Codigo);

        // Desde release, la estable (la versión sale de info.version).
        var estable = await PublicarAsync(api, Token(repositorio, rama: "release"), Cuerpo(aplicativo, version: null));
        Assert.Equal(HttpStatusCode.Created, estable.StatusCode);

        // Lectura interna con atlas.contratos.ver_internos.
        var desarrollador = await EmpleadoAsync(api, RolesAtlas.Desarrollador);
        var listado = (await (await desarrollador.GetAsync($"/api/v1/contratos?aplicativo={aplicativo}")).Content.ReadFromJsonAsync<Listado>(Ct))!;
        var contrato = Assert.Single(listado.Elementos);
        Assert.Equal(("api-externa", "API Demo", "openapi", aplicativo, 2), (contrato.Codigo, contrato.Nombre, contrato.Tipo, contrato.Aplicativo.Slug, contrato.Versiones));
        Assert.Equal("1.2.0", contrato.UltimaEstable?.Version);
        Assert.Equal("1.2.0", contrato.UltimaVersion?.Version);
        var porTexto = (await (await desarrollador.GetAsync("/api/v1/contratos?texto=api%20demo&tipo=openapi")).Content.ReadFromJsonAsync<Listado>(Ct))!;
        Assert.Contains(porTexto.Elementos, c => c.Id == contrato.Id);

        var versiones = (await (await desarrollador.GetAsync($"/api/v1/contratos/{contrato.Id}/versiones")).Content.ReadFromJsonAsync<List<VersionResumen>>(Ct))!;
        Assert.Equal(["1.2.0", "1.2.0-dev.a1b2c3d"], versiones.Select(v => v.Version));
        var dev = versiones[1];
        Assert.Equal((repositorio, "develop", "0123456789abcdef0123456789abcdef01234567", ".github/workflows/ci.yml", GitHubActionsSimulado.WorkflowOficial, "1234567890", $"github:{repositorio}@develop", true, "vigente"),
            (dev.Repositorio, dev.Rama, dev.Commit, dev.Workflow, dev.WorkflowDelJob, dev.Ejecucion, dev.PublicadoPor, dev.TieneBreaking, dev.Estado));

        var spec = await desarrollador.GetAsync($"/api/v1/contratos/{contrato.Id}/versiones/1.2.0-dev.a1b2c3d/spec");
        Assert.Equal(HttpStatusCode.OK, spec.StatusCode);
        Assert.Equal("application/yaml", spec.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SpecsDeEjemplo.OpenApiYaml, await spec.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"\"{publicada.Sha256}\"", spec.Headers.ETag?.Tag);
        var operaciones = (await (await desarrollador.GetAsync($"/api/v1/contratos/{contrato.Id}/versiones/1.2.0-dev.a1b2c3d/operaciones")).Content.ReadFromJsonAsync<List<Operacion>>(Ct))!;
        Assert.Equal(["DELETE /tramites/{id}", "GET /tramites/{id}", "POST webhook:tramiteRadicado"], operaciones.Select(o => o.Clave));
        Assert.Equal(HttpStatusCode.NotFound, (await desarrollador.GetAsync($"/api/v1/contratos/{contrato.Id}/versiones/9.9.9/spec")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desarrollador.GetAsync($"/api/v1/contratos/{Guid.NewGuid()}/versiones")).StatusCode);

        // Auditada con su procedencia y con su evento en la outbox, una vez por versión nueva (no en el 200).
        await using var plataforma = entorno.Plataforma();
        var auditoria = await plataforma.Auditoria.Where(a => a.Accion == "contratos.version_publicada" && a.ActorId == $"github:{repositorio}@develop").ToListAsync(Ct);
        var evento = Assert.Single(auditoria);
        Assert.Contains("1234567890", evento.Datos, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", evento.Datos, StringComparison.Ordinal);
        var eventos = await plataforma.Outbox.Where(m => m.Tipo == "atlas.contratos.version_publicada" && m.Sobre.Contains(aplicativo)).ToListAsync(Ct);
        Assert.Equal(2, eventos.Count);
        using var sobre = JsonDocument.Parse(eventos.OrderBy(e => e.OcurridoEn).First().Sobre);
        Assert.Equal("1.2.0-dev.a1b2c3d", sobre.RootElement.GetProperty("data").GetProperty("version").GetString());
    }

    [Fact]
    public async Task Repo_rama_aplicativo_o_tipo_fuera_de_la_lista_es_403_y_un_token_vencido_es_401()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (aplicativo, repositorio) = await FuenteAsync(lider, ["openapi"]);
        var (otroAplicativo, _) = await FuenteAsync(lider, ["openapi"]);

        async Task<(HttpStatusCode Estado, string? Codigo)> Intentar(string? token, object? cuerpo = null)
        {
            var respuesta = await PublicarAsync(api, token, cuerpo ?? Cuerpo(aplicativo));
            var texto = await respuesta.Content.ReadAsStringAsync(Ct);
            return (respuesta.StatusCode, texto.StartsWith('{') ? JsonDocument.Parse(texto).RootElement.TryGetProperty("codigo", out var c) ? c.GetString() : null : null);
        }

        Assert.Equal((HttpStatusCode.Forbidden, "fuente_no_autorizada"), await Intentar(Token($"{GitHubActionsSimulado.Propietario}/no-esta")));
        Assert.Equal((HttpStatusCode.Forbidden, "fuente_no_autorizada"), await Intentar(Token(repositorio, rama: "feature/x")));
        Assert.Equal((HttpStatusCode.Forbidden, "aplicativo_no_autorizado"), await Intentar(Token(repositorio), Cuerpo(otroAplicativo)));
        Assert.Equal((HttpStatusCode.Forbidden, "tipo_no_autorizado"), await Intentar(Token(repositorio), Cuerpo(aplicativo, tipo: "asyncapi", spec: SpecsDeEjemplo.AsyncApi3)));
        // Dueño, evento y workflow reutilizable: 403 de la política, antes de leer el cuerpo.
        Assert.Equal(HttpStatusCode.Forbidden, (await Intentar(Token("otra-empresa/api"))).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await Intentar(Token(repositorio, evento: "pull_request"))).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await Intentar(Token(repositorio, jobWorkflow: $"{repositorio}/.github/workflows/propio.yml@refs/heads/develop"))).Estado);
        // Vencido, de otra audiencia, sin firma o sin token: 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Intentar(Token(repositorio, edad: TimeSpan.FromMinutes(30)))).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Intentar(_github.Token(DateTimeOffset.UtcNow, GitHubActionsSimulado.Claims(repositorio: repositorio), audiencia: "https://otro.ejemplo.com"))).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Intentar(GitHubActionsSimulado.SinFirma(DateTimeOffset.UtcNow))).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PublicarAsync(api, null, Cuerpo(aplicativo), csrf: true)).StatusCode);
        // Sin Bearer ni la cabecera de la PWA, ni siquiera pasa la antifalsificación.
        Assert.Equal(HttpStatusCode.Forbidden, (await Intentar(null)).Estado);
        // La cookie de la PWA, ni la del líder técnico, sirve aquí.
        Assert.Equal(HttpStatusCode.Unauthorized, (await lider.EnviarAsync(HttpMethod.Post, "/api/v1/contratos/publicaciones", JsonContent.Create(Cuerpo(aplicativo)))).StatusCode);

        var listado = (await (await lider.GetAsync($"/api/v1/contratos?aplicativo={aplicativo}")).Content.ReadFromJsonAsync<Listado>(Ct))!;
        Assert.Empty(listado.Elementos);
    }

    [Fact]
    public async Task Un_spec_invalido_o_una_version_que_no_cuadra_es_422_y_una_peticion_mal_formada_400()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (aplicativo, repositorio) = await FuenteAsync(lider, ["openapi", "proto"]);

        async Task<(HttpStatusCode, string?)> Intentar(object cuerpo, string rama = "develop")
        {
            var respuesta = await PublicarAsync(api, Token(repositorio, rama), cuerpo);
            return (respuesta.StatusCode, respuesta.StatusCode == HttpStatusCode.UnprocessableEntity ? (await respuesta.Content.ReadFromJsonAsync<Error>(Ct))!.Codigo : null);
        }

        Assert.Equal((HttpStatusCode.UnprocessableEntity, "spec_invalida"), await Intentar(Cuerpo(aplicativo, spec: "openapi: 3.1.0\ninfo:\n  title: Sin version\npaths: {}\n")));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "version_distinta_del_spec"), await Intentar(Cuerpo(aplicativo, version: "2.0.0-dev.a1b2c3d")));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "version_no_permitida_en_rama"), await Intentar(Cuerpo(aplicativo, version: null)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "version_no_permitida_en_rama"), await Intentar(Cuerpo(aplicativo, version: "1.2.0-rc.a1b2c3d"), rama: "release"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "version_invalida"), await Intentar(Cuerpo(aplicativo, tipo: "proto", version: null, spec: SpecsDeEjemplo.Proto)));
        Assert.Equal((HttpStatusCode.BadRequest, null), await Intentar(Cuerpo(aplicativo, tipo: "wsdl")));
        Assert.Equal((HttpStatusCode.BadRequest, null), await Intentar(Cuerpo(aplicativo, contrato: "Con Espacios")));
        Assert.Equal((HttpStatusCode.BadRequest, null), await Intentar(Cuerpo(aplicativo, changelog: "{\"no\": \"es un arreglo\"}")));

        // Un proto válido sí entra, con sus rpc en el índice.
        var proto = await PublicarAsync(api, Token(repositorio), Cuerpo(aplicativo, contrato: "tramites-grpc", tipo: "proto", version: "1.0.0-dev.a1b2c3d", spec: SpecsDeEjemplo.Proto));
        Assert.Equal(HttpStatusCode.Created, proto.StatusCode);
        var publicada = (await proto.Content.ReadFromJsonAsync<Publicada>(Ct))!;
        Assert.Equal(2, publicada.Operaciones);
        Assert.NotEmpty(publicada.Advertencias);
        // Un contrato existente no cambia de tipo.
        var otroTipo = await PublicarAsync(api, Token(repositorio), Cuerpo(aplicativo, contrato: "tramites-grpc", version: "1.2.0-dev.a1b2c3d"));
        Assert.Equal(HttpStatusCode.Conflict, otroTipo.StatusCode);
        Assert.Equal("tipo_distinto", (await otroTipo.Content.ReadFromJsonAsync<Error>(Ct))!.Codigo);
    }

    [Fact]
    public async Task Publica_un_asyncapi_con_su_indice_de_canales()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (aplicativo, repositorio) = await FuenteAsync(lider, ["asyncapi"], workflow: ".github/workflows/ci.yml");

        var respuesta = await PublicarAsync(api, Token(repositorio), Cuerpo(aplicativo, contrato: "eventos", tipo: "asyncapi", version: "2.0.0-dev.a1b2c3d", spec: SpecsDeEjemplo.AsyncApi3));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var publicada = (await respuesta.Content.ReadFromJsonAsync<Publicada>(Ct))!;
        var operaciones = (await (await lider.GetAsync($"/api/v1/contratos/{publicada.ContratoId}/versiones/2.0.0-dev.a1b2c3d/operaciones")).Content.ReadFromJsonAsync<List<Operacion>>(Ct))!;
        Assert.Equal(["receive tramites.radicado", "send tramites.radicado"], operaciones.Select(o => o.Clave));
        Assert.True(operaciones.Single(o => o.Metodo == "send").Publica);
    }

    [Fact]
    public async Task El_lider_administra_la_lista_blanca_con_auditoria_y_un_lector_no_la_ve()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var lector = await EmpleadoAsync(api);
        var (aplicativo, repositorio) = await FuenteAsync(lider, ["openapi"]);

        var lista = (await (await lider.GetAsync($"/api/v1/catalogo/fuentes-publicacion?aplicativo={aplicativo}")).Content.ReadFromJsonAsync<List<Fuente>>(Ct))!;
        var fuente = Assert.Single(lista);
        Assert.Equal(repositorio, fuente.Repositorio);
        Assert.Equal(["develop", "release"], fuente.Ramas);
        Assert.Equal(["openapi"], fuente.Tipos);
        // La lista blanca inicial de la semilla (Atlas publica su propio contrato).
        var todas = (await (await lider.GetAsync("/api/v1/catalogo/fuentes-publicacion")).Content.ReadFromJsonAsync<List<Fuente>>(Ct))!;
        Assert.Contains(todas, f => f.Aplicativo.Slug == "atlas-api" && f.Repositorio == "flitsas/flit-atlas");

        var repetida = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion",
            JsonContent.Create(new { aplicativo, repositorio = repositorio.ToUpperInvariant(), ramas = new[] { "develop" }, tipos = new[] { "openapi" } }));
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        var invalida = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion",
            JsonContent.Create(new { aplicativo, repositorio = "sin-dueno", ramas = Array.Empty<string>(), workflow = "ci.yml", tipos = new[] { "wsdl" } }));
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        var errores = await invalida.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(Ct);
        Assert.Equal(["ramas", "repositorio", "tipos", "workflow"], errores!.Errors.Keys.Order(StringComparer.Ordinal));

        var editada = await lider.EnviarAsync(HttpMethod.Put, $"/api/v1/catalogo/fuentes-publicacion/{fuente.Id}",
            JsonContent.Create(new { aplicativo, repositorio, ramas = new[] { "release" }, workflow = ".github/workflows/publicar.yml", tipos = new[] { "openapi", "asyncapi" } }));
        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        // Con el workflow fijado, otro workflow del repo ya no publica.
        var conOtro = await PublicarAsync(api, Token(repositorio, rama: "release"), Cuerpo(aplicativo, version: null));
        Assert.Equal(HttpStatusCode.Forbidden, conOtro.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await lector.GetAsync("/api/v1/catalogo/fuentes-publicacion")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await lider.EnviarAsync(HttpMethod.Delete, $"/api/v1/catalogo/fuentes-publicacion/{fuente.Id}")).StatusCode);
        Assert.Empty((await (await lider.GetAsync($"/api/v1/catalogo/fuentes-publicacion?aplicativo={aplicativo}")).Content.ReadFromJsonAsync<List<Fuente>>(Ct))!);

        await using var plataforma = entorno.Plataforma();
        var acciones = await plataforma.Auditoria.Where(a => a.RecursoTipo == "catalogo.fuente_publicacion" && a.RecursoId == fuente.Id.ToString())
            .OrderBy(a => a.OcurridoEn).Select(a => a.Accion).ToListAsync(Ct);
        Assert.Equal(["catalogo.fuente_publicacion_creada", "catalogo.fuente_publicacion_actualizada", "catalogo.fuente_publicacion_eliminada"], acciones);
    }
}

internal static class ServiciosPublicacionApi
{
    /// <summary>El JWKS de GitHub simulado para atlas-api: nada sale a GitHub.</summary>
    public static void AddPlataformaPublicacionDePruebaEnApi(this IServiceCollection servicios, GitHubActionsSimulado github) =>
        servicios.AddHttpClient(LlavesGitHubActions.NombreCliente).ConfigurePrimaryHttpMessageHandler(() => new NoDesechar(github));

    private sealed class NoDesechar(HttpMessageHandler interno) : DelegatingHandler(interno)
    {
        // Sin desechar el interno (eso hace Dispose(true) en DelegatingHandler): el simulado vive lo que dura la prueba.
        protected override void Dispose(bool disposing) => base.Dispose(false);
    }
}
