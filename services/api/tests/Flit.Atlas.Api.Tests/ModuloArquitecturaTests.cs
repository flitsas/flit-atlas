using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Atlas.Modulos.Arquitectura;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Modulos.Arquitectura.Drift;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Publicacion;
using Flit.Atlas.Plataforma.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 6.3 a 6.5 (ATLAS-ADR-0056) con atlas-api completo, Postgres y Redis reales: publicación por OIDC con el emisor simulado, versiones
/// por sha y canal, visibilidad, un externo sin acceso, snapshots y drift con una lectura de Argo CD simulada. Cada prueba usa su base:
/// el trabajo de drift de otra API sobre la misma base pisaría su lectura. <c>AutorizacionTests</c> recorre además estos endpoints con
/// todos los roles.
/// </summary>
public sealed class ModuloArquitecturaTests(EntornoApi entorno) : IDisposable
{
    private const string Ruta = "/api/v1/arquitectura";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GitHubActionsSimulado _github = new();
    private readonly LectorDePrueba _responsables = new();
    private readonly ArgoSimulado _argo = new();

    public void Dispose() => _github.Dispose();

    private sealed class LectorDePrueba : ILectorResponsables
    {
        public ConcurrentDictionary<Guid, Guid> PorPersona { get; } = new();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DeAplicativosAsync(IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(PorPersona
                .Where(p => aplicativos.Contains(p.Value)).GroupBy(p => p.Value)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(p => p.Key)]));
    }

    /// <summary>Argo CD simulado: <b>no</b> prueba el adaptador HTTP contra un Argo real (ATLAS-ADR-0056, «Por verificar»).</summary>
    private sealed class ArgoSimulado : ILectorArgoCd
    {
        public LecturaArgoCd? Lectura { get; set; }

        public bool Configurado(string cluster) => Lectura is not null;

        public Task<LecturaArgoCd> LeerAsync(string cluster, CancellationToken ct) => Task.FromResult(Lectura ?? LecturaArgoCd.SinConfigurar);
    }

    private async Task<(WebApplicationFactory<Program> Api, string Base)> ApiAsync(bool argoSimulado = false, string? repoDespliegue = null)
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        var api = entorno.Api(
            baseDeDatos: baseDeDatos,
            configuracion: new Dictionary<string, string>
            {
                [$"{OpcionesPublicacion.Seccion}:Audiencia"] = GitHubActionsSimulado.Audiencia,
                [$"{OpcionesPublicacion.Seccion}:Propietarios:0"] = GitHubActionsSimulado.Propietario,
                [$"{OpcionesPublicacion.Seccion}:WorkflowsReutilizables:0"] = GitHubActionsSimulado.PatronWorkflowOficial,
                // Sin el trabajo en segundo plano: cada prueba corre el drift cuando le toca.
                [$"{OpcionesArquitectura.Seccion}:DriftHabilitado"] = "false",
                [$"{OpcionesArquitectura.Seccion}:RepositoriosDespliegue:0"] = repoDespliegue ?? $"{GitHubActionsSimulado.Propietario}/gitops-demo",
            },
            servicios: s =>
            {
                s.AddPlataformaPublicacionDePruebaEnApi(_github);
                s.Replace(ServiceDescriptor.Scoped<ILectorResponsables>(_ => _responsables));
                if (argoSimulado)
                {
                    s.Replace(ServiceDescriptor.Singleton<ILectorArgoCd>(_argo));
                }
            });
        return (api, baseDeDatos);
    }

    private async Task<(Navegador Navegador, Guid PersonaId)> EmpleadoAsync(WebApplicationFactory<Program> api, string baseDeDatos, string? rol = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::56:{Random.Shared.Next(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol, baseDeDatos) };
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"arq.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Arquitectura", grupos)).StatusCode);
        var yo = await Json(await navegador.GetAsync("/api/v1/plataforma/yo"));
        return (navegador, Guid.Parse(yo.GetProperty("personaId").GetString()!));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.True(texto.Length > 0, $"{(int)respuesta.StatusCode} sin cuerpo");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Enviar(Navegador navegador, HttpMethod metodo, string ruta, object cuerpo) =>
        navegador.EnviarAsync(metodo, ruta, JsonContent.Create(cuerpo));

    /// <summary>Un aplicativo y su fuente con el tipo likec4, creados por el líder técnico con la API.</summary>
    private static async Task<(string Slug, Guid Id, string Repositorio)> FuenteAsync(Navegador lider, string? repo = null, string[]? tipos = null)
    {
        var slug = $"arq-demo-{Guid.NewGuid().ToString("N")[..10]}";
        var creado = await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/aplicativos", new
        {
            slug, nombre = "Arquitectura Demo", tipo = "producto", grupo = "satelite", estado = "activo", criticidad = "media", manejaDatosPersonales = false,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await Json(creado)).GetProperty("id").GetGuid();
        var repositorio = repo ?? $"{GitHubActionsSimulado.Propietario}/{slug}";
        Assert.Equal(HttpStatusCode.Created, (await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion", new
        {
            aplicativo = slug, repositorio, ramas = new[] { "develop", "staging", "release", "main", "otra" }, workflow = (string?)null, tipos = tipos ?? ["likec4"], nota = "Prueba de la HU 6.3.",
        })).StatusCode);
        return (slug, id, repositorio);
    }

    private string Token(string repositorio, string rama = "develop", string? sha = null)
    {
        var claims = GitHubActionsSimulado.Claims(repositorio: repositorio, rama: rama);
        claims["sha"] = sha ?? Guid.NewGuid().ToString("N") + "01234567";
        return _github.Token(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5), claims);
    }

    private static async Task<HttpResponseMessage> PublicarAsync(WebApplicationFactory<Program> api, string? token, object cuerpo)
    {
        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{Ruta}/publicaciones") { Content = JsonContent.Create(cuerpo) };
        if (token is not null)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            peticion.Headers.Add(Identidad.EndpointsIdentidad.CabeceraCsrf, "1");
        }
        return await cliente.SendAsync(peticion, Ct);
    }

    private static JsonElement ModeloDemo(Func<string, string>? cambiar = null)
    {
        var texto = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "modelo-demo.json"));
        return JsonDocument.Parse(cambiar?.Invoke(texto) ?? texto).RootElement.Clone();
    }

    private static object Modelo(string proyecto, string aplicativo, JsonElement? modelo = null, string? visibilidad = null) =>
        new { proyecto, aplicativo, tipo = "modelo", visibilidad, herramienta = new { nombre = "likec4", version = "1.59.4" }, modelo = modelo ?? ModeloDemo() };

    private static object Snapshot(string proyecto, string aplicativo, string ambiente = "qa", string cluster = "cluster-demo", string[]? cargas = null, string? hosts = null, DateTimeOffset? generadoEn = null) => new
    {
        proyecto, aplicativo, tipo = "despliegue", ambiente, cluster,
        snapshot = new
        {
            ambiente, generadoEn = generadoEn ?? DateTimeOffset.UtcNow,
            cluster = new
            {
                id = cluster, raiz = "root-demo",
                namespaces = new[]
                {
                    new
                    {
                        nombre = "demo-qa", application = "demo-qa", ingressHosts = new[] { hosts ?? "app.ejemplo.com" },
                        workloads = (cargas ?? ["demo-web", "demo-api"]).Select(c => new
                        {
                            kind = "Deployment", nombre = c, replicas = 1, imagenes = new[] { new { imagen = "ghcr.io/flitsas-demo/app", digest = Digest } },
                        }).ToArray(),
                    },
                },
            },
        },
    };

    private static async Task<Guid> IdDe(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Json(respuesta)).GetProperty("publicacionId").GetGuid();
    }

    [Fact]
    public async Task Publica_un_modelo_por_sha_y_canal_y_republicar_es_idempotente()
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (lider, _) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (slug, _, repo) = await FuenteAsync(lider);
        var sha = new string('a', 40);

        var creada = await PublicarAsync(api, Token(repo, sha: sha), Modelo("demo-a", slug));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var cuerpo = await Json(creada);
        Assert.Equal("creada", cuerpo.GetProperty("resultado").GetString());
        Assert.Equal("dev", cuerpo.GetProperty("canal").GetString());
        Assert.Equal("borrador", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(sha, cuerpo.GetProperty("commitSha").GetString());

        var igual = await PublicarAsync(api, Token(repo, sha: sha), Modelo("demo-a", slug));
        Assert.Equal(HttpStatusCode.OK, igual.StatusCode);
        Assert.Equal("igual", (await Json(igual)).GetProperty("resultado").GetString());

        // El mismo sha con otro contenido: las versiones son inmutables.
        var otro = await PublicarAsync(api, Token(repo, sha: sha), Modelo("demo-a", slug, ModeloDemo(t => t.Replace("Portal web", "Portal renombrado", StringComparison.Ordinal))));
        Assert.Equal(HttpStatusCode.Conflict, otro.StatusCode);
        Assert.Equal("sha_con_otro_contenido", (await Json(otro)).GetProperty("codigo").GetString());

        // El mismo sha en otra rama es otro canal; desde release queda publicada.
        var enPdn = await Json(await PublicarAsync(api, Token(repo, "release", sha), Modelo("demo-a", slug)));
        Assert.Equal("pdn", enPdn.GetProperty("canal").GetString());
        Assert.Equal("publicado", enPdn.GetProperty("estado").GetString());

        var proyectos = await Json(await lider.GetAsync($"{Ruta}/proyectos"));
        var proyecto = Assert.Single(proyectos.EnumerateArray());
        Assert.Equal(slug, proyecto.GetProperty("aplicativo").GetProperty("slug").GetString());
        Assert.Equal(["pdn", "dev"], proyecto.GetProperty("canales").EnumerateArray().Select(c => c.GetProperty("canal").GetString()));
    }

    [Fact]
    public async Task Negativos_de_oidc_sin_token_cookie_otro_repo_otra_audiencia_y_workflow_propio()
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (lider, _) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (slug, _, repo) = await FuenteAsync(lider);
        var (otro, _, _) = await FuenteAsync(lider);
        var (soloDocs, _, repoDocs) = await FuenteAsync(lider, tipos: ["docs"]);
        var cuerpo = Modelo("demo-oidc", slug);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PublicarAsync(api, null, cuerpo)).StatusCode);
        // La cookie de un líder técnico no sirve aquí.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(lider, HttpMethod.Post, $"{Ruta}/publicaciones", cuerpo)).StatusCode);

        var ajena = await PublicarAsync(api, Token($"{GitHubActionsSimulado.Propietario}/no-esta"), cuerpo);
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
        Assert.Equal("fuente_no_autorizada", (await Json(ajena)).GetProperty("codigo").GetString());

        var soloDocumentos = await PublicarAsync(api, Token(repoDocs), Modelo("demo-oidc", soloDocs));
        Assert.Equal("fuente_no_autorizada", (await Json(soloDocumentos)).GetProperty("codigo").GetString());

        var deOtroAplicativo = await PublicarAsync(api, Token(repo), Modelo("demo-oidc", otro));
        Assert.Equal("aplicativo_no_autorizado", (await Json(deOtroAplicativo)).GetProperty("codigo").GetString());

        var conOtraAudiencia = _github.Token(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5), GitHubActionsSimulado.Claims(repositorio: repo), audiencia: "https://otro.ejemplo.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await PublicarAsync(api, conOtraAudiencia, cuerpo)).StatusCode);

        var deOtroDueno = GitHubActionsSimulado.Claims(repositorio: "intruso/demo");
        deOtroDueno["repository_owner"] = "intruso";
        Assert.Equal(HttpStatusCode.Forbidden, (await PublicarAsync(api, _github.Token(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5), deOtroDueno), cuerpo)).StatusCode);

        var conWorkflowPropio = GitHubActionsSimulado.Claims(repositorio: repo);
        conWorkflowPropio["job_workflow_ref"] = $"{repo}/.github/workflows/propio.yml@refs/heads/develop";
        Assert.Equal(HttpStatusCode.Forbidden, (await PublicarAsync(api, _github.Token(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5), conWorkflowPropio), cuerpo)).StatusCode);

        var sinCanal = await PublicarAsync(api, Token(repo, "otra"), cuerpo);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinCanal.StatusCode);
        Assert.Equal("rama_sin_ambiente", (await Json(sinCanal)).GetProperty("codigo").GetString());

        // Nada de lo anterior dejó rastro.
        Assert.Empty((await Json(await lider.GetAsync($"{Ruta}/proyectos"))).EnumerateArray());
    }

    [Fact]
    public async Task Un_repo_no_publica_en_el_proyecto_de_otro_ni_un_repo_de_modelos_publica_snapshots()
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (lider, _) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (slug, _, repo) = await FuenteAsync(lider);
        var (otro, _, repoOtro) = await FuenteAsync(lider);
        await IdDe(await PublicarAsync(api, Token(repo), Modelo("demo-propio", slug)));

        var robo = await PublicarAsync(api, Token(repoOtro), Modelo("demo-propio", otro));
        Assert.Equal(HttpStatusCode.Forbidden, robo.StatusCode);
        Assert.Equal("proyecto_de_otro_repo", (await Json(robo)).GetProperty("codigo").GetString());

        var snapshot = await PublicarAsync(api, Token(repo), Snapshot("demo-propio", slug));
        Assert.Equal(HttpStatusCode.Forbidden, snapshot.StatusCode);
        Assert.Equal("despliegue_no_autorizado", (await Json(snapshot)).GetProperty("codigo").GetString());
    }

    [Theory]
    [InlineData("192.0.2.40")]
    [InlineData("password=hunter2")]
    public async Task Un_modelo_con_una_ip_o_un_secreto_se_rechaza_entero_y_el_error_no_cita_el_valor(string sensible)
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (lider, _) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (slug, _, repo) = await FuenteAsync(lider);

        var respuesta = await PublicarAsync(api, Token(repo), Modelo("demo-sensible", slug, ModeloDemo(t => t.Replace("Portal ficticio de radicación", sensible, StringComparison.Ordinal))));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.Contains("contenido_sensible", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(sensible, texto, StringComparison.Ordinal);
        Assert.Empty((await Json(await lider.GetAsync($"{Ruta}/proyectos"))).EnumerateArray());
    }

    [Fact]
    public async Task Interno_ti_lo_ven_los_tecnicos_restringido_el_lider_y_superadmin_y_el_externo_no_ve_nada()
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (lider, _) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (superadmin, _) = await EmpleadoAsync(api, b, RolesAtlas.Superadmin);
        var (desarrollador, _) = await EmpleadoAsync(api, b, RolesAtlas.Desarrollador);
        var (slug, _, repo) = await FuenteAsync(lider);
        var (slug2, _, repo2) = await FuenteAsync(lider);
        var interno = await IdDe(await PublicarAsync(api, Token(repo), Modelo("demo-interno", slug)));
        var restringido = await IdDe(await PublicarAsync(api, Token(repo2), Modelo("demo-restringido", slug2, visibilidad: "restringido")));

        static async Task<string[]> Codigos(Navegador n) =>
            [.. (await Json(await n.GetAsync($"{Ruta}/proyectos"))).EnumerateArray().Select(p => p.GetProperty("codigo").GetString()!).Order(StringComparer.Ordinal)];
        Assert.Equal(["demo-interno", "demo-restringido"], await Codigos(lider));
        Assert.Equal(["demo-interno", "demo-restringido"], await Codigos(superadmin));
        Assert.Equal(["demo-interno"], await Codigos(desarrollador));
        Assert.Equal(HttpStatusCode.OK, (await desarrollador.GetAsync($"{Ruta}/publicaciones/{interno}/modelo")).StatusCode);
        // Lo que no se puede ver responde como lo que no existe.
        Assert.Equal(HttpStatusCode.NotFound, (await desarrollador.GetAsync($"{Ruta}/publicaciones/{restringido}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desarrollador.GetAsync($"{Ruta}/publicaciones/{restringido}/modelo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desarrollador.GetAsync($"{Ruta}/proyectos/demo-restringido/vigente?canal=dev")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desarrollador.GetAsync($"{Ruta}/proyectos/demo-restringido/publicaciones")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desarrollador.GetAsync($"{Ruta}/publicaciones/{interno}/diff/{restringido}")).StatusCode);

        // El asesor comercial tiene atlas.arquitectura.ver por el §7.4, pero un externo no ve nada de esto.
        using var asesor = Navegador.De(api);
        asesor.Ip = $"2001:db8::56:{Random.Shared.Next(1, 0xffff):x}";
        var externo = await entorno.ExternoAsync(api, RolesAtlas.AsesorComercial, DateTimeOffset.UtcNow.AddDays(30), b);
        Assert.Equal(HttpStatusCode.NoContent, (await asesor.EntrarComoAsync(externo.UsuarioId)).StatusCode);
        var yo = await Json(await asesor.GetAsync("/api/v1/plataforma/yo"));
        Assert.Contains(PermisosArquitectura.Ver, yo.GetProperty("permisos").EnumerateArray().Select(p => p.GetString()));
        Assert.Empty((await Json(await asesor.GetAsync($"{Ruta}/proyectos"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await asesor.GetAsync($"{Ruta}/publicaciones/{interno}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asesor.GetAsync($"{Ruta}/publicaciones/{interno}/modelo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asesor.GetAsync($"{Ruta}/publicaciones/{interno}/elementos/demo.web")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asesor.GetAsync($"{Ruta}/proyectos/demo-interno/vigente?canal=dev")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asesor.GetAsync($"{Ruta}/proyectos/demo-interno/publicaciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await asesor.GetAsync($"{Ruta}/drift")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await asesor.GetAsync($"{Ruta}/despliegue/vigentes")).StatusCode);

        foreach (var rol in new[] { RolesAtlas.Soporte, RolesAtlas.Negocio, RolesAtlas.Auditor, RolesAtlas.Lector })
        {
            var (sinPermiso, _) = await EmpleadoAsync(api, b, rol);
            Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync($"{Ruta}/proyectos")).StatusCode);
        }
    }

    /// <summary>
    /// Deja a la vista una brecha que decide el líder (ATLAS-ADR-0056, «Por verificar»): el nivel restringido incluye a gobierno de IA, pero
    /// su rol no tiene <c>atlas.arquitectura.ver</c> en el §7.4 y cambiar las concesiones quedó fuera de este trabajo. Hoy recibe 403. Si el
    /// líder aprueba la concesión, esta prueba debe cambiar: el predicado ya le da el nivel y solo ese nivel.
    /// </summary>
    [Fact]
    public async Task Gobierno_de_ia_hoy_recibe_403_porque_su_rol_no_tiene_el_permiso_del_manifiesto()
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (gobierno, _) = await EmpleadoAsync(api, b, RolesAtlas.GobiernoIa);
        Assert.Equal(HttpStatusCode.Forbidden, (await gobierno.GetAsync($"{Ruta}/proyectos")).StatusCode);
    }

    [Fact]
    public async Task Volver_en_el_tiempo_da_el_modelo_vigente_en_cada_fecha_y_el_diff_las_diferencias()
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (lider, _) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (slug, _, repo) = await FuenteAsync(lider);
        var primera = await IdDe(await PublicarAsync(api, Token(repo), Modelo("demo-tiempo", slug)));
        var fechaPrimera = (await Json(await lider.GetAsync($"{Ruta}/publicaciones/{primera}"))).GetProperty("publicacion").GetProperty("publicadoEn").GetDateTimeOffset();
        await Task.Delay(20, Ct);
        var segundaCuerpo = await PublicarAsync(api, Token(repo), Modelo("demo-tiempo", slug, ModeloDemo(t => t.Replace("Portal web", "Portal nuevo", StringComparison.Ordinal))));
        var segunda = await Json(segundaCuerpo);
        Assert.Equal(HttpStatusCode.Created, segundaCuerpo.StatusCode);
        Assert.True(segunda.GetProperty("diff").GetProperty("cambios").GetInt32() > 0);

        var vigente = await Json(await lider.GetAsync($"{Ruta}/proyectos/demo-tiempo/vigente?canal=dev"));
        Assert.Equal(segunda.GetProperty("publicacionId").GetGuid(), vigente.GetProperty("publicacion").GetProperty("id").GetGuid());
        var en = Uri.EscapeDataString(fechaPrimera.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var entonces = await Json(await lider.GetAsync($"{Ruta}/proyectos/demo-tiempo/vigente?canal=dev&en={en}"));
        Assert.Equal(primera, entonces.GetProperty("publicacion").GetProperty("id").GetGuid());
        var antes = Uri.EscapeDataString(fechaPrimera.AddDays(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(HttpStatusCode.NotFound, (await lider.GetAsync($"{Ruta}/proyectos/demo-tiempo/vigente?canal=dev&en={antes}")).StatusCode);

        var historial = await Json(await lider.GetAsync($"{Ruta}/proyectos/demo-tiempo/publicaciones?canal=dev"));
        Assert.Equal(2, historial.GetProperty("elementos").GetArrayLength());

        var diff = await Json(await lider.GetAsync($"{Ruta}/publicaciones/{primera}/diff/{segunda.GetProperty("publicacionId").GetGuid()}"));
        Assert.Contains(diff.GetProperty("cambios").EnumerateArray(), c => c.GetProperty("id").GetString() == "demo.web");
    }

    [Fact]
    public async Task La_ficha_de_un_elemento_enlaza_el_catalogo_el_adr_y_el_contrato_solo_si_se_puede_ver()
    {
        var (api, b) = await ApiAsync();
        await using var _ = api;
        var (lider, _) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (slug, _, repo) = await FuenteAsync(lider);
        var id = await IdDe(await PublicarAsync(api, Token(repo), Modelo("demo-ficha", slug, ModeloDemo(t => t.Replace("demo-portal", slug, StringComparison.Ordinal)))));

        var detalle = await Json(await lider.GetAsync($"{Ruta}/publicaciones/{id}"));
        var categorias = detalle.GetProperty("vistas").EnumerateArray().Select(v => v.GetProperty("categoria").GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "landscape", "contexto", "contenedores", "componentes", "dinamica", "despliegue" }, categorias);

        var ficha = await Json(await lider.GetAsync($"{Ruta}/publicaciones/{id}/elementos/demo.web"));
        Assert.Equal("Portal web", ficha.GetProperty("titulo").GetString());
        // El contenedor no dice su aplicativo y su repo no es el del proyecto: sin enlace. El sistema sí lo dice.
        Assert.Equal(JsonValueKind.Null, ficha.GetProperty("aplicativo").ValueKind);
        var sistema = await Json(await lider.GetAsync($"{Ruta}/publicaciones/{id}/elementos/demo"));
        Assert.Equal($"/catalogo/{slug}", sistema.GetProperty("aplicativo").GetProperty("ruta").GetString());
        var actor = await Json(await lider.GetAsync($"{Ruta}/publicaciones/{id}/elementos/operador"));
        Assert.Equal(JsonValueKind.Null, actor.GetProperty("aplicativo").ValueKind);
        Assert.Equal("/documentacion?texto=ADR-0001", ficha.GetProperty("adr")[0].GetProperty("rutaAtlas").GetString());
        var contrato = ficha.GetProperty("contratos")[0];
        Assert.Equal("contracts/openapi/demo-api.v1.yaml", contrato.GetProperty("ruta").GetString());
        Assert.Equal(JsonValueKind.Null, contrato.GetProperty("rutaAtlas").ValueKind);
        Assert.Equal(0, ficha.GetProperty("driftAbierto").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await lider.GetAsync($"{Ruta}/publicaciones/{id}/elementos/no.existe")).StatusCode);
    }

    private async Task<(WebApplicationFactory<Program> Api, string Base, Navegador Lider, Guid LiderId, string Slug, Guid Aplicativo, string Repo)> GitopsAsync(bool argoSimulado)
    {
        var repoGitops = $"{GitHubActionsSimulado.Propietario}/gitops-{Guid.NewGuid():N}"[..40];
        var (api, b) = await ApiAsync(argoSimulado, repoGitops);
        var (lider, liderId) = await EmpleadoAsync(api, b, RolesAtlas.LiderTecnico);
        var (slug, aplicativo, repo) = await FuenteAsync(lider, repoGitops);
        return (api, b, lider, liderId, slug, aplicativo, repo);
    }

    [Fact]
    public async Task Un_snapshot_se_publica_se_compara_y_republicar_el_mismo_sha_da_igual_aunque_cambie_la_fecha()
    {
        var (api, _, lider, _, slug, _, repo) = await GitopsAsync(false);
        await using var _d = api;
        var sha = new string('c', 40);

        var primera = await IdDe(await PublicarAsync(api, Token(repo, "main", sha), Snapshot("gitops-demo", slug, generadoEn: DateTimeOffset.UtcNow.AddHours(-6))));
        var igual = await PublicarAsync(api, Token(repo, "main", sha), Snapshot("gitops-demo", slug));
        Assert.Equal(HttpStatusCode.OK, igual.StatusCode);
        Assert.Equal("igual", (await Json(igual)).GetProperty("resultado").GetString());

        var segunda = await IdDe(await PublicarAsync(api, Token(repo, "main"), Snapshot("gitops-demo", slug, cargas: ["demo-web"], hosts: "otro.ejemplo.com")));
        var diff = await Json(await lider.GetAsync($"{Ruta}/despliegue/diff/{primera}/{segunda}"));
        Assert.Contains(diff.GetProperty("bajas").EnumerateArray(), c => c.GetProperty("id").GetString() == "demo-qa/Deployment/demo-api");
        Assert.Contains(diff.GetProperty("altas").EnumerateArray(), c => c.GetProperty("id").GetString() == "otro.ejemplo.com");

        var vigentes = await Json(await lider.GetAsync($"{Ruta}/despliegue/vigentes?ambiente=qa"));
        Assert.Equal(segunda, Assert.Single(vigentes.GetProperty("elementos").EnumerateArray()).GetProperty("id").GetGuid());
        var detalle = await Json(await lider.GetAsync($"{Ruta}/despliegue/publicaciones/{segunda}"));
        Assert.Equal("cluster-demo", detalle.GetProperty("snapshot").GetProperty("cluster").GetProperty("id").GetString());
        // El sha guardado es el del token, no el que diga el cuerpo.
        Assert.Equal(detalle.GetProperty("publicacion").GetProperty("commitSha").GetString(), detalle.GetProperty("snapshot").GetProperty("gitopsSha").GetString());

        var conHost = await PublicarAsync(api, Token(repo, "main"), Snapshot("gitops-demo", slug, hosts: "192.0.2.3"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, conHost.StatusCode);
        Assert.DoesNotContain("192.0.2.3", await conHost.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private static async Task<ResultadoDrift> DriftAsync(WebApplicationFactory<Program> api)
    {
        await using var alcance = api.Services.CreateAsyncScope();
        var resultado = await alcance.ServiceProvider.GetRequiredService<ServicioDrift>().CorrerAsync(null, Ct);
        Assert.NotNull(resultado);
        return resultado;
    }

    private static JsonElement Ambiente(JsonElement panel, string nombre) =>
        panel.GetProperty("ambientes").EnumerateArray().Single(a => a.GetProperty("ambiente").GetString() == nombre);

    [Fact]
    public async Task Sin_argo_configurado_esa_capa_dice_sin_datos_y_nunca_sin_drift()
    {
        var (api, _, lider, _, slug, _, repo) = await GitopsAsync(false);
        await using var _d = api;

        // Nada publicado: las tres capas sin datos.
        var vacio = Ambiente(await Json(await lider.GetAsync($"{Ruta}/drift")), "qa");
        Assert.Equal("sin_datos", vacio.GetProperty("modelo").GetProperty("estado").GetString());
        Assert.Equal("sin_datos", vacio.GetProperty("gitops").GetProperty("estado").GetString());
        Assert.Equal("sin_datos", vacio.GetProperty("modeloGitops").GetString());
        Assert.Equal("sin_datos", vacio.GetProperty("gitopsArgo").GetString());

        await IdDe(await PublicarAsync(api, Token(repo, "main"), Snapshot("gitops-demo", slug)));
        await DriftAsync(api);

        var qa = Ambiente(await Json(await lider.GetAsync($"{Ruta}/drift")), "qa");
        Assert.Equal("con_datos", qa.GetProperty("gitops").GetProperty("estado").GetString());
        Assert.Equal("sin_datos", qa.GetProperty("argo").GetProperty("estado").GetString());
        Assert.Equal("sin_configurar", qa.GetProperty("argo").GetProperty("motivo").GetString());
        Assert.Equal("sin_datos", qa.GetProperty("gitopsArgo").GetString());
        // Sin modelo tampoco se compara, aunque haya snapshot.
        Assert.Equal("sin_datos", qa.GetProperty("modeloGitops").GetString());
    }

    [Fact]
    public async Task El_drift_alerta_una_vez_al_lider_y_al_responsable_no_resuelve_sin_datos_y_resuelve_con_datos()
    {
        var (api, b, lider, liderId, slug, aplicativo, repo) = await GitopsAsync(true);
        await using var _d = api;
        var (responsable, responsableId) = await EmpleadoAsync(api, b, RolesAtlas.Soporte);
        var (ajeno, ajenoId) = await EmpleadoAsync(api, b, RolesAtlas.Desarrollador);
        _responsables.PorPersona[responsableId] = aplicativo;
        var ahora = DateTimeOffset.UtcNow;
        var sana = new AplicacionArgo("demo-qa", "Synced", "Healthy", ahora, [$"ghcr.io/flitsas-demo/app@{Digest}"]);
        var (slugFlit, _, repoFlit) = await FuenteAsync(lider);

        // Staging publica el modelo del canal qa; gitops no tiene demo-api ni el futuro, que es #objetivo.
        await IdDe(await PublicarAsync(api, Token(repoFlit, "staging"), Modelo("demo-drift", slugFlit)));
        await IdDe(await PublicarAsync(api, Token(repo, "main"), Snapshot("gitops-demo", slug, cargas: ["demo-web", "pgbouncer"])));
        _argo.Lectura = new LecturaArgoCd(ResultadoLecturaArgo.Correcta, [sana with { Sync = "OutOfSync" }], null);

        await DriftAsync(api);

        var panel = await Json(await lider.GetAsync($"{Ruta}/drift?ambiente=qa"));
        var hallazgos = panel.GetProperty("hallazgos").EnumerateArray().ToList();
        Assert.Contains(hallazgos, h => h.GetProperty("codigo").GetString() == "ausente_en_gitops" && h.GetProperty("sujeto").GetString() == "demo-api");
        Assert.Contains(hallazgos, h => h.GetProperty("codigo").GetString() == "sin_modelo" && h.GetProperty("sujeto").GetString() == "pgbouncer");
        Assert.Contains(hallazgos, h => h.GetProperty("codigo").GetString() == "sin_sincronizar" && h.GetProperty("severidad").GetString() == "alta");
        Assert.DoesNotContain(hallazgos, h => h.GetProperty("sujeto").GetString() == "demo-futuro");
        var qa = Ambiente(panel, "qa");
        Assert.Equal("con_drift", qa.GetProperty("modeloGitops").GetString());
        Assert.Equal("con_drift", qa.GetProperty("gitopsArgo").GetString());

        async Task<List<JsonElement>> Alertas(Navegador n) =>
            [.. (await Json(await n.GetAsync("/api/v1/plataforma/notificaciones/"))).GetProperty("elementos").EnumerateArray().Where(e => e.GetProperty("tipo").GetString() == AlertasDrift.Tipo.Codigo)];
        var deLider = Assert.Single(await Alertas(lider));
        Assert.Contains("severidad alta", deLider.GetProperty("titulo").GetString(), StringComparison.Ordinal);
        Assert.Equal("/arquitectura/drift?ambiente=qa", deLider.GetProperty("enlace").GetString());
        // El responsable del aplicativo del modelo y de gitops recibe la suya, sin hosts ni nombres de workloads.
        var deResponsable = Assert.Single(await Alertas(responsable));
        Assert.DoesNotContain("demo-api", deResponsable.GetProperty("cuerpo").GetString(), StringComparison.Ordinal);
        Assert.Empty(await Alertas(ajeno));

        // Argo se cae: sin datos no resuelve nada ni se reporta como sin drift.
        _argo.Lectura = LecturaArgoCd.Fallida("sin_respuesta");
        await DriftAsync(api);
        var caido = await Json(await lider.GetAsync($"{Ruta}/drift?ambiente=qa"));
        Assert.Contains(caido.GetProperty("hallazgos").EnumerateArray(), h => h.GetProperty("codigo").GetString() == "sin_sincronizar");
        var capaArgo = Ambiente(caido, "qa");
        Assert.Equal("sin_datos", capaArgo.GetProperty("argo").GetProperty("estado").GetString());
        Assert.Equal("sin_respuesta", capaArgo.GetProperty("argo").GetProperty("motivo").GetString());
        Assert.Equal("con_drift", capaArgo.GetProperty("gitopsArgo").GetString());

        // Y no vuelve a alertar lo mismo.
        Assert.Single(await Alertas(lider));

        // Argo vuelve sano: ahora sí se resuelve, y se puede decir «sin drift» en esa comparación.
        _argo.Lectura = new LecturaArgoCd(ResultadoLecturaArgo.Correcta, [sana], null);
        await DriftAsync(api);
        var sano = await Json(await lider.GetAsync($"{Ruta}/drift?ambiente=qa"));
        Assert.DoesNotContain(sano.GetProperty("hallazgos").EnumerateArray(), h => h.GetProperty("capa").GetString() == "gitops_argo");
        Assert.Equal("sin_drift", Ambiente(sano, "qa").GetProperty("gitopsArgo").GetString());
        var resueltos = await Json(await lider.GetAsync($"{Ruta}/drift?ambiente=qa&estado=resuelto"));
        Assert.Contains(resueltos.GetProperty("hallazgos").EnumerateArray(), h => h.GetProperty("codigo").GetString() == "sin_sincronizar");

        // Reaparece: reabre la misma fila y alerta otra vez.
        _argo.Lectura = new LecturaArgoCd(ResultadoLecturaArgo.Correcta, [sana with { Sync = "OutOfSync" }], null);
        await DriftAsync(api);
        Assert.Equal(2, (await Alertas(lider)).Count);

        await using var contexto = api.Services.CreateAsyncScope();
        var bd = contexto.ServiceProvider.GetRequiredService<ArquitecturaDbContext>();
        Assert.Equal(1, await bd.Hallazgos.CountAsync(h => h.Codigo == "sin_sincronizar", Ct));
        // Ningún hallazgo guardó hosts.
        Assert.False(await bd.Hallazgos.AnyAsync(h => h.Descripcion.Contains("ejemplo.com"), Ct));
    }

    [Fact]
    public async Task Publicar_dispara_el_trabajo_de_drift_en_segundo_plano()
    {
        var repoGitops = $"{GitHubActionsSimulado.Propietario}/gitops-{Guid.NewGuid():N}"[..40];
        var baseDeDatos = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(
            baseDeDatos: baseDeDatos,
            configuracion: new Dictionary<string, string>
            {
                [$"{OpcionesPublicacion.Seccion}:Audiencia"] = GitHubActionsSimulado.Audiencia,
                [$"{OpcionesPublicacion.Seccion}:Propietarios:0"] = GitHubActionsSimulado.Propietario,
                [$"{OpcionesPublicacion.Seccion}:WorkflowsReutilizables:0"] = GitHubActionsSimulado.PatronWorkflowOficial,
                [$"{OpcionesArquitectura.Seccion}:RepositoriosDespliegue:0"] = repoGitops,
            },
            servicios: s => s.AddPlataformaPublicacionDePruebaEnApi(_github));
        var (lider, _) = await EmpleadoAsync(api, baseDeDatos, RolesAtlas.LiderTecnico);
        var (slug, _, repo) = await FuenteAsync(lider, repoGitops);
        var (slugM, _, repoM) = await FuenteAsync(lider);
        await IdDe(await PublicarAsync(api, Token(repoM, "staging"), Modelo("demo-bg", slugM)));
        await IdDe(await PublicarAsync(api, Token(repo, "main"), Snapshot("gitops-bg", slug, cargas: ["demo-web"])));

        for (var i = 0; i < 100; i++)
        {
            var panel = await Json(await lider.GetAsync($"{Ruta}/drift?ambiente=qa"));
            if (panel.GetProperty("hallazgos").GetArrayLength() > 0)
            {
                Assert.Contains(panel.GetProperty("hallazgos").EnumerateArray(), h => h.GetProperty("sujeto").GetString() == "demo-api");
                return;
            }
            await Task.Delay(100, Ct);
        }
        Assert.Fail("El trabajo de drift no corrió después de publicar.");
    }
}
