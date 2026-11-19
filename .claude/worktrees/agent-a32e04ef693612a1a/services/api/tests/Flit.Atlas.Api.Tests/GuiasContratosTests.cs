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
/// HU 3.7 (ATLAS-ADR-0040) con atlas-api completo: guías de un contrato publicado desde la CI, cada guardado como revisión
/// inmutable con autor, fecha y nota; restaurar crea una revisión nueva igual a la elegida; publicar lo compartible solo
/// lo hace el responsable principal o secundario (rol implícito con alcance <c>aplicativo:&lt;id&gt;</c>); todo auditado.
/// El responsable lo da aquí <see cref="ResponsablesDePrueba"/> (el reparto real lo prueba <c>ModuloEquipoTests</c>).
/// </summary>
public sealed class GuiasContratosTests(EntornoApi entorno) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GitHubActionsSimulado _github = new();
    private readonly ResponsablesDePrueba _responsables = new();

    public void Dispose() => _github.Dispose();

    /// <summary>Rol implícito de responsable por persona y aplicativo, fijado por la prueba.</summary>
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
        navegador.Ip = $"2001:db8::37:{Random.Shared.Next(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"guias.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Guías", grupos)).StatusCode);
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

    /// <summary>Un aplicativo del catálogo con un contrato OpenAPI publicado desde la CI (1.2.0-dev y 1.2.0).</summary>
    private async Task<(Guid AplicativoId, Guid ContratoId)> ContratoAsync(WebApplicationFactory<Program> api, Navegador lider)
    {
        var slug = $"api-guias-{Guid.NewGuid().ToString("N")[..10]}";
        var creado = await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/aplicativos", new
        {
            slug, nombre = "API Demo Guías", tipo = "producto", grupo = "satelite", estado = "activo", criticidad = "media", manejaDatosPersonales = false,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var aplicativoId = Guid.Parse((await Json(creado)).GetProperty("id").GetString()!);
        var repositorio = $"{GitHubActionsSimulado.Propietario}/{slug}";
        var fuente = await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion", new
        {
            aplicativo = slug, repositorio, ramas = new[] { "develop", "release" }, workflow = (string?)null, tipos = new[] { "openapi" }, nota = "Prueba de la HU 3.7.",
        });
        Assert.Equal(HttpStatusCode.Created, fuente.StatusCode);

        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contratos/publicaciones")
        {
            Content = JsonContent.Create(new { aplicativo = slug, contrato = "api-externa", tipo = "openapi", version = "1.2.0-dev.a1b2c3d", spec = SpecsDeEjemplo.OpenApiYaml }),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            _github.Token(DateTimeOffset.UtcNow.AddSeconds(-5), GitHubActionsSimulado.Claims(repositorio: repositorio, rama: "develop")));
        var publicada = await cliente.SendAsync(peticion, Ct);
        Assert.Equal(HttpStatusCode.Created, publicada.StatusCode);
        return (aplicativoId, Guid.Parse((await Json(publicada)).GetProperty("contratoId").GetString()!));
    }

    private static object Guia(string titulo = "Cómo autenticarse", string contenido = "# Autenticación\n\nPide un token con `$TOKEN`.\n",
        string visibilidad = "interna", string nota = "Primera versión.", int? revisionBase = null, string? operacion = null,
        string? aplicaDesde = null, string? aplicaHasta = null) =>
        new { titulo, contenido, operacion, aplicaDesde, aplicaHasta, visibilidad, nota, revisionBase };

    [Fact]
    public async Task Cada_guardado_es_una_revision_restaurar_crea_otra_igual_y_todo_queda_auditado()
    {
        await using var api = Api();
        var (lider, liderId) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (_, contratoId) = await ContratoAsync(api, lider);

        var creada = await Enviar(lider, HttpMethod.Post, $"/api/v1/contratos/{contratoId}/guias", Guia(operacion: "GET /tramites/{id}", aplicaDesde: "1.0.0"));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var uno = await Json(creada);
        var guiaId = uno.GetProperty("guia").GetProperty("id").GetString()!;
        Assert.Equal($"/api/v1/contratos/guias/{guiaId}", creada.Headers.Location?.OriginalString);
        Assert.Equal(("borrador", 1, "creacion", "GET /tramites/{id}", "1.0.0", liderId.ToString()),
            (uno.GetProperty("guia").GetProperty("estado").GetString(), uno.GetProperty("guia").GetProperty("revisionActual").GetInt32(),
             uno.GetProperty("revision").GetProperty("origen").GetString(), uno.GetProperty("guia").GetProperty("operacion").GetString(),
             uno.GetProperty("guia").GetProperty("aplicaDesde").GetString(), uno.GetProperty("revision").GetProperty("autor").GetProperty("id").GetString()));
        Assert.True(uno.GetProperty("puedeEditar").GetBoolean());
        Assert.False(uno.GetProperty("puedeAprobar").GetBoolean());
        var huellaUno = uno.GetProperty("revision").GetProperty("huella").GetString();

        // Guardar crea la 2; sin cambios o sobre una revisión vieja, 409.
        var ruta = $"/api/v1/contratos/guias/{guiaId}";
        var editada = await Enviar(lider, HttpMethod.Put, ruta, Guia(contenido: "# Autenticación\n\nPide un token y renuévalo cada hora.\n", nota: "Renovación.", revisionBase: 1));
        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        Assert.Equal(2, (await Json(editada)).GetProperty("guia").GetProperty("revisionActual").GetInt32());
        var vieja = await Enviar(lider, HttpMethod.Put, ruta, Guia(contenido: "Otra cosa", revisionBase: 1));
        Assert.Equal((HttpStatusCode.Conflict, "revision_desactualizada"), (vieja.StatusCode, await Codigo(vieja)));
        var igual = await Enviar(lider, HttpMethod.Put, ruta, Guia(contenido: "# Autenticación\n\nPide un token y renuévalo cada hora.\n", nota: "Nada.", revisionBase: 2));
        Assert.Equal((HttpStatusCode.Conflict, "sin_cambios"), (igual.StatusCode, await Codigo(igual)));
        var invalida = await Enviar(lider, HttpMethod.Put, ruta, Guia(operacion: "GET /no-existe", revisionBase: 2));
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.True((await Json(invalida)).GetProperty("errors").TryGetProperty("operacion", out _));

        // Restaurar la 1 crea la 3, igual a la 1 (misma huella), sin tocar la historia.
        var restaurada = await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones/1/restauracion", new { nota = "Volvemos a la primera.", revisionBase = 2 });
        Assert.Equal(HttpStatusCode.Created, restaurada.StatusCode);
        var tres = await Json(restaurada);
        Assert.Equal((3, "restauracion", 1, huellaUno), (tres.GetProperty("revision").GetProperty("numero").GetInt32(), tres.GetProperty("revision").GetProperty("origen").GetString(),
            tres.GetProperty("revision").GetProperty("restauradaDe").GetInt32(), tres.GetProperty("revision").GetProperty("huella").GetString()));
        var otraVez = await Enviar(lider, HttpMethod.Post, $"{ruta}/revisiones/1/restauracion", new { nota = "Otra vez.", revisionBase = 3 });
        Assert.Equal((HttpStatusCode.Conflict, "sin_cambios"), (otraVez.StatusCode, await Codigo(otraVez)));

        var revisiones = (await Json(await lider.GetAsync($"{ruta}/revisiones"))).EnumerateArray().ToList();
        Assert.Equal([3, 2, 1], revisiones.Select(r => r.GetProperty("numero").GetInt32()));
        Assert.Equal(["Volvemos a la primera.", "Renovación.", "Primera versión."], revisiones.Select(r => r.GetProperty("nota").GetString()));
        Assert.Equal("Persona Demo Guías", revisiones[0].GetProperty("autor").GetProperty("nombre").GetString());
        var dos = await Json(await lider.GetAsync($"{ruta}/revisiones/2"));
        Assert.Equal("# Autenticación\n\nPide un token y renuévalo cada hora.\n", dos.GetProperty("contenido").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await lider.GetAsync($"{ruta}/revisiones/9")).StatusCode);

        // La lista del contrato, también filtrada por la versión a la que aplica (desde 1.0.0).
        var lista = await Json(await lider.GetAsync($"/api/v1/contratos/{contratoId}/guias?version=1.2.0"));
        Assert.Equal(guiaId, Assert.Single(lista.GetProperty("elementos").EnumerateArray()).GetProperty("id").GetString());
        Assert.Empty((await Json(await lider.GetAsync($"/api/v1/contratos/{contratoId}/guias?version=0.9.0"))).GetProperty("elementos").EnumerateArray());

        // Publicar una interna la publica quien edita; editar después la deja en borrador sin cambiar lo publicado.
        var publicada = await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 3 });
        Assert.Equal(HttpStatusCode.OK, publicada.StatusCode);
        Assert.Equal(("publicada", 3), ((await Json(publicada)).GetProperty("guia").GetProperty("estado").GetString(),
            (await Json(await lider.GetAsync(ruta))).GetProperty("guia").GetProperty("revisionPublicada").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Put, ruta, Guia(titulo: "Autenticación", nota: "Título corto.", revisionBase: 3))).StatusCode);
        var detalle = await Json(await lider.GetAsync(ruta));
        Assert.Equal(("borrador", 4, 3), (detalle.GetProperty("guia").GetProperty("estado").GetString(),
            detalle.GetProperty("guia").GetProperty("revisionActual").GetInt32(), detalle.GetProperty("guia").GetProperty("revisionPublicada").GetInt32()));

        // Auditado: una entrada por acción, sin el título, el cuerpo ni la nota.
        await using var plataforma = entorno.Plataforma();
        var acciones = await plataforma.Auditoria.Where(a => a.RecursoTipo == "contratos.guia" && a.RecursoId == guiaId)
            .OrderBy(a => a.OcurridoEn).ThenBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(["contratos.guia_creada", "contratos.guia_revisada", "contratos.guia_restaurada", "contratos.guia_publicada", "contratos.guia_revisada"],
            acciones.Select(a => a.Accion));
        Assert.Equal(["1", "2", "3", "3", "4"], acciones.Select(a => a.Version));
        Assert.All(acciones, a => Assert.Equal(liderId.ToString(), a.ActorId));
        Assert.All(acciones, a => Assert.DoesNotContain("Autenticación", a.Datos, StringComparison.Ordinal));
        Assert.All(acciones, a => Assert.DoesNotContain("Volvemos", a.Datos, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lo_compartible_solo_lo_aprueba_el_responsable_y_editar_exige_el_permiso_sobre_el_aplicativo()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var (aplicativoId, contratoId) = await ContratoAsync(api, lider);
        var (otroAplicativo, _) = await ContratoAsync(api, lider);
        var (responsable, responsableId) = await EmpleadoAsync(api, RolesAtlas.Desarrollador, responsableDe: aplicativoId);
        var (deOtro, _) = await EmpleadoAsync(api, RolesAtlas.Desarrollador, responsableDe: otroAplicativo);
        var (desarrollador, _) = await EmpleadoAsync(api, RolesAtlas.Desarrollador);

        // El desarrollador ve, pero no edita; el responsable de otro aplicativo tampoco.
        var guias = $"/api/v1/contratos/{contratoId}/guias";
        var vista = await Json(await desarrollador.GetAsync(guias));
        Assert.Equal((false, false), (vista.GetProperty("puedeEditar").GetBoolean(), vista.GetProperty("puedeAprobar").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(desarrollador, HttpMethod.Post, guias, Guia())).StatusCode);
        var ajena = await Enviar(deOtro, HttpMethod.Post, guias, Guia());
        Assert.Equal((HttpStatusCode.Forbidden, "sin_permiso"), (ajena.StatusCode, await Codigo(ajena)));
        var propia = await Json(await responsable.GetAsync(guias));
        Assert.Equal((true, true), (propia.GetProperty("puedeEditar").GetBoolean(), propia.GetProperty("puedeAprobar").GetBoolean()));

        // El líder crea una compartible, pero no la puede aprobar: no es principal ni secundario.
        var creada = await Json(await Enviar(lider, HttpMethod.Post, guias, Guia(titulo: "Firmas de webhooks", visibilidad: "compartible")));
        var ruta = $"/api/v1/contratos/guias/{creada.GetProperty("guia").GetProperty("id").GetString()}";
        var delLider = await Enviar(lider, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 1 });
        Assert.Equal((HttpStatusCode.Forbidden, "aprobacion_requerida"), (delLider.StatusCode, await Codigo(delLider)));
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(deOtro, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 1 })).StatusCode);

        // El responsable edita (rol implícito sobre su aplicativo) y aprueba.
        var editada = await Enviar(responsable, HttpMethod.Put, ruta, Guia(titulo: "Firmas de webhooks", contenido: "Valida la firma HMAC.", visibilidad: "compartible", nota: "Más claro.", revisionBase: 1));
        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        var vieja = await Enviar(responsable, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 1 });
        Assert.Equal((HttpStatusCode.Conflict, "revision_desactualizada"), (vieja.StatusCode, await Codigo(vieja)));
        var aprobada = await Enviar(responsable, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 2 });
        Assert.Equal(HttpStatusCode.OK, aprobada.StatusCode);
        var guia = (await Json(aprobada)).GetProperty("guia");
        Assert.Equal(("publicada", 2, responsableId.ToString()), (guia.GetProperty("estado").GetString(), guia.GetProperty("revisionPublicada").GetInt32(),
            guia.GetProperty("aprobadaPor").GetProperty("id").GetString()));
        var repetida = await Enviar(responsable, HttpMethod.Post, $"{ruta}/publicacion", new { revision = 2 });
        Assert.Equal((HttpStatusCode.Conflict, "ya_publicada"), (repetida.StatusCode, await Codigo(repetida)));

        await using var plataforma = entorno.Plataforma();
        var aprobacion = await plataforma.Auditoria.SingleAsync(a => a.Accion == "contratos.guia_publicada" && a.RecursoId == guia.GetProperty("id").GetString(), Ct);
        Assert.Equal(responsableId.ToString(), aprobacion.ActorId);
        using var datos = JsonDocument.Parse(aprobacion.Datos!);
        Assert.True(datos.RootElement.GetProperty("aprobacion").GetBoolean());
    }
}
