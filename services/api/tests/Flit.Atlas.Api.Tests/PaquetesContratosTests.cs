using System.Collections.Concurrent;
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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 4.1 (ATLAS-ADR-0041) con atlas-api completo: paquetes de contratos compartibles en versiones estables, con sus
/// operaciones y sus guías compartibles; cada guardado es una revisión inmutable; publicar marca la última como vigente,
/// fijar vuelve a una anterior sin borrar las nuevas; el responsable arma paquetes solo de sus contratos; todo auditado.
/// El responsable lo da aquí un proveedor de prueba (el reparto real lo prueba <c>ModuloEquipoTests</c>).
/// </summary>
public sealed class PaquetesContratosTests(EntornoApi entorno) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Compartible = "  x-flit-visibilidad: compartible\n";

    private readonly GitHubActionsSimulado _github = new();
    private readonly ResponsablesDePrueba _responsables = new();

    public void Dispose() => _github.Dispose();

    private sealed class ResponsablesDePrueba
    {
        public ConcurrentDictionary<Guid, Guid> PorPersona { get; } = new();
    }

    private sealed class ProveedorDePrueba(ResponsablesDePrueba responsables) : IProveedorRolesImplicitos
    {
        public Task<IReadOnlyList<RolImplicito>> ObtenerAsync(Guid personaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RolImplicito>>(responsables.PorPersona.TryGetValue(personaId, out var aplicativo)
                ? [new RolImplicito(RolesAtlas.Responsable, new AlcanceAsignacion(TiposAlcance.Aplicativo, aplicativo.ToString("D")))]
                : []);
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
            s.Replace(ServiceDescriptor.Scoped<IProveedorRolesImplicitos, ProveedorDePrueba>());
        });

    private async Task<(Navegador Navegador, Guid PersonaId)> EmpleadoAsync(WebApplicationFactory<Program> api, string? rol = null, Guid? responsableDe = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::41:{Random.Shared.Next(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"paquetes.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Paquetes", grupos)).StatusCode);
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

    private static Task<HttpResponseMessage> Enviar(Navegador navegador, HttpMethod metodo, string ruta, object? cuerpo = null) =>
        navegador.EnviarAsync(metodo, ruta, JsonContent.Create(cuerpo ?? new { }));

    private static async Task<string?> Codigo(HttpResponseMessage respuesta) =>
        (await Json(respuesta)).TryGetProperty("codigo", out var c) ? c.GetString() : null;

    private static async Task<IReadOnlyList<string>> Errores(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        return [.. (await Json(respuesta)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
    }

    /// <summary>Un aplicativo del catálogo con su fuente de publicación (develop y release).</summary>
    private static async Task<(Guid Id, string Slug)> AplicativoAsync(Navegador lider)
    {
        var slug = $"api-paquetes-{Guid.NewGuid().ToString("N")[..10]}";
        var creado = await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/aplicativos", new
        {
            slug, nombre = "API Demo Paquetes", tipo = "producto", grupo = "satelite", estado = "activo", criticidad = "media", manejaDatosPersonales = false,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var fuente = await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion", new
        {
            aplicativo = slug, repositorio = $"{GitHubActionsSimulado.Propietario}/{slug}", ramas = new[] { "develop", "release" }, workflow = (string?)null,
            tipos = new[] { "openapi" }, nota = "Prueba de la HU 4.1.",
        });
        Assert.Equal(HttpStatusCode.Created, fuente.StatusCode);
        return (Guid.Parse((await Json(creado)).GetProperty("id").GetString()!), slug);
    }

    /// <summary>Publica una versión desde la CI y devuelve (contratoId, versionId).</summary>
    private async Task<(Guid ContratoId, Guid VersionId)> PublicarAsync(WebApplicationFactory<Program> api, string slug, string contrato, string version,
        bool compartible)
    {
        var info = version.Split('-')[0];
        var spec = SpecsDeEjemplo.OpenApiYaml.Replace("  version: 1.2.0\n", $"  version: {info}\n" + (compartible ? Compartible : string.Empty), StringComparison.Ordinal);
        var rama = version.Contains('-', StringComparison.Ordinal) ? "develop" : "release";
        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contratos/publicaciones")
        {
            Content = JsonContent.Create(new { aplicativo = slug, contrato, tipo = "openapi", version, spec }),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            _github.Token(DateTimeOffset.UtcNow.AddSeconds(-5), GitHubActionsSimulado.Claims(repositorio: $"{GitHubActionsSimulado.Propietario}/{slug}", rama: rama)));
        var publicada = await cliente.SendAsync(peticion, Ct);
        Assert.True(publicada.StatusCode == HttpStatusCode.Created, await publicada.Content.ReadAsStringAsync(Ct));
        var cuerpo = await Json(publicada);
        return (Guid.Parse(cuerpo.GetProperty("contratoId").GetString()!), Guid.Parse(cuerpo.GetProperty("versionId").GetString()!));
    }

    /// <summary>Una guía compartible del contrato, creada por el líder y aprobada por el responsable; devuelve su id.</summary>
    private static async Task<Guid> GuiaCompartibleAsync(Navegador lider, Navegador responsable, Guid contratoId, string titulo = "Firmas de webhooks")
    {
        var creada = await Enviar(lider, HttpMethod.Post, $"/api/v1/contratos/{contratoId}/guias", new
        {
            titulo, contenido = "Valida la firma HMAC con `$SECRETO`.", operacion = (string?)null, aplicaDesde = (string?)null, aplicaHasta = (string?)null,
            visibilidad = "compartible", nota = "Primera versión.", revisionBase = (int?)null,
        });
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var id = Guid.Parse((await Json(creada)).GetProperty("guia").GetProperty("id").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(responsable, HttpMethod.Post, $"/api/v1/contratos/guias/{id}/publicacion", new { revision = 1 })).StatusCode);
        return id;
    }

    private static object Paquete(object[] definicion, string nombre = "Paquete Integradores Demo", string nota = "Primera versión.", int? revisionBase = null) =>
        new { nombre, descripcion = "Para Organismo Demo.", definicion, nota, revisionBase };

    private static object Entrada(Guid contrato, Guid version, string[]? operaciones = null, object[]? guias = null) =>
        new { contratoId = contrato, versionId = version, operaciones = operaciones ?? [], guias = guias ?? [] };

    [Fact]
    public async Task Revisiones_inmutables_publicar_fijar_una_anterior_archivar_y_todo_auditado()
    {
        await using var api = Api();
        var (lider, liderId) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (aplicativoId, slug) = await AplicativoAsync(lider);
        var (responsable, _) = await EmpleadoAsync(api, RolesAtlas.Desarrollador, responsableDe: aplicativoId);
        var (contratoId, v120) = await PublicarAsync(api, slug, "api-externa", "1.2.0", compartible: true);
        var (_, v130) = await PublicarAsync(api, slug, "api-externa", "1.3.0", compartible: true);
        var (_, dev) = await PublicarAsync(api, slug, "api-externa", "1.4.0-dev.a1b2c3d", compartible: true);
        var (interno, internoV) = await PublicarAsync(api, slug, "api-interna", "1.2.0", compartible: false);
        var guiaId = await GuiaCompartibleAsync(lider, responsable, contratoId);

        // Pasos 1 y 3 del asistente: solo el compartible, sin la prerelease, con su guía aprobada.
        var elegibles = (await Json(await lider.GetAsync("/api/v1/contratos/paquetes/elegibles"))).GetProperty("elementos").EnumerateArray().ToList();
        var elegible = Assert.Single(elegibles, e => e.GetProperty("aplicativo").GetProperty("slug").GetString() == slug);
        Assert.Equal(contratoId.ToString(), elegible.GetProperty("id").GetString());
        Assert.Equal(["1.3.0", "1.2.0"], elegible.GetProperty("versiones").EnumerateArray().Select(v => v.GetProperty("version").GetString()));
        Assert.Equal(guiaId.ToString(), Assert.Single(elegible.GetProperty("guias").EnumerateArray()).GetProperty("id").GetString());

        // Paso 2: el índice con publica y etiquetas, filtrado.
        var ops = $"/api/v1/contratos/paquetes/versiones/{v120}/operaciones";
        var todas = await Json(await lider.GetAsync(ops));
        Assert.Equal((3, 3), (todas.GetProperty("total").GetInt32(), todas.GetProperty("elementos").GetArrayLength()));
        Assert.Equal(["public", "tramites"], todas.GetProperty("etiquetas").EnumerateArray().Select(e => e.GetString()));
        var publicas = (await Json(await lider.GetAsync($"{ops}?soloPublicas=true"))).GetProperty("elementos").EnumerateArray().Select(o => o.GetProperty("clave").GetString());
        Assert.Equal(["GET /tramites/{id}", "POST webhook:tramiteRadicado"], publicas);
        var porEtiqueta = (await Json(await lider.GetAsync($"{ops}?etiqueta=TRAMITES"))).GetProperty("elementos").EnumerateArray().Select(o => o.GetProperty("clave").GetString());
        Assert.Equal(["GET /tramites/{id}"], porEtiqueta);
        Assert.Equal(HttpStatusCode.NotFound, (await lider.GetAsync($"/api/v1/contratos/paquetes/versiones/{Guid.NewGuid()}/operaciones")).StatusCode);

        // Reglas al guardar: ni prerelease, ni internos, ni operaciones fuera del índice, ni guías de otro contrato.
        var paquetes = "/api/v1/contratos/paquetes";
        Assert.Equal(["definicion[0].versionId"], await Errores(await Enviar(lider, HttpMethod.Post, paquetes, Paquete([Entrada(contratoId, dev, ["GET /tramites/{id}"])]))));
        Assert.Equal(["definicion[0].versionId"], await Errores(await Enviar(lider, HttpMethod.Post, paquetes, Paquete([Entrada(contratoId, internoV)]))));
        Assert.Equal(["definicion[0].contratoId"], await Errores(await Enviar(lider, HttpMethod.Post, paquetes, Paquete([Entrada(interno, internoV, ["GET /tramites/{id}"])]))));
        Assert.Equal(["definicion[0].operaciones"], await Errores(await Enviar(lider, HttpMethod.Post, paquetes, Paquete([Entrada(contratoId, v120, ["GET /no-existe"])]))));
        Assert.Equal(["definicion[0].guias[0]"], await Errores(await Enviar(lider, HttpMethod.Post, paquetes,
            Paquete([Entrada(contratoId, v120, [], [new { guiaId, revision = 9 }])]))));

        // Revisión 1: un borrador sin operaciones; publicarlo no se puede.
        var creado = await Enviar(lider, HttpMethod.Post, paquetes, Paquete([Entrada(contratoId, v120, [], [new { guiaId, revision = (int?)null }])]));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var uno = await Json(creado);
        var id = uno.GetProperty("paquete").GetProperty("id").GetString()!;
        var ruta = $"{paquetes}/{id}";
        Assert.Equal(ruta, creado.Headers.Location?.OriginalString);
        Assert.Equal(("borrador", 1, true), (uno.GetProperty("paquete").GetProperty("estado").GetString(), uno.GetProperty("paquete").GetProperty("revisionActual").GetInt32(),
            uno.GetProperty("paquete").GetProperty("puedeGestionar").GetBoolean()));
        var guiaEnPaquete = Assert.Single(Assert.Single(uno.GetProperty("revision").GetProperty("definicion").EnumerateArray()).GetProperty("guias").EnumerateArray());
        Assert.Equal(("Firmas de webhooks", 1), (guiaEnPaquete.GetProperty("titulo").GetString(), guiaEnPaquete.GetProperty("revision").GetInt32()));
        var vacio = await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 1 });
        Assert.Equal((HttpStatusCode.Conflict, "no_publicable"), (vacio.StatusCode, await Codigo(vacio)));

        // Revisión 2 con operaciones; repetir lo mismo (en otro orden) es sin_cambios; sobre una vieja, desactualizada.
        var dos = await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones",
            Paquete([Entrada(contratoId, v120, ["POST webhook:tramiteRadicado", "GET /tramites/{id}"], [new { guiaId, revision = 1 }])], nota: "Operaciones públicas.", revisionBase: 1));
        Assert.Equal(HttpStatusCode.Created, dos.StatusCode);
        Assert.Equal($"{ruta}/revisiones/2", dos.Headers.Location?.OriginalString);
        var igual = await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones",
            Paquete([Entrada(contratoId, v120, ["GET /tramites/{id}", "POST webhook:tramiteRadicado"], [new { guiaId, revision = 1 }])], nota: "Nada.", revisionBase: 2));
        Assert.Equal((HttpStatusCode.Conflict, "sin_cambios"), (igual.StatusCode, await Codigo(igual)));
        var vieja = await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones", Paquete([], revisionBase: 1));
        Assert.Equal((HttpStatusCode.Conflict, "revision_desactualizada"), (vieja.StatusCode, await Codigo(vieja)));

        // Publicar la 2; repetir es ya_publicada; publicar una que no es la última, desactualizada.
        Assert.Equal((HttpStatusCode.Conflict, "revision_desactualizada"), await Estado(await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 1 })));
        var publicado = await Json(await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 2 }));
        Assert.Equal(("publicado", 2, false), (publicado.GetProperty("paquete").GetProperty("estado").GetString(),
            publicado.GetProperty("paquete").GetProperty("revisionPublicada").GetInt32(), publicado.GetProperty("paquete").GetProperty("cambiosSinPublicar").GetBoolean()));
        Assert.Equal((HttpStatusCode.Conflict, "ya_publicada"), await Estado(await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 2 })));

        // Revisión 3 (otra versión, sin guía) y publicarla; fijar la 2 la vuelve vigente sin borrar la 3.
        Assert.Equal(HttpStatusCode.Created, (await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones",
            Paquete([Entrada(contratoId, v130, ["GET /tramites/{id}"])], nombre: "Paquete Integradores Demo 1.3", nota: "Versión 1.3.", revisionBase: 2))).StatusCode);
        Assert.True((await Json(await lider.GetAsync(ruta))).GetProperty("paquete").GetProperty("cambiosSinPublicar").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 3 })).StatusCode);
        var fijado = await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones/2/fijacion");
        Assert.Equal(HttpStatusCode.OK, fijado.StatusCode);
        var detalle = await Json(fijado);
        Assert.Equal((3, 2, true), (detalle.GetProperty("paquete").GetProperty("revisionActual").GetInt32(),
            detalle.GetProperty("paquete").GetProperty("revisionPublicada").GetInt32(), detalle.GetProperty("paquete").GetProperty("cambiosSinPublicar").GetBoolean()));
        Assert.Equal([3, 2, 1], detalle.GetProperty("revisiones").EnumerateArray().Select(r => r.GetProperty("numero").GetInt32()));
        Assert.Equal([false, true, false], detalle.GetProperty("revisiones").EnumerateArray().Select(r => r.GetProperty("vigente").GetBoolean()));
        Assert.Equal(["Versión 1.3.", "Operaciones públicas.", "Primera versión."], detalle.GetProperty("revisiones").EnumerateArray().Select(r => r.GetProperty("nota").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "ya_vigente"), await Estado(await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones/2/fijacion")));
        Assert.Equal((HttpStatusCode.Conflict, "es_la_ultima"), await Estado(await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones/3/fijacion")));
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones/9/fijacion")).StatusCode);
        var dosDetalle = await Json(await lider.GetAsync($"{ruta}/revisiones/2"));
        Assert.Equal(("1.2.0", true), (Assert.Single(dosDetalle.GetProperty("definicion").EnumerateArray()).GetProperty("version").GetString(), dosDetalle.GetProperty("vigente").GetBoolean()));

        // Las reglas se revisan otra vez al publicar: con la 1.3.0 retirada, la 3 ya no se puede publicar.
        await using (var plataforma = entorno.Plataforma())
        {
            await plataforma.Database.ExecuteSqlInterpolatedAsync($"UPDATE atlas.contratos_version SET estado = 'retirada' WHERE id = {v130}", Ct);
        }
        var retirada = await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 3 });
        Assert.Equal((HttpStatusCode.Conflict, "no_publicable"), (retirada.StatusCode, await Codigo(retirada)));
        Assert.Contains("retirada", Assert.Single((await Json(retirada)).GetProperty("detalles").EnumerateArray()).GetString(), StringComparison.Ordinal);

        // La lista (también por contrato) y el archivo.
        var lista = (await Json(await lider.GetAsync($"{paquetes}?contrato={contratoId}"))).GetProperty("elementos").EnumerateArray().ToList();
        Assert.Equal(id, Assert.Single(lista).GetProperty("id").GetString());
        Assert.Empty((await Json(await lider.GetAsync($"{paquetes}?contrato={interno}"))).GetProperty("elementos").EnumerateArray());
        var archivado = await Json(await Enviar(lider, HttpMethod.Post, $"{ruta}/archivo"));
        Assert.Equal(("archivado", false), (archivado.GetProperty("paquete").GetProperty("estado").GetString(), archivado.GetProperty("paquete").GetProperty("puedeGestionar").GetBoolean()));
        Assert.Equal((HttpStatusCode.Conflict, "archivado"), await Estado(await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones", Paquete([], revisionBase: 3))));
        Assert.Equal((HttpStatusCode.Conflict, "archivado"), await Estado(await Enviar(lider, HttpMethod.Post, $"{ruta}/archivo")));
        Assert.DoesNotContain((await Json(await lider.GetAsync(paquetes))).GetProperty("elementos").EnumerateArray(), p => p.GetProperty("id").GetString() == id);
        Assert.Contains((await Json(await lider.GetAsync($"{paquetes}?estado=archivado"))).GetProperty("elementos").EnumerateArray(), p => p.GetProperty("id").GetString() == id);

        // Auditado: una entrada por acción, sin el nombre, la descripción ni la nota.
        await using var auditoria = entorno.Plataforma();
        var acciones = await auditoria.Auditoria.Where(a => a.RecursoTipo == "contratos.paquete" && a.RecursoId == id).OrderBy(a => a.OcurridoEn).ThenBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(["contratos.paquete_creado", "contratos.paquete_revisado", "contratos.paquete_publicado", "contratos.paquete_revisado",
            "contratos.paquete_publicado", "contratos.paquete_fijado", "contratos.paquete_archivado"], acciones.Select(a => a.Accion));
        Assert.Equal(["1", "2", "2", "3", "3", "2", "3"], acciones.Select(a => a.Version));
        Assert.All(acciones, a => Assert.Equal(liderId.ToString(), a.ActorId));
        Assert.All(acciones, a => Assert.DoesNotContain("Integradores", a.Datos, StringComparison.Ordinal));
        Assert.All(acciones, a => Assert.DoesNotContain("Organismo Demo", a.Datos, StringComparison.Ordinal));
        Assert.All(acciones, a => Assert.DoesNotContain("Operaciones públicas", a.Datos, StringComparison.Ordinal));
        using var fijacion = JsonDocument.Parse(acciones[5].Datos!);
        Assert.Equal((2, 3), (fijacion.RootElement.GetProperty("revision").GetInt32(), fijacion.RootElement.GetProperty("anterior").GetInt32()));
    }

    [Fact]
    public async Task El_responsable_arma_paquetes_solo_de_sus_contratos_y_los_demas_leen_sin_gestionar()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (aplicativoA, slugA) = await AplicativoAsync(lider);
        var (aplicativoB, slugB) = await AplicativoAsync(lider);
        var (contratoA, versionA) = await PublicarAsync(api, slugA, "api-externa", "1.2.0", compartible: true);
        var (contratoB, versionB) = await PublicarAsync(api, slugB, "api-externa", "1.2.0", compartible: true);
        var (deA, deAId) = await EmpleadoAsync(api, RolesAtlas.Desarrollador, responsableDe: aplicativoA);
        var (deB, _) = await EmpleadoAsync(api, RolesAtlas.Desarrollador, responsableDe: aplicativoB);
        var (desarrollador, _) = await EmpleadoAsync(api, RolesAtlas.Desarrollador);
        var paquetes = "/api/v1/contratos/paquetes";

        // El responsable de A solo ve elegibles sus contratos y no lista operaciones de B.
        var elegibles = (await Json(await deA.GetAsync($"{paquetes}/elegibles"))).GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Contains(contratoA.ToString(), elegibles);
        Assert.DoesNotContain(contratoB.ToString(), elegibles);
        Assert.Equal((HttpStatusCode.Forbidden, "sin_permiso"), await Estado(await deA.GetAsync($"{paquetes}/versiones/{versionB}/operaciones")));

        // Arma con lo suyo; con un contrato ajeno, 403.
        Assert.Equal((HttpStatusCode.Forbidden, "sin_permiso"), await Estado(await Enviar(deA, HttpMethod.Post, paquetes,
            Paquete([Entrada(contratoA, versionA, ["GET /tramites/{id}"]), Entrada(contratoB, versionB, ["GET /tramites/{id}"])]))));
        var creado = await Enviar(deA, HttpMethod.Post, paquetes, Paquete([Entrada(contratoA, versionA, ["GET /tramites/{id}"])]));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var idA = (await Json(creado)).GetProperty("paquete").GetProperty("id").GetString()!;
        var ruta = $"{paquetes}/{idA}";
        Assert.Equal(HttpStatusCode.OK, (await Enviar(deA, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 1 })).StatusCode);
        // No puede meter un contrato ajeno en una revisión nueva.
        Assert.Equal((HttpStatusCode.Forbidden, "sin_permiso"), await Estado(await Enviar(deA, HttpMethod.Post, $"{ruta}/revisiones",
            Paquete([Entrada(contratoA, versionA, ["GET /tramites/{id}"]), Entrada(contratoB, versionB)], revisionBase: 1))));

        // El responsable de B no toca el paquete de A; el desarrollador lo lee, pero no gestiona.
        Assert.False((await Json(await deB.GetAsync(ruta))).GetProperty("paquete").GetProperty("puedeGestionar").GetBoolean());
        Assert.Equal((HttpStatusCode.Forbidden, "sin_permiso"), await Estado(await Enviar(deB, HttpMethod.Post, $"{ruta}/archivo")));
        Assert.Equal((HttpStatusCode.Forbidden, "sin_permiso"), await Estado(await Enviar(deB, HttpMethod.Post, $"{ruta}/revisiones", Paquete([], revisionBase: 1))));
        var lectura = await Json(await desarrollador.GetAsync(paquetes));
        Assert.False(lectura.GetProperty("puedeCrear").GetBoolean());
        Assert.Contains(lectura.GetProperty("elementos").EnumerateArray(), p => $"{paquetes}/{p.GetProperty("id").GetString()}" == ruta && !p.GetProperty("puedeGestionar").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(desarrollador, HttpMethod.Post, paquetes, Paquete([]))).StatusCode);

        // Un paquete vacío solo lo toca quien lo creó (o el líder).
        var vacio = await Enviar(deA, HttpMethod.Post, paquetes, Paquete([]));
        var rutaVacio = $"{paquetes}/{(await Json(vacio)).GetProperty("paquete").GetProperty("id").GetString()}";
        Assert.Equal((HttpStatusCode.Forbidden, "sin_permiso"), await Estado(await Enviar(deB, HttpMethod.Post, $"{rutaVacio}/archivo")));
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Post, $"{rutaVacio}/archivo")).StatusCode);

        await using var plataforma = entorno.Plataforma();
        var publicacion = await plataforma.Auditoria.SingleAsync(a => a.Accion == "contratos.paquete_publicado" && a.RecursoId == idA, Ct);
        Assert.Equal(deAId.ToString(), publicacion.ActorId);
    }

    private static async Task<(HttpStatusCode, string?)> Estado(HttpResponseMessage respuesta) => (respuesta.StatusCode, await Codigo(respuesta));
}
