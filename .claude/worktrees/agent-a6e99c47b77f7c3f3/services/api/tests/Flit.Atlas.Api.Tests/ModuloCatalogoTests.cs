using System.Net;
using System.Net.Http.Json;
using Flit.Atlas.Modulos.Catalogo;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 2.1 (ATLAS-ADR-0028): el catálogo montado por el host, encendido por defecto y con la semilla que corre el
/// migrador (aquí, <see cref="EntornoApi"/>). Lectura para todo empleado; crear y eliminar con gestionar; editar con editar
/// sobre el aplicativo o un ancestro; cada cambio en la auditoría. La prueba parametrizada de <c>AutorizacionTests</c>
/// recorre además estos endpoints con todos los roles.
/// </summary>
public class ModuloCatalogoTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Referencia(Guid Id, string Slug, string Nombre);

    private sealed record Resumen(Guid Id, string Slug, string Nombre, string Tipo, string Grupo, string Estado, string Criticidad,
        bool ManejaDatosPersonales, Guid? PadreId, string? Descripcion, List<string> Tecnologias, List<string> Etiquetas,
        List<Referencia> Ancestros, int Hijos);

    private sealed record Listado(int Total, List<Resumen> Elementos);

    private sealed record PermisosFicha(bool Editar, bool Gestionar);

    private sealed record Ficha(Guid Id, string Slug, string Nombre, string Tipo, string Grupo, string Estado, string Criticidad,
        bool ManejaDatosPersonales, Guid? PadreId, string? Descripcion, string? Repo, string? RutaEnRepo, List<string> Tecnologias,
        List<string> Ambientes, List<string> Etiquetas, List<Referencia> Ancestros, List<Resumen> Hijos, PermisosFicha Permisos);

    private sealed record Error(string Codigo, string Mensaje);

    private static object Solicitud(string slug, string nombre, string tipo, Guid? padreId = null, string? grupo = null,
        string estado = "activo", string criticidad = "media", string? descripcion = null, string[]? etiquetas = null) => new
    {
        slug, nombre, tipo, padreId, grupo, descripcion, estado, criticidad, manejaDatosPersonales = false,
        repo = "flitsas/ejemplo", rutaEnRepo = "services/demo", tecnologias = new[] { ".NET 10" }, ambientes = new[] { "pdn", "qa", "qa" },
        etiquetas = etiquetas ?? ["Demo"],
    };

    private async Task<(Navegador Navegador, string Correo)> EmpleadoAsync(WebApplicationFactory<Program> api, string? rol = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::28:{Random.Shared.Next(1, 0xffff):x}";
        var correo = $"catalogo.{Guid.NewGuid():N}@ejemplo.com";
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, "Persona Demo", grupos)).StatusCode);
        return (navegador, correo);
    }

    private static string Unico(string prefijo) => $"{prefijo}-{Guid.NewGuid().ToString("N")[..12]}";

    [Fact]
    public async Task Un_lector_ve_la_semilla_filtra_por_grupo_estado_criticidad_y_texto_y_abre_la_ficha()
    {
        await using var api = entorno.Api();
        var (lector, _) = await EmpleadoAsync(api);

        var banderas = await (await lector.GetAsync("/api/v1/plataforma/banderas")).Content.ReadFromJsonAsync<Dictionary<string, bool>>(Ct);
        var todo = await (await lector.GetAsync("/api/v1/catalogo/aplicativos")).Content.ReadFromJsonAsync<Listado>(Ct);
        var legados = await (await lector.GetAsync("/api/v1/catalogo/aplicativos?grupo=legado&tipo=servicio")).Content.ReadFromJsonAsync<Listado>(Ct);
        var planeadosFlit2 = await (await lector.GetAsync("/api/v1/catalogo/aplicativos?grupo=flit2&estado=planeado&criticidad=media")).Content.ReadFromJsonAsync<Listado>(Ct);
        var porTexto = await (await lector.GetAsync("/api/v1/catalogo/aplicativos?texto=tramites")).Content.ReadFromJsonAsync<Listado>(Ct);
        var invalido = await lector.GetAsync("/api/v1/catalogo/aplicativos?estado=borrado");

        Assert.True(banderas!["modulo.catalogo"]);
        Assert.True(todo!.Total >= 90);
        Assert.Equal(28, legados!.Elementos.Count(e => e.Slug.StartsWith("legado-", StringComparison.Ordinal)));
        Assert.All(planeadosFlit2!.Elementos, e => Assert.Equal(("flit2", "planeado"), (e.Grupo, e.Estado)));
        Assert.Contains(planeadosFlit2.Elementos, e => e.Slug == "flit2-core-diagnostico");
        var tramites = Assert.Single(porTexto!.Elementos, e => e.Slug == "flit2-tramites");
        Assert.Equal(["flit2"], tramites.Ancestros.Select(a => a.Slug));
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        // Ordenado por grupo y por la ruta: el producto antes que sus módulos.
        var flit2 = todo.Elementos.FindIndex(e => e.Slug == "flit2");
        Assert.True(flit2 < todo.Elementos.FindIndex(e => e.Slug == "flit2-comparendos"));

        var ficha = await (await lector.GetAsync("/api/v1/catalogo/aplicativos/flit2-identidad")).Content.ReadFromJsonAsync<Ficha>(Ct);
        Assert.Equal("modulo", ficha!.Tipo);
        Assert.Contains(ficha.Hijos, h => h.Slug == "flit2-core-identity");
        Assert.Equal(new PermisosFicha(false, false), ficha.Permisos);
        var sentinel = await (await lector.GetAsync("/api/v1/catalogo/aplicativos/sentinel")).Content.ReadFromJsonAsync<Ficha>(Ct);
        Assert.Contains("WhatsApp", sentinel!.Descripcion, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await lector.GetAsync("/api/v1/catalogo/aplicativos/no-existe")).StatusCode);
    }

    [Fact]
    public async Task Un_lector_no_crea_ni_edita_ni_elimina()
    {
        await using var api = entorno.Api();
        var (lector, _) = await EmpleadoAsync(api);
        var flito = await (await lector.GetAsync("/api/v1/catalogo/aplicativos/flito")).Content.ReadFromJsonAsync<Ficha>(Ct);

        Assert.Equal(HttpStatusCode.Forbidden, (await lector.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(Unico("demo"), "Demo", "producto", grupo: "satelite")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.EnviarAsync(HttpMethod.Put, $"/api/v1/catalogo/aplicativos/{flito!.Id}",
            JsonContent.Create(Solicitud("flito", "FLITO", "producto", grupo: "flito")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.EnviarAsync(HttpMethod.Delete, $"/api/v1/catalogo/aplicativos/{flito.Id}")).StatusCode);
    }

    [Fact]
    public async Task El_lider_tecnico_crea_edita_y_elimina_y_cada_cambio_queda_en_la_auditoria()
    {
        await using var api = entorno.Api();
        var (lider, correo) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var slug = Unico("producto-demo");

        var creado = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(slug, "  Producto Demo  ", "producto", grupo: "satelite", etiquetas: ["Demo", "demo", " "])));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var producto = (await creado.Content.ReadFromJsonAsync<Ficha>(Ct))!;
        Assert.Equal("Producto Demo", producto.Nombre);
        Assert.Equal(["demo"], producto.Etiquetas);
        Assert.Equal(["qa", "pdn"], producto.Ambientes);
        Assert.Equal(new PermisosFicha(true, true), producto.Permisos);
        Assert.Equal($"/api/v1/catalogo/aplicativos/{slug}", creado.Headers.Location?.OriginalString);

        var repetido = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(slug, "Otro", "producto", grupo: "satelite")));
        var sinGrupo = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos", JsonContent.Create(Solicitud(Unico("x"), "X", "producto")));
        var moduloSinPadre = await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos", JsonContent.Create(Solicitud(Unico("x"), "X", "modulo")));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("slug_en_uso", (await repetido.Content.ReadFromJsonAsync<Error>(Ct))!.Codigo);
        Assert.Equal(HttpStatusCode.BadRequest, sinGrupo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, moduloSinPadre.StatusCode);

        // Un módulo hereda el grupo del producto aunque pida otro; un servicio no puede tener hijos.
        var modulo = (await (await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(Unico("modulo-demo"), "Módulo Demo", "modulo", producto.Id, grupo: "legado")))).Content.ReadFromJsonAsync<Ficha>(Ct))!;
        Assert.Equal("satelite", modulo.Grupo);
        Assert.Equal([producto.Slug], modulo.Ancestros.Select(a => a.Slug));
        var servicio = (await (await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(Unico("servicio-demo"), "Servicio Demo", "servicio", modulo.Id)))).Content.ReadFromJsonAsync<Ficha>(Ct))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(Unico("nieto"), "Nieto", "servicio", servicio.Id)))).StatusCode);

        // Cambiar el grupo del producto se propaga a sus descendientes.
        var editado = await lider.EnviarAsync(HttpMethod.Put, $"/api/v1/catalogo/aplicativos/{producto.Id}",
            JsonContent.Create(Solicitud(slug, "Producto Demo", "producto", grupo: "infraestructura", estado: "en_retiro", criticidad: "alta")));
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        var fichaServicio = await (await lider.GetAsync($"/api/v1/catalogo/aplicativos/{servicio.Slug}")).Content.ReadFromJsonAsync<Ficha>(Ct);
        Assert.Equal("infraestructura", fichaServicio!.Grupo);
        Assert.Equal("en_retiro", (await editado.Content.ReadFromJsonAsync<Ficha>(Ct))!.Estado);

        // Con hijos no se elimina; sin hijos, sí.
        var conHijos = await lider.EnviarAsync(HttpMethod.Delete, $"/api/v1/catalogo/aplicativos/{producto.Id}");
        Assert.Equal(HttpStatusCode.Conflict, conHijos.StatusCode);
        Assert.Equal("tiene_hijos", (await conHijos.Content.ReadFromJsonAsync<Error>(Ct))!.Codigo);
        Assert.Equal(HttpStatusCode.NoContent, (await lider.EnviarAsync(HttpMethod.Delete, $"/api/v1/catalogo/aplicativos/{servicio.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await lider.GetAsync($"/api/v1/catalogo/aplicativos/{servicio.Slug}")).StatusCode);

        await using var plataforma = entorno.Plataforma();
        var persona = await plataforma.Personas.SingleAsync(p => p.Correo == correo, Ct);
        var eventos = await plataforma.Auditoria.Where(a => a.ActorId == persona.Id.ToString() && a.RecursoTipo == "catalogo.aplicativo")
            .OrderBy(a => a.OcurridoEn).Select(a => new { a.Accion, a.RecursoId, a.Datos }).ToListAsync(Ct);
        Assert.Equal(["catalogo.aplicativo_creado", "catalogo.aplicativo_creado", "catalogo.aplicativo_creado", "catalogo.aplicativo_actualizado", "catalogo.aplicativo_eliminado"],
            eventos.Select(e => e.Accion));
        Assert.Contains("\"grupo\"", eventos[3].Datos, StringComparison.Ordinal);
        Assert.Equal(servicio.Id.ToString(), eventos[4].RecursoId);
    }

    [Fact]
    public async Task Con_un_rol_limitado_a_un_producto_edita_lo_que_cuelga_de_el_pero_no_cambia_la_jerarquia_fuera()
    {
        await using var api = entorno.Api();
        var (lider, _) = await EmpleadoAsync(api, RolesAtlas.LiderTecnico);
        var producto = (await (await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(Unico("producto-acotado"), "Producto Acotado", "producto", grupo: "satelite")))).Content.ReadFromJsonAsync<Ficha>(Ct))!;
        var servicio = (await (await lider.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(Unico("servicio-acotado"), "Servicio Acotado", "servicio", producto.Id)))).Content.ReadFromJsonAsync<Ficha>(Ct))!;
        var flito = await (await lider.GetAsync("/api/v1/catalogo/aplicativos/flito")).Content.ReadFromJsonAsync<Ficha>(Ct);

        // Un empleado con el líder técnico solo sobre el producto (alcance aplicativo:<id>): lo mismo que dará el
        // responsable implícito de la HU 2.3.
        var (acotado, correo) = await EmpleadoAsync(api);
        await using (var plataforma = entorno.Plataforma())
        {
            var persona = await plataforma.Personas.SingleAsync(p => p.Correo == correo, Ct);
            var rolId = await plataforma.Roles.Where(r => r.Codigo == RolesAtlas.LiderTecnico).Select(r => r.Id).SingleAsync(Ct);
            plataforma.AsignacionesRol.Add(new AsignacionRol
            {
                PersonaId = persona.Id, RolId = rolId, OrganizacionId = persona.OrganizacionId, VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-1),
                AlcanceTipo = TiposAlcance.Aplicativo, AlcanceId = producto.Id.ToString(),
            });
            await plataforma.SaveChangesAsync(Ct);
        }

        var fichaServicio = await (await acotado.GetAsync($"/api/v1/catalogo/aplicativos/{servicio.Slug}")).Content.ReadFromJsonAsync<Ficha>(Ct);
        var fichaFlito = await (await acotado.GetAsync("/api/v1/catalogo/aplicativos/flito")).Content.ReadFromJsonAsync<Ficha>(Ct);
        Assert.Equal(new PermisosFicha(true, true), fichaServicio!.Permisos);
        Assert.Equal(new PermisosFicha(false, false), fichaFlito!.Permisos);

        var editarServicio = await acotado.EnviarAsync(HttpMethod.Put, $"/api/v1/catalogo/aplicativos/{servicio.Id}",
            JsonContent.Create(Solicitud(servicio.Slug, "Servicio Acotado", "servicio", producto.Id, descripcion: "Editado por su responsable.")));
        var editarFlito = await acotado.EnviarAsync(HttpMethod.Put, $"/api/v1/catalogo/aplicativos/{flito!.Id}",
            JsonContent.Create(Solicitud("flito", "FLITO", "producto", grupo: "flito")));
        var moverAFlito = await acotado.EnviarAsync(HttpMethod.Put, $"/api/v1/catalogo/aplicativos/{servicio.Id}",
            JsonContent.Create(Solicitud(servicio.Slug, "Servicio Acotado", "servicio", flito.Id)));
        var crearProducto = await acotado.EnviarAsync(HttpMethod.Post, "/api/v1/catalogo/aplicativos",
            JsonContent.Create(Solicitud(Unico("otro"), "Otro", "producto", grupo: "satelite")));

        Assert.Equal(HttpStatusCode.OK, editarServicio.StatusCode);
        Assert.Equal("Editado por su responsable.", (await editarServicio.Content.ReadFromJsonAsync<Ficha>(Ct))!.Descripcion);
        Assert.Equal(HttpStatusCode.Forbidden, editarFlito.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, moverAFlito.StatusCode);
        Assert.Equal("requiere_permiso", (await moverAFlito.Content.ReadFromJsonAsync<Error>(Ct))!.Codigo);
        Assert.Equal(HttpStatusCode.Forbidden, crearProducto.StatusCode);
    }

    [Fact]
    public async Task Un_externo_no_ve_el_catalogo()
    {
        await using var api = entorno.Api();
        var externo = await entorno.ExternoAsync(api, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddHours(1));
        using var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::28:{Random.Shared.Next(1, 0xffff):x}";
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(externo.UsuarioId)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await navegador.GetAsync("/api/v1/catalogo/aplicativos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await navegador.GetAsync("/api/v1/catalogo/aplicativos/flito")).StatusCode);
    }

    [Fact]
    public async Task El_proveedor_de_busqueda_global_respeta_el_permiso()
    {
        await using var api = entorno.Api();
        var (lector, correo) = await EmpleadoAsync(api);
        await using var alcance = api.Services.CreateAsyncScope();
        var proveedor = alcance.ServiceProvider.GetServices<IProveedorBusqueda>().Single(p => p.Proveedor == ModuloCatalogo.IdModulo);
        Guid personaId;
        await using (var plataforma = entorno.Plataforma())
        {
            personaId = await plataforma.Personas.Where(p => p.Correo == correo).Select(p => p.Id).SingleAsync(Ct);
        }
        var usuario = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(Plataforma.EndpointsPlataforma.ClaimPersona, personaId.ToString())], "prueba"));

        var resultados = await proveedor.BuscarAsync(new ConsultaBusqueda("comparendos", usuario), Ct);
        var anonimo = await proveedor.BuscarAsync(new ConsultaBusqueda("comparendos", new System.Security.Claims.ClaimsPrincipal()), Ct);

        Assert.Contains(resultados, r => r.Ruta == "/catalogo/flit2-comparendos" && r.Tipo == "modulo");
        Assert.Empty(anonimo);
        lector.Dispose();
    }
}
