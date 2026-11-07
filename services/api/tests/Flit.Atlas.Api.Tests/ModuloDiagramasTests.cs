using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Atlas.Modulos.Diagramas;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Render;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 6.6 y 6.7 (ATLAS-ADR-0054) con atlas-api completo: versiones, restauración, comentarios, referencias al catálogo, XML
/// limpiado y la visibilidad en cada consulta. <c>AutorizacionTests</c> recorre además estos endpoints con todos los roles.
/// </summary>
public sealed class ModuloDiagramasTests(EntornoApi entorno)
{
    private const string Ruta = "/api/v1/diagramas";
    private const string Mermaid = "graph TD; Radicar-->Revisar;";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ResponsablesDePrueba _responsables = new();

    private readonly RenderLienzoSimulado _render = new();

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

    private WebApplicationFactory<Program> Api() => entorno.Api(servicios: s =>
    {
        s.AddSingleton(_responsables);
        s.Replace(ServiceDescriptor.Scoped<IProveedorRolesImplicitos, RolesDePrueba>());
        s.Replace(ServiceDescriptor.Singleton<IRenderLienzo>(_render));
    });

    private async Task<Navegador> EmpleadoAsync(WebApplicationFactory<Program> api, string? rol = null, Guid? responsableDe = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::54:{Random.Shared.Next(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"dgm.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Diagramas", grupos)).StatusCode);
        if (responsableDe is { } aplicativo)
        {
            var yo = await Json(await navegador.GetAsync("/api/v1/plataforma/yo"));
            _responsables.PorPersona[Guid.Parse(yo.GetProperty("personaId").GetString()!)] = aplicativo;
        }
        return navegador;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.True(texto.Length > 0, $"{(int)respuesta.StatusCode} sin cuerpo");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Enviar(Navegador navegador, HttpMethod metodo, string ruta, object cuerpo) =>
        navegador.EnviarAsync(metodo, ruta, JsonContent.Create(cuerpo));

    private static async Task<Guid> AplicativoAsync(Navegador lider)
    {
        var slug = $"dgm-demo-{Guid.NewGuid().ToString("N")[..10]}";
        var creado = await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/aplicativos", new
        {
            slug, nombre = "Diagramas Demo " + slug, tipo = "producto", grupo = "satelite", estado = "activo", criticidad = "media", manejaDatosPersonales = false,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        return (await Json(creado)).GetProperty("id").GetGuid();
    }

    private static string Codigo() => $"flujo-{Guid.NewGuid().ToString("N")[..10]}";

    private static object Solicitud(Guid aplicativo, string? codigo = null, string tipo = "mermaid", string fuente = Mermaid, string visibilidad = "interno_negocio", string titulo = "Flujo de radicación") =>
        new { codigo = codigo ?? Codigo(), aplicativoId = aplicativo, tipo, titulo, visibilidad, fuente, nota = "Primera versión" };

    private static object Guardado(int versionBase, string fuente = "graph TD; A-->B;", string visibilidad = "interno_negocio", string titulo = "Flujo de radicación") =>
        new { titulo, visibilidad, fuente, nota = "Ajuste", versionBase };

    private static async Task<JsonElement> CrearAsync(Navegador navegador, object solicitud)
    {
        var creado = await Enviar(navegador, HttpMethod.Post, Ruta, solicitud);
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        return await Json(creado);
    }

    private static Guid Id(JsonElement detalle) => detalle.GetProperty("diagrama").GetProperty("id").GetGuid();

    private static async Task<string?> CodigoError(HttpResponseMessage respuesta) => (await Json(respuesta)).GetProperty("codigo").GetString();

    [Fact]
    public async Task Guardar_crea_versiones_y_restaurar_crea_otra_igual_sin_reescribir_la_historia()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var id = Id(await CrearAsync(lider, Solicitud(await AplicativoAsync(lider))));

        // Sin cambios y con una base vieja: 409.
        var igual = await Enviar(lider, HttpMethod.Put, $"{Ruta}/{id}", Guardado(1, Mermaid));
        Assert.Equal(HttpStatusCode.Conflict, igual.StatusCode);
        Assert.Equal("sin_cambios", await CodigoError(igual));

        var guardado = await Enviar(lider, HttpMethod.Put, $"{Ruta}/{id}", Guardado(1));
        Assert.Equal(HttpStatusCode.OK, guardado.StatusCode);
        Assert.Equal(2, (await Json(guardado)).GetProperty("diagrama").GetProperty("versionActual").GetInt32());
        var vieja = await Enviar(lider, HttpMethod.Put, $"{Ruta}/{id}", Guardado(1, "graph TD; C-->D;"));
        Assert.Equal("version_desactualizada", await CodigoError(vieja));

        var restaurada = await Enviar(lider, HttpMethod.Post, $"{Ruta}/{id}/versiones/1/restauracion", new { nota = "Volver", versionBase = 2 });
        Assert.Equal(HttpStatusCode.Created, restaurada.StatusCode);
        var detalle = await Json(restaurada);
        Assert.Equal(Mermaid, detalle.GetProperty("version").GetProperty("fuente").GetString());
        Assert.Equal(1, detalle.GetProperty("version").GetProperty("version").GetProperty("restauradaDe").GetInt32());

        var versiones = await Json(await lider.GetAsync($"{Ruta}/{id}/versiones"));
        Assert.Equal([3, 2, 1], versiones.EnumerateArray().Select(v => v.GetProperty("numero").GetInt32()));
        Assert.Equal(versiones[2].GetProperty("huella").GetString(), versiones[0].GetProperty("huella").GetString());
        Assert.Equal("graph TD; A-->B;", (await Json(await lider.GetAsync($"{Ruta}/{id}/versiones/2"))).GetProperty("fuente").GetString());
    }

