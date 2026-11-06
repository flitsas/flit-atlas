using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Modulos.Documentacion;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Publicacion;
using Flit.Atlas.Plataforma.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 5.1, 5.2 y 5.5 (ATLAS-ADR-0053) con atlas-api completo: el lote desde la CI con el token OIDC firmado en la prueba y
/// la lista blanca del catálogo; los canales; lo escrito en Atlas con sus estados, revisiones y aprobación; y la visibilidad
/// aplicada en cada consulta. <c>AutorizacionTests</c> recorre además estos endpoints con todos los roles.
/// </summary>
public sealed class ModuloDocumentacionTests(EntornoApi entorno) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GitHubActionsSimulado _github = new();
    private readonly ResponsablesDePrueba _responsables = new();

    public void Dispose() => _github.Dispose();

    private sealed class ResponsablesDePrueba
    {
        public ConcurrentDictionary<Guid, Guid> PorPersona { get; } = new();
    }

    private sealed class RolesDePrueba(ResponsablesDePrueba responsables) : IProveedorRolesImplicitos
    {
        public Task<IReadOnlyList<RolImplicito>> ObtenerAsync(Guid personaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RolImplicito>>(responsables.PorPersona.TryGetValue(personaId, out var aplicativo)
                ? [new RolImplicito(RolesAtlas.Responsable, new AlcanceAsignacion(TiposAlcance.Aplicativo, aplicativo.ToString("D")))]
                : []);
    }

    /// <summary>Lector de responsables según el proveedor de la prueba: un aplicativo sin entrada no tiene responsables.</summary>
    private sealed class LectorDePrueba(ResponsablesDePrueba responsables) : Flit.Atlas.Plataforma.Modulos.ILectorResponsables
    {
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DeAplicativosAsync(IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(responsables.PorPersona
                .Where(p => aplicativos.Contains(p.Value))
                .GroupBy(p => p.Value)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(p => p.Key)]));
    }

    private WebApplicationFactory<Program> Api() => entorno.Api(
        configuracion: new Dictionary<string, string>
        {
            [$"{OpcionesPublicacion.Seccion}:Audiencia"] = GitHubActionsSimulado.Audiencia,
            [$"{OpcionesPublicacion.Seccion}:Propietarios:0"] = GitHubActionsSimulado.Propietario,
            [$"{OpcionesPublicacion.Seccion}:WorkflowsReutilizables:0"] = GitHubActionsSimulado.PatronWorkflowOficial,
        },
        servicios: s =>
        {
            s.AddPlataformaPublicacionDePruebaEnApi(_github);
            s.AddSingleton(_responsables);
            s.Replace(ServiceDescriptor.Scoped<IProveedorRolesImplicitos, RolesDePrueba>());
            s.Replace(ServiceDescriptor.Scoped<Flit.Atlas.Plataforma.Modulos.ILectorResponsables, LectorDePrueba>());
        });

    private async Task<(Navegador Navegador, Guid PersonaId)> EmpleadoAsync(WebApplicationFactory<Program> api, string? rol = null, Guid? responsableDe = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::53:{Random.Shared.Next(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"docs.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Docs", grupos)).StatusCode);
        var yo = await Json(await navegador.GetAsync("/api/v1/plataforma/yo"));
        var persona = Guid.Parse(yo.GetProperty("personaId").GetString()!);
        if (responsableDe is { } aplicativo)
        {
            _responsables.PorPersona[persona] = aplicativo;
        }
        return (navegador, persona);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.True(texto.Length > 0, $"{(int)respuesta.StatusCode} sin cuerpo");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Enviar(Navegador navegador, HttpMethod metodo, string ruta, object cuerpo) =>
        navegador.EnviarAsync(metodo, ruta, JsonContent.Create(cuerpo));

    private static async Task<string?> Codigo(HttpResponseMessage respuesta) =>
        (await Json(respuesta)).TryGetProperty("codigo", out var c) ? c.GetString() : null;

    /// <summary>Un aplicativo y su fuente con el tipo docs, creados por el líder técnico con la API.</summary>
    private static async Task<(string Slug, Guid Id, string Repositorio)> FuenteAsync(Navegador lider, string[]? tipos = null)
    {
        var slug = $"docs-demo-{Guid.NewGuid().ToString("N")[..10]}";
        var creado = await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/aplicativos", new
        {
            slug, nombre = "Docs Demo", tipo = "producto", grupo = "satelite", estado = "activo", criticidad = "media", manejaDatosPersonales = false,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await Json(creado)).GetProperty("id").GetGuid();
        var repositorio = $"{GitHubActionsSimulado.Propietario}/{slug}";
        Assert.Equal(HttpStatusCode.Created, (await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion", new
        {
            aplicativo = slug, repositorio, ramas = new[] { "develop", "staging", "release", "otra" }, workflow = (string?)null, tipos = tipos ?? ["docs"], nota = "Prueba de la HU 5.2.",
        })).StatusCode);
        return (slug, id, repositorio);
    }

    private string Token(string repositorio, string rama = "develop") =>
        _github.Token(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5), GitHubActionsSimulado.Claims(repositorio: repositorio, rama: rama));

    private static async Task<HttpResponseMessage> LoteAsync(WebApplicationFactory<Program> api, string? token, object cuerpo)
    {
        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentacion/publicaciones") { Content = JsonContent.Create(cuerpo) };
        if (token is not null)
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            // Sin Bearer ni esta cabecera responde la antifalsificación (403), no el esquema de la CI.
            peticion.Headers.Add(Identidad.EndpointsIdentidad.CabeceraCsrf, "1");
        }
        return await cliente.SendAsync(peticion, Ct);
    }

    private static string Archivo(string aplicativo, string titulo = "Alta de vehículo", string visibilidad = "interno_negocio", string cuerpo = "# Alta\n\nDocumentación del trámite.",
        string? estado = null, string tipo = "guia_funcional") =>
        $"---\naplicativo: {aplicativo}\ntipo: {tipo}\ntitulo: {titulo}\nvisibilidad: {visibilidad}\naudiencias: [Todos]\nresponsable: Persona Demo\n{(estado is null ? string.Empty : $"estado: {estado}\n")}---\n\n{cuerpo}\n";

    private static object Lote(string ruta, string contenido, string[]? rutasVigentes = null) =>
        new { documentos = new[] { new { ruta, contenido } }, rutasVigentes };

    private static JsonElement Unico(JsonElement lote) => Assert.Single(lote.GetProperty("resultados").EnumerateArray());

    [Fact]
    public async Task Publica_una_version_y_el_mismo_contenido_es_idempotente_y_solo_mueve_el_canal()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (slug, _, repositorio) = await FuenteAsync(lider);
        var contenido = Archivo(slug);

        var creada = await Json(await LoteAsync(api, Token(repositorio), Lote("docs/funcional/alta.md", contenido)));
        Assert.Equal("dev", creada.GetProperty("ambiente").GetString());
        var resultado = Unico(creada);
        Assert.Equal("creada", resultado.GetProperty("resultado").GetString());
        Assert.Equal(1, resultado.GetProperty("version").GetInt32());
        var id = resultado.GetProperty("documentoId").GetGuid();

        // Igual en develop (aun con saltos de línea de Windows): no crea versión.
        var otraVez = Unico(await Json(await LoteAsync(api, Token(repositorio), Lote("docs/funcional/alta.md", contenido.Replace("\n", "\r\n", StringComparison.Ordinal)))));
        Assert.Equal("igual", otraVez.GetProperty("resultado").GetString());

        // Igual en release: mueve el canal pdn a la misma versión.
        var enPdn = Unico(await Json(await LoteAsync(api, Token(repositorio, "release"), Lote("docs/funcional/alta.md", contenido))));
        Assert.Equal("igual", enPdn.GetProperty("resultado").GetString());

        // Una versión nueva en develop no cambia lo que corre en pdn.
        var cambio = Unico(await Json(await LoteAsync(api, Token(repositorio), Lote("docs/funcional/alta.md", Archivo(slug, titulo: "Título nuevo")))));
        Assert.Equal(2, cambio.GetProperty("version").GetInt32());

        var detalle = await Json(await lider.GetAsync($"/api/v1/documentacion/documentos/{id}"));
        Assert.Equal("Alta de vehículo", detalle.GetProperty("documento").GetProperty("titulo").GetString());
        Assert.Equal(1, detalle.GetProperty("documento").GetProperty("versionVigente").GetInt32());
        Assert.Equal("# Alta\n\nDocumentación del trámite.\n", detalle.GetProperty("version").GetProperty("contenido").GetString());
        Assert.Equal("repo", detalle.GetProperty("documento").GetProperty("origen").GetString());
        Assert.False(detalle.GetProperty("puedeEditar").GetBoolean());
        Assert.Equal(["dev:2", "pdn:1"], detalle.GetProperty("canales").EnumerateArray()
            .Select(c => $"{c.GetProperty("ambiente").GetString()}:{c.GetProperty("version").GetInt32()}"));
        var enDev = await Json(await lider.GetAsync($"/api/v1/documentacion/documentos/{id}?canal=dev"));
        Assert.Equal("Título nuevo", enDev.GetProperty("version").GetProperty("version").GetProperty("titulo").GetString());
        Assert.Equal(2, (await Json(await lider.GetAsync($"/api/v1/documentacion/documentos/{id}/versiones"))).GetArrayLength());

        // De repo no se edita.
        var editar = await Enviar(lider, HttpMethod.Put, $"/api/v1/documentacion/documentos/{id}", new
        {
            tipo = "guia_funcional", titulo = "X", contenido = "Y", visibilidad = "interno_negocio", audiencias = Array.Empty<string>(), responsable = "Z", nota = "N", revisionBase = 0,
        });
        Assert.Equal(HttpStatusCode.Conflict, editar.StatusCode);
        Assert.Equal("documento_de_repo", await Codigo(editar));

        await using var scope = api.Services.CreateAsyncScope();
        var plataforma = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var sobres = await plataforma.Outbox.AsNoTracking().Where(o => o.Tipo == "atlas.documentacion.version_publicada").Select(o => o.Sobre).ToListAsync(Ct);
        Assert.Equal(2, sobres.Count(o => o.Contains(id.ToString(), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Cada_archivo_se_resuelve_por_separado_y_lo_invalido_no_frena_a_los_demas()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (slug, _, repositorio) = await FuenteAsync(lider);
        var (otro, _, _) = await FuenteAsync(lider);

        var respuesta = await LoteAsync(api, Token(repositorio), new
        {
            documentos = new[]
            {
                new { ruta = "docs/bien.md", contenido = Archivo(slug) },
                new { ruta = "docs/sin-frente.md", contenido = "# Hola" },
                new { ruta = "docs/mdx.md", contenido = Archivo(slug, cuerpo: "<script>alert(1)</script>") },
                new { ruta = "../fuera.md", contenido = Archivo(slug) },
                new { ruta = "docs/ajeno.md", contenido = Archivo(otro) },
                new { ruta = "docs/tipo.md", contenido = Archivo(slug, tipo: "inventado") },
            },
        });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var resultados = (await Json(respuesta)).GetProperty("resultados").EnumerateArray()
            .ToDictionary(r => r.GetProperty("ruta").GetString()!, r => r.GetProperty("codigo").GetString() ?? r.GetProperty("resultado").GetString());

        Assert.Equal("creada", resultados["docs/bien.md"]);
        Assert.Equal("front_matter_invalido", resultados["docs/sin-frente.md"]);
        Assert.Equal("mdx_invalido", resultados["docs/mdx.md"]);
        Assert.Equal("ruta_invalida", resultados["../fuera.md"]);
        Assert.Equal("aplicativo_no_autorizado", resultados["docs/ajeno.md"]);
        Assert.Equal("documento_invalido", resultados["docs/tipo.md"]);
    }

    [Fact]
    public async Task Fuera_de_la_lista_blanca_es_403_sin_token_401_y_una_rama_sin_ambiente_422()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (slug, _, repositorio) = await FuenteAsync(lider);
        var (soloContratos, _, repoContratos) = await FuenteAsync(lider, ["openapi"]);
        var cuerpo = Lote("docs/a.md", Archivo(slug));

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoteAsync(api, null, cuerpo)).StatusCode);
        var ajeno = await LoteAsync(api, Token($"{GitHubActionsSimulado.Propietario}/no-esta"), cuerpo);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal("fuente_no_autorizada", await Codigo(ajeno));
        var sinDocs = await LoteAsync(api, Token(repoContratos), Lote("docs/a.md", Archivo(soloContratos)));
        Assert.Equal(HttpStatusCode.Forbidden, sinDocs.StatusCode);
        var rama = await LoteAsync(api, Token(repositorio, "otra"), cuerpo);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rama.StatusCode);
        Assert.Equal("rama_sin_ambiente", await Codigo(rama));
        Assert.Equal(HttpStatusCode.BadRequest, (await LoteAsync(api, Token(repositorio), new { documentos = Array.Empty<object>(), rutasVigentes = Array.Empty<string>() })).StatusCode);

        // La cookie de la PWA no sirve aquí.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(lider, HttpMethod.Post, "/api/v1/documentacion/publicaciones", cuerpo)).StatusCode);
    }

    [Fact]
    public async Task Lo_que_desaparece_del_repo_sale_del_canal_y_sin_canales_queda_archivado_con_sus_versiones()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (lector, _) = await EmpleadoAsync(api);
        var (slug, _, repositorio) = await FuenteAsync(lider);
        var a = Archivo(slug, titulo: "Se queda");
        var b = Archivo(slug, titulo: "Se va");
        var cuerpo = new { documentos = new[] { new { ruta = "docs/a.md", contenido = a }, new { ruta = "docs/b.md", contenido = b } } };
        var dev = await Json(await LoteAsync(api, Token(repositorio), cuerpo));
        await LoteAsync(api, Token(repositorio, "release"), cuerpo);
        var idB = dev.GetProperty("resultados")[1].GetProperty("documentoId").GetGuid();

        // Borrarlo en develop no retira lo que corre en pdn.
        var enDev = await Json(await LoteAsync(api, Token(repositorio), Lote("docs/a.md", a, ["docs/a.md"])));
        Assert.Equal(["docs/b.md"], enDev.GetProperty("retiradas").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"/api/v1/documentacion/documentos/{idB}")).StatusCode);

        var enPdn = await Json(await LoteAsync(api, Token(repositorio, "release"), Lote("docs/a.md", a, ["docs/a.md"])));
        Assert.Equal(["docs/b.md"], enPdn.GetProperty("retiradas").EnumerateArray().Select(r => r.GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"/api/v1/documentacion/documentos/{idB}")).StatusCode);
        var archivado = await Json(await lider.GetAsync($"/api/v1/documentacion/documentos/{idB}"));
        Assert.Equal("archivado", archivado.GetProperty("documento").GetProperty("estado").GetString());
        Assert.Equal(1, (await Json(await lider.GetAsync($"/api/v1/documentacion/documentos/{idB}/versiones"))).GetArrayLength());

        // Vuelve al repo: se reactiva.
        var vuelve = Unico(await Json(await LoteAsync(api, Token(repositorio, "release"), Lote("docs/b.md", b))));
        Assert.Equal("igual", vuelve.GetProperty("resultado").GetString());
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"/api/v1/documentacion/documentos/{idB}")).StatusCode);
    }

    [Fact]
    public async Task Cada_rol_solo_ve_su_nivel_tambien_en_la_lista_en_las_versiones_y_en_la_busqueda()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (slug, aplicativoId, repositorio) = await FuenteAsync(lider);
        var marca = $"palabraunica{Guid.NewGuid():N}";
        async Task<Guid> PublicarAsync(string ruta, string visibilidad, string? estado = null)
        {
            var r = Unico(await Json(await LoteAsync(api, Token(repositorio, "release"), Lote(ruta, Archivo(slug, titulo: ruta, visibilidad: visibilidad, estado: estado, cuerpo: marca)))));
            Assert.Equal("creada", r.GetProperty("resultado").GetString());
            return r.GetProperty("documentoId").GetGuid();
        }
        var negocio = await PublicarAsync("docs/negocio.md", "interno_negocio");
        var ti = await PublicarAsync("docs/ti.md", "interno_ti");
        var restringido = await PublicarAsync("docs/restringido.md", "restringido");
        var publico = await PublicarAsync("docs/publico.md", "publico_clientes", "aprobado");
        var sinAprobar = await PublicarAsync("docs/sin-aprobar.md", "publico_clientes");

        async Task<string[]> Ve(Navegador n)
        {
            var lista = await Json(await n.GetAsync($"/api/v1/documentacion/documentos?aplicativo={aplicativoId}"));
            return [.. lista.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("ruta").GetString()!).Order(StringComparer.Ordinal)];
        }
        string[] N = ["docs/negocio.md", "docs/publico.md"];
        var (lector, _) = await EmpleadoAsync(api);
        var (soporte, _) = await EmpleadoAsync(api, RolesAtlas.Soporte);
        var (desarrollador, _) = await EmpleadoAsync(api, RolesAtlas.Desarrollador);
        var (gobierno, _) = await EmpleadoAsync(api, RolesAtlas.GobiernoIa);
        var (admin, _) = await EmpleadoAsync(api, RolesAtlas.Superadmin);
        var (responsable, personaResponsable) = await EmpleadoAsync(api, responsableDe: aplicativoId);

        Assert.Equal(N, await Ve(lector));
        Assert.Equal(N, await Ve(soporte));
        Assert.Equal(["docs/negocio.md", "docs/publico.md", "docs/sin-aprobar.md", "docs/ti.md"], await Ve(desarrollador));
        Assert.Equal(["docs/negocio.md", "docs/publico.md", "docs/restringido.md"], await Ve(gobierno));
        Assert.Equal(["docs/negocio.md", "docs/publico.md", "docs/restringido.md", "docs/sin-aprobar.md", "docs/ti.md"], await Ve(admin));
        Assert.Equal(["docs/negocio.md", "docs/publico.md", "docs/restringido.md", "docs/sin-aprobar.md", "docs/ti.md"], await Ve(responsable));

        // Lo que no ve es 404, no 403, y por cualquier ruta.
        foreach (var ruta in new[] { $"documentos/{ti}", $"documentos/{ti}/versiones", $"documentos/{ti}/versiones/1", $"documentos/{restringido}", $"documentos/{sinAprobar}" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"/api/v1/documentacion/{ruta}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"/api/v1/documentacion/documentos/{negocio}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"/api/v1/documentacion/documentos/{publico}")).StatusCode);

        // La búsqueda full-text aplica el mismo predicado.
        async Task<int> Busca(Navegador n) =>
            (await Json(await n.GetAsync($"/api/v1/documentacion/documentos?texto={marca}"))).GetProperty("total").GetInt32();
        Assert.Equal(2, await Busca(lector));
        Assert.Equal(4, await Busca(desarrollador));
        Assert.Equal(5, await Busca(admin));
        Assert.True(personaResponsable != Guid.Empty);
    }

    [Fact]
    public async Task Una_version_vieja_mas_restringida_no_se_abre_cuando_el_documento_se_abre_despues()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (lector, _) = await EmpleadoAsync(api);
        var (slug, _, repositorio) = await FuenteAsync(lider);

        var v1 = Unico(await Json(await LoteAsync(api, Token(repositorio, "release"), Lote("docs/a.md", Archivo(slug, visibilidad: "interno_ti", cuerpo: "secreto técnico")))));
        var id = v1.GetProperty("documentoId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"/api/v1/documentacion/documentos/{id}")).StatusCode);

        await LoteAsync(api, Token(repositorio, "release"), Lote("docs/a.md", Archivo(slug, visibilidad: "interno_negocio", cuerpo: "ya abierto")));

        var detalle = await Json(await lector.GetAsync($"/api/v1/documentacion/documentos/{id}"));
        Assert.Equal("ya abierto\n", detalle.GetProperty("version").GetProperty("contenido").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"/api/v1/documentacion/documentos/{id}/versiones/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"/api/v1/documentacion/documentos/{id}?version=1")).StatusCode);
        Assert.Equal(1, (await Json(await lector.GetAsync($"/api/v1/documentacion/documentos/{id}/versiones"))).GetArrayLength());
    }

    [Fact]
    public async Task Un_externo_no_ve_ningun_documento_hasta_que_exista_compartir()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (slug, _, repositorio) = await FuenteAsync(lider);
        var id = Unico(await Json(await LoteAsync(api, Token(repositorio, "release"),
            Lote("docs/a.md", Archivo(slug, visibilidad: "publico_clientes", estado: "aprobado", cuerpo: "para clientes"))))).GetProperty("documentoId").GetGuid();

        await using var scope = api.Services.CreateAsyncScope();
        var acceso = scope.ServiceProvider.GetRequiredService<Flit.Atlas.Modulos.Documentacion.Datos.DocumentacionDbContext>();
        var catalogo = await CatalogoDePermisosAsync(scope);
        var externo = PermisosEfectivos.Desde(catalogo, [new ConcesionRol(RolesAtlas.ClienteFuncional, Guid.NewGuid(), AlcanceAsignacion.Global, null, "invitacion")]);

        Assert.True(externo.Tiene(PermisosDocumentacion.Ver));
        var calculado = await AccesoDocumentos.CalcularAsync(new System.Security.Claims.ClaimsPrincipal(), new PermisosFijos(externo),
            scope.ServiceProvider.GetRequiredService<Flit.Atlas.Plataforma.Modulos.ILectorAplicativos>(), Ct);
        Assert.Empty(calculado.Niveles);
        Assert.Empty(calculado.Propios);
        Assert.False(await calculado.Filtrar(acceso, acceso.Documentos).AnyAsync(d => d.Id == id, Ct));
    }

    private static Task<CatalogoPermisos> CatalogoDePermisosAsync(AsyncServiceScope scope) =>
        Task.FromResult(scope.ServiceProvider.GetRequiredService<CatalogoPermisos>());

    private sealed class PermisosFijos(PermisosEfectivos permisos) : IPermisosDeSesion
    {
        public Task<PermisosEfectivos> ObtenerAsync(System.Security.Claims.ClaimsPrincipal usuario, CancellationToken ct = default) => Task.FromResult(permisos);
    }

    private static object Solicitud(Guid? aplicativoId = null, string titulo = "Manual", string contenido = "Cuerpo del manual", string visibilidad = "interno_negocio",
        string nota = "Cambio", int? revisionBase = null, string tipo = "manual_usuario") =>
        new { aplicativoId, tipo, titulo, contenido, visibilidad, audiencias = new[] { "Todos" }, responsable = "Persona Demo", nota, revisionBase };

    [Fact]
    public async Task Lo_escrito_en_atlas_pasa_por_revision_aprobacion_y_restauracion()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (lector, _) = await EmpleadoAsync(api);
        var (_, aplicativoId, _) = await FuenteAsync(lider);
        var ruta = "/api/v1/documentacion/documentos";

        var creado = await Enviar(lider, HttpMethod.Post, ruta, Solicitud(aplicativoId));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await Json(creado)).GetProperty("documento").GetProperty("id").GetGuid();

        // Un borrador sin publicar no lo ve un lector.
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"{ruta}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/publicacion", new { revision = 1 })).StatusCode);

        // Un borrador con MDX inválido se guarda, pero no se envía.
        var conMdx = await Enviar(lider, HttpMethod.Put, $"{ruta}/{id}", Solicitud(contenido: "<script>x</script>", revisionBase: 1));
        Assert.Equal(HttpStatusCode.OK, conMdx.StatusCode);
        var invalido = await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/envio-revision", new { revision = 2 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalido.StatusCode);
        Assert.Equal("mdx_invalido", await Codigo(invalido));

        Assert.Equal("revision_desactualizada", await Codigo(await Enviar(lider, HttpMethod.Put, $"{ruta}/{id}", Solicitud(contenido: "Otro", revisionBase: 1))));
        Assert.Equal("sin_cambios", await Codigo(await Enviar(lider, HttpMethod.Put, $"{ruta}/{id}", Solicitud(contenido: "<script>x</script>", revisionBase: 2))));

        // Restaurar la 1 crea la 3, igual a la 1.
        var restaurada = await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/revisiones/1/restauracion", new { nota = "Volver", revisionBase = 2 });
        Assert.Equal(HttpStatusCode.Created, restaurada.StatusCode);
        var revisiones = await Json(await lider.GetAsync($"{ruta}/{id}/revisiones"));
        Assert.Equal(3, revisiones.GetArrayLength());
        Assert.Equal(revisiones[2].GetProperty("huella").GetString(), revisiones[0].GetProperty("huella").GetString());
        Assert.Equal("restauracion", revisiones[0].GetProperty("origen").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/envio-revision", new { revision = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/devolucion", new { revision = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/envio-revision", new { revision = 3 })).StatusCode);
        var publicado = await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/publicacion", new { revision = 3 });
        Assert.Equal(HttpStatusCode.OK, publicado.StatusCode);
        var detalle = await Json(publicado);
        Assert.Equal("publicado", detalle.GetProperty("documento").GetProperty("estado").GetString());
        Assert.Equal(1, detalle.GetProperty("documento").GetProperty("versionVigente").GetInt32());
        Assert.Equal("pdn", detalle.GetProperty("canales")[0].GetProperty("ambiente").GetString());
        Assert.Equal(3, detalle.GetProperty("version").GetProperty("version").GetProperty("revisionOrigen").GetInt32());
        Assert.Equal("ya_publicada", await Codigo(await Enviar(lider, HttpMethod.Post, $"{ruta}/{id}/publicacion", new { revision = 3 })));

        // Ya publicado, el lector lo ve; un borrador nuevo no cambia lo que lee.
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Put, $"{ruta}/{id}", Solicitud(titulo: "Borrador nuevo", contenido: "Sin publicar", revisionBase: 3))).StatusCode);
        var leido = await Json(await lector.GetAsync($"{ruta}/{id}"));
        Assert.Equal("Manual", leido.GetProperty("documento").GetProperty("titulo").GetString());
        Assert.Equal("Cuerpo del manual", leido.GetProperty("version").GetProperty("contenido").GetString());
        Assert.False(leido.GetProperty("puedeEditar").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.GetAsync($"{ruta}/{id}/revisiones")).StatusCode);

        // Archivar: el lector deja de verlo; las versiones siguen ahí.
        Assert.Equal(HttpStatusCode.OK, (await lider.EnviarAsync(HttpMethod.Post, $"{ruta}/{id}/archivo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"{ruta}/{id}")).StatusCode);
        Assert.Equal(1, (await Json(await lider.GetAsync($"{ruta}/{id}/versiones"))).GetArrayLength());
        Assert.Equal("documento_archivado", await Codigo(await Enviar(lider, HttpMethod.Put, $"{ruta}/{id}", Solicitud(contenido: "Otro más", revisionBase: 4))));

        await using var scope = api.Services.CreateAsyncScope();
        var plataforma = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var acciones = await plataforma.Auditoria.AsNoTracking().Where(a => a.RecursoId == id.ToString()).Select(a => a.Accion).ToListAsync(Ct);
        Assert.Contains("documentacion.documento_creado", acciones);
        Assert.Contains("documentacion.documento_restaurado", acciones);
        Assert.Contains("documentacion.version_publicada", acciones);
        Assert.Contains("documentacion.documento_archivado", acciones);
        var datos = await plataforma.Auditoria.AsNoTracking().Where(a => a.RecursoId == id.ToString()).Select(a => a.Datos).ToListAsync(Ct);
        Assert.DoesNotContain(datos, d => d != null && d.Contains("Cuerpo del manual", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publico_clientes_solo_lo_aprueba_el_responsable_y_sin_responsables_el_lider_tecnico()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (_, conResponsable, _) = await FuenteAsync(lider);
        var (_, sinResponsable, _) = await FuenteAsync(lider);
        var (responsable, _) = await EmpleadoAsync(api, responsableDe: conResponsable);
        var (ajeno, _) = await EmpleadoAsync(api, responsableDe: sinResponsable);
        _responsables.PorPersona.TryRemove(_responsables.PorPersona.First(p => p.Value == sinResponsable).Key, out _);
        var ruta = "/api/v1/documentacion/documentos";

        async Task<Guid> EnRevisionAsync(Navegador n, Guid aplicativo)
        {
            var creado = await Enviar(n, HttpMethod.Post, ruta, Solicitud(aplicativo, visibilidad: "publico_clientes"));
            Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
            var id = (await Json(creado)).GetProperty("documento").GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await Enviar(n, HttpMethod.Post, $"{ruta}/{id}/envio-revision", new { revision = 1 })).StatusCode);
            return id;
        }

        var deResponsable = await EnRevisionAsync(lider, conResponsable);
        var rechazo = await Enviar(lider, HttpMethod.Post, $"{ruta}/{deResponsable}/publicacion", new { revision = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, rechazo.StatusCode);
        Assert.Equal("aprobacion_requerida", await Codigo(rechazo));
        Assert.Equal(HttpStatusCode.OK, (await Enviar(responsable, HttpMethod.Post, $"{ruta}/{deResponsable}/publicacion", new { revision = 1 })).StatusCode);

        var sinNadie = await EnRevisionAsync(lider, sinResponsable);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Post, $"{ruta}/{sinNadie}/publicacion", new { revision = 1 })).StatusCode);

        // Un responsable no edita ni aprueba otro aplicativo: 403 en lo que ve y 403 al crear.
        var otroDoc = await Enviar(responsable, HttpMethod.Post, ruta, Solicitud(sinResponsable));
        Assert.Equal(HttpStatusCode.Forbidden, otroDoc.StatusCode);
        var (lector, _) = await EmpleadoAsync(api);
        var deOtro = await lector.GetAsync($"{ruta}/{sinNadie}");
        Assert.Equal(HttpStatusCode.OK, deOtro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(lector, HttpMethod.Put, $"{ruta}/{sinNadie}", Solicitud(revisionBase: 1))).StatusCode);
        Assert.True(ajeno is not null);
    }

    [Fact]
    public async Task No_se_puede_dejar_un_documento_en_un_nivel_que_uno_no_alcanza_ni_escribir_un_adr()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (_, aplicativoId, _) = await FuenteAsync(lider);

        var nivel = await Enviar(lider, HttpMethod.Post, "/api/v1/documentacion/documentos", Solicitud(aplicativoId, visibilidad: "restringido"));
        Assert.Equal(HttpStatusCode.BadRequest, nivel.StatusCode);
        Assert.True((await Json(nivel)).GetProperty("errors").TryGetProperty("visibilidad", out _));
        var adr = await Enviar(lider, HttpMethod.Post, "/api/v1/documentacion/documentos", Solicitud(aplicativoId, tipo: "adr"));
        Assert.Equal(HttpStatusCode.BadRequest, adr.StatusCode);
    }

    [Fact]
    public async Task El_proveedor_de_busqueda_solo_devuelve_lo_visible_con_la_ruta_del_documento()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (slug, _, repositorio) = await FuenteAsync(lider);
        var marca = $"proveedorunico{Guid.NewGuid():N}";
        var visible = Unico(await Json(await LoteAsync(api, Token(repositorio, "release"), Lote("docs/v.md", Archivo(slug, titulo: "Visible", cuerpo: marca))))).GetProperty("documentoId").GetGuid();
        await LoteAsync(api, Token(repositorio, "release"), Lote("docs/o.md", Archivo(slug, titulo: "Oculto", visibilidad: "interno_ti", cuerpo: marca)));

        await using var scope = api.Services.CreateAsyncScope();
        var proveedor = scope.ServiceProvider.GetServices<IProveedorBusqueda>().Single(p => p.Proveedor == "documentacion");
        var lector = PermisosEfectivos.Desde(scope.ServiceProvider.GetRequiredService<CatalogoPermisos>(), [new ConcesionRol(RolesAtlas.Lector, null, AlcanceAsignacion.Global, null, "base")]);
        var acceso = await AccesoDocumentos.CalcularAsync(new System.Security.Claims.ClaimsPrincipal(), new PermisosFijos(lector),
            scope.ServiceProvider.GetRequiredService<Flit.Atlas.Plataforma.Modulos.ILectorAplicativos>(), Ct);
        var contexto = scope.ServiceProvider.GetRequiredService<Flit.Atlas.Modulos.Documentacion.Datos.DocumentacionDbContext>();

        var hallados = await BusquedaDocumentacion.Vigentes(contexto, acceso, marca).Select(x => x.Documento.Id).ToListAsync(Ct);

        Assert.Equal([visible], hallados);
        Assert.Equal(["documento"], proveedor.Tipos);
    }
}