    [Fact]
    public async Task Un_codigo_repetido_o_datos_invalidos_no_se_guardan()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var aplicativo = await AplicativoAsync(lider);
        var codigo = Codigo();
        await CrearAsync(lider, Solicitud(aplicativo, codigo));

        var repetido = await Enviar(lider, HttpMethod.Post, Ruta, Solicitud(aplicativo, codigo));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("codigo_existente", await CodigoError(repetido));

        foreach (var mala in new[]
        {
            Solicitud(aplicativo, "Código malo"),
            Solicitud(aplicativo, tipo: "otro"),
            Solicitud(Guid.NewGuid()),
            Solicitud(aplicativo, visibilidad: "publico_clientes"),
            Solicitud(aplicativo, fuente: new string('x', ReglasDiagramas.LargoMermaid + 1)),
            Solicitud(aplicativo, tipo: "lienzo", fuente: "{}"),
            Solicitud(aplicativo, tipo: "bpmn", fuente: "<a/>"),
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(lider, HttpMethod.Post, Ruta, mala)).StatusCode);
        }
    }

    [Fact]
    public async Task El_bpmn_se_guarda_limpio_y_una_dtd_no_entra()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var aplicativo = await AplicativoAsync(lider);
        const string Definiciones = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" id="Def" targetNamespace="http://ejemplo.com/b">
              <bpmn:process id="P"><bpmn:scriptTask id="T"><bpmn:script>evil()</bpmn:script></bpmn:scriptTask></bpmn:process>
              <bpmndi:BPMNDiagram id="D"><bpmndi:BPMNPlane id="Pl" bpmnElement="P"/></bpmndi:BPMNDiagram>
            </bpmn:definitions>
            """;

        var detalle = await CrearAsync(lider, Solicitud(aplicativo, tipo: "bpmn", fuente: Definiciones));
        var fuente = detalle.GetProperty("version").GetProperty("fuente").GetString()!;
        Assert.Contains("scriptTask", fuente);
        Assert.DoesNotContain("evil", fuente);

        var conDtd = await Enviar(lider, HttpMethod.Post, Ruta, Solicitud(aplicativo, tipo: "bpmn",
            fuente: "<!DOCTYPE d [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>" + Definiciones));
        Assert.Equal(HttpStatusCode.BadRequest, conDtd.StatusCode);
    }

    [Fact]
    public async Task El_lienzo_proyecta_sus_nodos_y_la_ficha_ve_los_diagramas_que_mencionan_al_aplicativo()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var dueño = await AplicativoAsync(lider);
        var mencionado = await AplicativoAsync(lider);
        var fuente = JsonSerializer.Serialize(new
        {
            nodos = new object[]
            {
                new { id = "a", tipo = "sistema", etiqueta = "Radicación", x = 0, y = 0, aplicativoId = mencionado },
                new { id = "b", tipo = "nota", etiqueta = "Nota", x = 10, y = 10 },
            },
            relaciones = new[] { new { id = "r", origen = "a", destino = "b" } },
        });

        var detalle = await CrearAsync(lider, Solicitud(dueño, tipo: "lienzo", fuente: fuente));
        var id = Id(detalle);
        Assert.Equal(mencionado, Assert.Single(detalle.GetProperty("referencias").EnumerateArray()).GetProperty("aplicativoId").GetGuid());
        var lista = await Json(await lider.GetAsync($"{Ruta}?aplicativo={mencionado}"));
        Assert.Equal(id, Assert.Single(lista.GetProperty("elementos").EnumerateArray()).GetProperty("id").GetGuid());

        // La siguiente versión ya no lo menciona: la ficha deja de listarlo, y la versión vieja sigue igual.
        var sin = JsonSerializer.Serialize(new { nodos = new[] { new { id = "b", tipo = "nota", etiqueta = "Nota", x = 10, y = 10 } }, relaciones = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Put, $"{Ruta}/{id}", Guardado(1, sin))).StatusCode);
        Assert.Empty((await Json(await lider.GetAsync($"{Ruta}?aplicativo={mencionado}"))).GetProperty("elementos").EnumerateArray());
        Assert.Contains(mencionado.ToString(), (await Json(await lider.GetAsync($"{Ruta}/{id}/versiones/1"))).GetProperty("fuente").GetString());

        // Restaurar vuelve a proyectar.
        Assert.Equal(HttpStatusCode.Created, (await Enviar(lider, HttpMethod.Post, $"{Ruta}/{id}/versiones/1/restauracion", new { nota = "Volver", versionBase = 2 })).StatusCode);
        Assert.Single((await Json(await lider.GetAsync($"{Ruta}?aplicativo={mencionado}"))).GetProperty("elementos").EnumerateArray());
    }

    [Fact]
    public async Task La_visibilidad_se_aplica_en_cada_consulta_y_lo_que_no_se_ve_es_404()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var lector = await EmpleadoAsync(api);
        var aplicativo = await AplicativoAsync(lider);
        var codigo = Codigo();
        var titulo = $"Oculto {Guid.NewGuid():N}";
        var id = Id(await CrearAsync(lider, Solicitud(aplicativo, codigo, visibilidad: "interno_ti", titulo: titulo)));

        Assert.Equal(HttpStatusCode.OK, (await lider.GetAsync($"{Ruta}/por-codigo/{codigo}")).StatusCode);
        foreach (var ruta in new[] { $"{Ruta}/{id}", $"{Ruta}/por-codigo/{codigo}", $"{Ruta}/{id}/versiones", $"{Ruta}/{id}/versiones/1", $"{Ruta}/{id}/comentarios" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync(ruta)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(lector, HttpMethod.Post, $"{Ruta}/{id}/comentarios", new { versionNumero = 1, texto = "Hola" })).StatusCode);
        Assert.Empty((await Json(await lector.GetAsync($"{Ruta}?texto={titulo}"))).GetProperty("elementos").EnumerateArray());

        // Al abrirlo a negocio, el lector lo ve; la versión vieja, más restringida, sigue oculta para él.
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Put, $"{Ruta}/{id}", Guardado(1, visibilidad: "interno_negocio", titulo: titulo))).StatusCode);
        var detalle = await Json(await lector.GetAsync($"{Ruta}/por-codigo/{codigo}"));
        Assert.False(detalle.GetProperty("puedeEditar").GetBoolean());
        Assert.Equal(2, Assert.Single((await Json(await lector.GetAsync($"{Ruta}/{id}/versiones"))).EnumerateArray()).GetProperty("numero").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"{Ruta}/{id}/versiones/1")).StatusCode);

        // Y al volverlo a cerrar, deja de verlo.
        Assert.Equal(HttpStatusCode.OK, (await Enviar(lider, HttpMethod.Put, $"{Ruta}/{id}", Guardado(2, "graph TD; X-->Y;", "interno_ti", titulo))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"{Ruta}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Escribir_exige_el_permiso_sobre_el_aplicativo_y_el_responsable_solo_en_el_suyo()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var aplicativo = await AplicativoAsync(lider);
        var otro = await AplicativoAsync(lider);
        var delOtro = Id(await CrearAsync(lider, Solicitud(otro)));
        var lector = await EmpleadoAsync(api);
        var responsable = await EmpleadoAsync(api, responsableDe: aplicativo);

        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(lector, HttpMethod.Post, Ruta, Solicitud(aplicativo))).StatusCode);

        var propio = await CrearAsync(responsable, Solicitud(aplicativo, visibilidad: "interno_ti"));
        Assert.True(propio.GetProperty("puedeEditar").GetBoolean());

        var ajeno = await Enviar(responsable, HttpMethod.Post, Ruta, Solicitud(otro));
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal("sin_permiso", await CodigoError(ajeno));
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(responsable, HttpMethod.Put, $"{Ruta}/{delOtro}", Guardado(1))).StatusCode);

        // Un desarrollador no puede dejar un diagrama en un nivel que no alcanza.
        var desarrollador = await EmpleadoAsync(api, RolesAtlas.Desarrollador);
        var restringido = await Enviar(desarrollador, HttpMethod.Post, Ruta, Solicitud(aplicativo, visibilidad: "restringido"));
        Assert.Equal(HttpStatusCode.BadRequest, restringido.StatusCode);

        // El líder técnico sí alcanza ese nivel (decisión del líder, 06/11/2026).
        var liderTecnico = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        Assert.Equal(HttpStatusCode.Created, (await Enviar(liderTecnico, HttpMethod.Post, Ruta, Solicitud(otro, visibilidad: "restringido"))).StatusCode);
    }

    private sealed class PermisosFijos(PermisosEfectivos permisos) : IPermisosDeSesion
    {
        public Task<PermisosEfectivos> ObtenerAsync(System.Security.Claims.ClaimsPrincipal usuario, CancellationToken ct = default) => Task.FromResult(permisos);
    }

    [Fact]
    public async Task Un_externo_no_ve_ningun_diagrama_aunque_tenga_el_permiso_de_ver()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        await CrearAsync(lider, Solicitud(await AplicativoAsync(lider)));
        await using var scope = api.Services.CreateAsyncScope();
        var externo = PermisosEfectivos.Desde(scope.ServiceProvider.GetRequiredService<CatalogoPermisos>(),
            [new ConcesionRol(RolesAtlas.ClienteFuncional, Guid.NewGuid(), AlcanceAsignacion.Global, null, "invitacion")]);

        var acceso = await AccesoDiagramas.CalcularAsync(new System.Security.Claims.ClaimsPrincipal(), new PermisosFijos(externo),
            scope.ServiceProvider.GetRequiredService<Flit.Atlas.Plataforma.Modulos.ILectorAplicativos>(), Ct);
        var contexto = scope.ServiceProvider.GetRequiredService<Flit.Atlas.Modulos.Diagramas.Datos.DiagramasDbContext>();

        Assert.True(externo.Tiene(PermisosDiagramas.Ver));
        Assert.Empty(acceso.Niveles);
        Assert.Empty(await acceso.Filtrar(contexto.Diagramas.AsNoTracking()).ToListAsync(Ct));
    }

    [Fact]
    public async Task Los_comentarios_se_auditan_sin_el_texto_y_respetan_nodo_y_version()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var lector = await EmpleadoAsync(api);
        var fuente = JsonSerializer.Serialize(new { nodos = new[] { new { id = "a", tipo = "sistema", etiqueta = "A", x = 0, y = 0 } }, relaciones = Array.Empty<object>() });
        var id = Id(await CrearAsync(lider, Solicitud(await AplicativoAsync(lider), tipo: "lienzo", fuente: fuente)));
        var secreto = $"Comentario {Guid.NewGuid():N}";

        var escrito = await Enviar(lector, HttpMethod.Post, $"{Ruta}/{id}/comentarios", new { versionNumero = 1, nodoClave = "a", texto = secreto });
        Assert.Equal(HttpStatusCode.Created, escrito.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(lector, HttpMethod.Post, $"{Ruta}/{id}/comentarios", new { versionNumero = 1, nodoClave = "z", texto = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(lector, HttpMethod.Post, $"{Ruta}/{id}/comentarios", new { versionNumero = 9, texto = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(lector, HttpMethod.Post, $"{Ruta}/{id}/comentarios", new { versionNumero = 1, texto = "  " })).StatusCode);

        var lista = await Json(await lider.GetAsync($"{Ruta}/{id}/comentarios"));
        Assert.Equal(secreto, Assert.Single(lista.EnumerateArray()).GetProperty("texto").GetString());

        await using var bd = entorno.Plataforma();
        var eventos = await bd.Auditoria.AsNoTracking().Where(e => e.RecursoTipo == EndpointsDiagramas.RecursoAuditoria && e.RecursoId == id.ToString()).ToListAsync(Ct);
        Assert.Contains(eventos, e => e.Accion == EndpointsDiagramas.AccionCreado);
        var comentario = Assert.Single(eventos, e => e.Accion == EndpointsDiagramas.AccionComentario);
        Assert.DoesNotContain(secreto, comentario.Datos ?? string.Empty);
    }

    [Fact]
    public async Task Exportar_un_lienzo_a_svg_respeta_la_visibilidad_se_audita_y_avisa_si_no_hay_render()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var lector = await EmpleadoAsync(api);
        var aplicativo = await AplicativoAsync(lider);
        var fuente = JsonSerializer.Serialize(new
        {
            nodos = new[] { new { id = "a", tipo = "sistema", etiqueta = "Trámites", x = 10, y = 20 }, new { id = "b", tipo = "actor", etiqueta = "Radicador", x = 30, y = 40 } },
            relaciones = new[] { new { id = "r", origen = "b", destino = "a", etiqueta = "radica" } },
        });
        var codigo = Codigo();
        var id = Id(await CrearAsync(lider, Solicitud(aplicativo, codigo: codigo, tipo: "lienzo", fuente: fuente)));

        var respuesta = await lector.GetAsync($"{Ruta}/{id}/versiones/1/svg");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("image/svg+xml", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"{codigo}-v1.svg", respuesta.Content.Headers.ContentDisposition?.FileNameStar ?? respuesta.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("sandbox", string.Join(' ', respuesta.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        var modelo = Assert.Single(_render.Recibidos);
        Assert.Equal(["Trámites", "Radicador"], modelo.Nodos.Select(n => n.Etiqueta));
        Assert.Equal("radica", Assert.Single(modelo.Relaciones).Etiqueta);

        await using (var bd = entorno.Plataforma())
        {
            var evento = Assert.Single(await bd.Auditoria.AsNoTracking()
                .Where(e => e.RecursoId == id.ToString() && e.Accion == EndpointsDiagramas.AccionExportado).ToListAsync(Ct));
            Assert.DoesNotContain("Trámites", evento.Datos ?? string.Empty);
        }

        // Lo que no existe y lo que la persona no alcanza responden igual, y no llegan a atlas-render.
        var tecnico = Id(await CrearAsync(lider, Solicitud(aplicativo, tipo: "lienzo", fuente: fuente, visibilidad: "interno_ti")));
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"{Ruta}/{tecnico}/versiones/1/svg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync($"{Ruta}/{id}/versiones/9/svg")).StatusCode);
        Assert.Single(_render.Recibidos);

        var mermaid = Id(await CrearAsync(lider, Solicitud(aplicativo)));
        var noSoportada = await lector.GetAsync($"{Ruta}/{mermaid}/versiones/1/svg");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noSoportada.StatusCode);
        Assert.Equal("exportacion_no_soportada", await CodigoError(noSoportada));

        _render.Fallo = new RenderLienzoException("atlas-render no respondió.", noDisponible: true);
        var sinRender = await lector.GetAsync($"{Ruta}/{id}/versiones/1/svg");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, sinRender.StatusCode);
        Assert.Equal("render_no_disponible", await CodigoError(sinRender));
    }

    [Fact]
    public async Task La_busqueda_global_filtra_por_visibilidad()
    {
        await using var api = Api();
        var lider = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var aplicativo = await AplicativoAsync(lider);
        var marca = $"radicacionbuscada{Guid.NewGuid():N}";
        await CrearAsync(lider, Solicitud(aplicativo, titulo: $"Radicación {marca}", visibilidad: "interno_ti"));

        var visible = Id(await CrearAsync(lider, Solicitud(aplicativo, titulo: $"Radicación visible {marca}")));

        await using var scope = api.Services.CreateAsyncScope();
        var proveedor = scope.ServiceProvider.GetServices<IProveedorBusqueda>().Single(p => p.Proveedor == ModuloDiagramas.IdModulo);
        var lector = PermisosEfectivos.Desde(scope.ServiceProvider.GetRequiredService<CatalogoPermisos>(), [new ConcesionRol(RolesAtlas.Lector, null, AlcanceAsignacion.Global, null, "base")]);
        var acceso = await AccesoDiagramas.CalcularAsync(new System.Security.Claims.ClaimsPrincipal(), new PermisosFijos(lector),
            scope.ServiceProvider.GetRequiredService<Flit.Atlas.Plataforma.Modulos.ILectorAplicativos>(), Ct);
        var contexto = scope.ServiceProvider.GetRequiredService<Flit.Atlas.Modulos.Diagramas.Datos.DiagramasDbContext>();

        var hallados = await BusquedaDiagramas.PorTexto(acceso.Filtrar(contexto.Diagramas.AsNoTracking()), marca).Select(d => d.Id).ToListAsync(Ct);

        Assert.Equal([visible], hallados);
        Assert.Equal(["diagrama"], proveedor.Tipos);
    }
}
