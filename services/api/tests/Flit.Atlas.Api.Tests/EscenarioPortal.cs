using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Publicacion;
using Flit.Atlas.Plataforma.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// Piezas de las pruebas del portal (HU 4.3 y 4.4, ATLAS-ADR-0043) con atlas-api completo: aplicativos con su fuente de
/// publicación, contratos publicados desde la CI simulada, guías compartibles aprobadas por el responsable, paquetes
/// publicados, comparticiones, organizaciones y personas externas con el alcance de su invitación. Todo ficticio.
/// </summary>
public sealed class EscenarioPortal : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly EntornoApi _entorno;
    private readonly GitHubActionsSimulado _github = new();
    private readonly ResponsablesDePrueba _responsables = new();

    public EscenarioPortal(
        EntornoApi entorno, IReadOnlyDictionary<string, string>? configuracion = null, TimeProvider? reloj = null, Action<IServiceCollection>? servicios = null)
    {
        _entorno = entorno;
        var claves = new Dictionary<string, string>
        {
            [$"{OpcionesPublicacion.Seccion}:Audiencia"] = GitHubActionsSimulado.Audiencia,
            [$"{OpcionesPublicacion.Seccion}:Propietarios:0"] = GitHubActionsSimulado.Propietario,
            [$"{OpcionesPublicacion.Seccion}:WorkflowsReutilizables:0"] = GitHubActionsSimulado.PatronWorkflowOficial,
        };
        foreach (var (clave, valor) in configuracion ?? new Dictionary<string, string>())
        {
            claves[clave] = valor;
        }
        Api = entorno.Api(configuracion: claves, reloj: reloj, servicios: s =>
        {
            s.AddPlataformaPublicacionDePruebaEnApi(_github);
            s.AddSingleton(_responsables);
            s.Replace(ServiceDescriptor.Scoped<IProveedorRolesImplicitos, ProveedorDePrueba>());
            // HU 4.8: los responsables de un aplicativo que reciben los avisos de deprecación salen del mismo mapa.
            s.Replace(ServiceDescriptor.Scoped<ILectorResponsables, LectorDePrueba>());
            servicios?.Invoke(s);
        });
    }

    public WebApplicationFactory<Program> Api { get; }

    public async ValueTask DisposeAsync()
    {
        _github.Dispose();
        await Api.DisposeAsync();
    }

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

    private sealed class LectorDePrueba(ResponsablesDePrueba responsables) : ILectorResponsables
    {
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DeAplicativosAsync(IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(responsables.PorPersona
                .Where(p => aplicativos.Contains(p.Value))
                .GroupBy(p => p.Value)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(p => p.Key)]));
    }

    public static async Task<JsonElement> Json(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.True(texto.Length > 0, $"{(int)respuesta.StatusCode} sin cuerpo");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    public static async Task<JsonElement> Ok(HttpResponseMessage respuesta, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.True(respuesta.StatusCode == esperado, $"{(int)respuesta.StatusCode}, se esperaba {(int)esperado}: {texto}");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    public static Task<HttpResponseMessage> Enviar(Navegador navegador, HttpMethod metodo, string ruta, object? cuerpo = null) =>
        navegador.EnviarAsync(metodo, ruta, JsonContent.Create(cuerpo ?? new { }));

    /// <summary>Un empleado que entra con el proveedor simulado y, si se da, un rol interno por grupo de Entra.</summary>
    public async Task<(Navegador Navegador, Guid PersonaId)> EmpleadoAsync(string? rol = null, Guid? responsableDe = null)
    {
        var navegador = Navegador.De(Api);
        navegador.Ip = $"2001:db8::43:{RandomNumberGenerator.GetInt32(1, 0xffff):x}";
        var grupos = rol is null ? [] : new[] { await _entorno.GrupoConRolAsync(rol) };
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"portal.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Portal", grupos)).StatusCode);
        var yo = await Json(await navegador.GetAsync("/api/v1/plataforma/yo"));
        var persona = Guid.Parse(yo.GetProperty("personaId").GetString()!);
        if (responsableDe is { } aplicativo)
        {
            _responsables.PorPersona[persona] = aplicativo;
        }
        return (navegador, persona);
    }

    /// <summary>Un aplicativo del catálogo con su fuente de publicación (develop y release).</summary>
    public static async Task<(Guid Id, string Slug)> AplicativoAsync(Navegador lider, string nombre = "API Demo Portal", string? slug = null)
    {
        slug ??= $"api-portal-{Guid.NewGuid().ToString("N")[..10]}";
        var creado = await Ok(await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/aplicativos", new
        {
            slug, nombre, tipo = "producto", grupo = "satelite", estado = "activo", criticidad = "media", manejaDatosPersonales = false,
        }), HttpStatusCode.Created);
        await Ok(await Enviar(lider, HttpMethod.Post, "/api/v1/catalogo/fuentes-publicacion", new
        {
            aplicativo = slug, repositorio = $"{GitHubActionsSimulado.Propietario}/{slug}", ramas = new[] { "develop", "release" }, workflow = (string?)null,
            tipos = new[] { "openapi" }, nota = "Prueba del portal.",
        }), HttpStatusCode.Created);
        return (Guid.Parse(creado.GetProperty("id").GetString()!), slug);
    }

    /// <summary>
    /// Un spec OpenAPI 3.1 compartible con una operación que se comparte (<c>GET /{prefijo}/{id}</c>, esquema
    /// <c>{Esquema}</c>) y otra que no (<c>POST /{prefijo}-pagos-internos</c>, esquema <c>{Esquema}PagoInterno</c>), y un
    /// servidor interno que la poda debe quitar.
    /// </summary>
    public static string Spec(string titulo, string prefijo, string esquema, string version = "1.0.0") => $"""
        openapi: 3.1.0
        info:
          title: {titulo}
          description: Contrato ficticio del portal.
          version: {version}
          x-flit-visibilidad: compartible
        servers:
          - url: https://servidor-interno.ejemplo.com
        paths:
          /{prefijo}/{"{id}"}:
            get:
              operationId: obtener{esquema}
              summary: Obtener {titulo}
              tags: [publico]
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: {"{ type: string }"}
              responses:
                '200':
                  description: El recurso.
                  content:
                    application/json:
                      schema: {"{ $ref: '#/components/schemas/"}{esquema}{"' }"}
          /{prefijo}-pagos-internos:
            post:
              operationId: pagar{esquema}
              summary: Pago interno de {titulo}
              responses:
                '200':
                  description: Pagado.
                  content:
                    application/json:
                      schema: {"{ $ref: '#/components/schemas/"}{esquema}PagoInterno{"' }"}
        components:
          schemas:
            {esquema}:
              type: object
              properties:
                id: {"{ type: string }"}
            {esquema}PagoInterno:
              type: object
              properties:
                cuenta: {"{ type: string }"}
        """;

    /// <summary>Publica una versión estable desde la CI y devuelve (contratoId, versionId).</summary>
    public async Task<(Guid ContratoId, Guid VersionId)> PublicarAsync(string slug, string contrato, string spec, string version = "1.0.0", string? changelog = null)
    {
        using var cliente = Api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contratos/publicaciones")
        {
            Content = JsonContent.Create(new { aplicativo = slug, contrato, tipo = "openapi", version, spec, changelog, changelogBase = changelog is null ? null : "0.9.0" }),
        };
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            _github.Token(DateTimeOffset.UtcNow.AddSeconds(-5), GitHubActionsSimulado.Claims(repositorio: $"{GitHubActionsSimulado.Propietario}/{slug}", rama: "release")));
        var publicada = await cliente.SendAsync(peticion, Ct);
        var cuerpo = await Ok(publicada, HttpStatusCode.Created);
        return (Guid.Parse(cuerpo.GetProperty("contratoId").GetString()!), Guid.Parse(cuerpo.GetProperty("versionId").GetString()!));
    }

    /// <summary>Una guía compartible del contrato, creada por el líder y aprobada por el responsable; devuelve su id.</summary>
    public static async Task<Guid> GuiaCompartibleAsync(Navegador lider, Navegador responsable, Guid contratoId, string titulo, string contenido)
    {
        var creada = await Ok(await Enviar(lider, HttpMethod.Post, $"/api/v1/contratos/{contratoId}/guias", new
        {
            titulo, contenido, operacion = (string?)null, aplicaDesde = (string?)null, aplicaHasta = (string?)null,
            visibilidad = "compartible", nota = "Primera versión.", revisionBase = (int?)null,
        }), HttpStatusCode.Created);
        var id = Guid.Parse(creada.GetProperty("guia").GetProperty("id").GetString()!);
        await Ok(await Enviar(responsable, HttpMethod.Post, $"/api/v1/contratos/guias/{id}/publicacion", new { revision = 1 }));
        return id;
    }

    /// <summary>Un paquete con un contrato, sus operaciones y sus guías, publicado (revisión 1); devuelve su id.</summary>
    public static async Task<Guid> PaquetePublicadoAsync(Navegador lider, string nombre, Guid contrato, Guid version, string[] operaciones, params Guid[] guias)
    {
        var creado = await Ok(await Enviar(lider, HttpMethod.Post, "/api/v1/contratos/paquetes", new
        {
            nombre, descripcion = $"Descripción de {nombre}.", nota = "Primera versión.", revisionBase = (int?)null,
            definicion = new[] { new { contratoId = contrato, versionId = version, operaciones, guias = guias.Select(g => new { guiaId = g, revision = (int?)null }).ToArray() } },
        }), HttpStatusCode.Created);
        var id = Guid.Parse(creado.GetProperty("paquete").GetProperty("id").GetString()!);
        await Ok(await Enviar(lider, HttpMethod.Post, $"/api/v1/contratos/paquetes/{id}/publicacion", new { revision = 1 }));
        return id;
    }

    /// <summary>Comparte un paquete con una organización y devuelve el id de la compartición.</summary>
    public static async Task<Guid> CompartirAsync(Navegador lider, Guid paquete, Guid organizacion, bool exportar = true, bool sandbox = false,
        DateTimeOffset? hasta = null, int? revisionFijada = null)
    {
        var creada = await Ok(await Enviar(lider, HttpMethod.Post, $"/api/v1/contratos/paquetes/{paquete}/comparticiones", new
        {
            organizacionId = organizacion, revisionFijada, puedeExportar = exportar, puedeSandbox = sandbox, vigenteDesde = (DateTimeOffset?)null, vigenteHasta = hasta,
        }), HttpStatusCode.Created);
        return Guid.Parse(creada.GetProperty("id").GetString()!);
    }

    /// <summary>Una organización cliente (o proveedora) nueva.</summary>
    public async Task<Guid> OrganizacionAsync(string nombre, TipoOrganizacion tipo = TipoOrganizacion.Cliente)
    {
        await using var plataforma = _entorno.Plataforma();
        var organizacion = new Organizacion { Nombre = $"{nombre} {Guid.NewGuid().ToString("N")[..6]}", Tipo = tipo, AdminOrganizacionHabilitado = true };
        plataforma.Organizaciones.Add(organizacion);
        await plataforma.SaveChangesAsync(Ct);
        return organizacion.Id;
    }

    /// <summary>
    /// Una persona externa de la organización con un rol y una asignación por recurso del alcance de su invitación (vacío =
    /// toda la organización), como la crea la aceptación de una invitación (ATLAS-ADR-0025); con <paramref name="extras"/>,
    /// también su invitación aceptada con esos permisos extra. Entra con el ingreso de prueba.
    /// </summary>
    public async Task<PersonaExterna> ExternoAsync(Guid organizacion, string rol, (string Tipo, Guid Id)[]? alcance = null, string[]? extras = null)
    {
        await using var plataforma = _entorno.Plataforma();
        var persona = new Persona
        {
            Tipo = TipoPersona.Externo, OrganizacionId = organizacion, Nombre = "Externa Demo Portal", Correo = $"externa.portal.{Guid.NewGuid():N}@ejemplo.com",
        };
        var rolId = await plataforma.Roles.Where(r => r.Codigo == rol).Select(r => r.Id).SingleAsync(Ct);
        var asignaciones = ((alcance ?? []).Length == 0 ? [(TiposAlcance.Global, (Guid?)null)] : alcance!.Select(a => (a.Tipo, (Guid?)a.Id)))
            .Select(a => new AsignacionRol
            {
                PersonaId = persona.Id, RolId = rolId, OrganizacionId = organizacion, AlcanceTipo = a.Item1, AlcanceId = a.Item2?.ToString("D"),
                VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-1), VigenteHasta = DateTimeOffset.UtcNow.AddDays(80), Origen = OrigenAsignacion.Invitacion,
            })
            .ToList();
        plataforma.Personas.Add(persona);
        plataforma.AsignacionesRol.AddRange(asignaciones);
        if (extras is { Length: > 0 })
        {
            plataforma.Invitaciones.Add(new Invitacion
            {
                OrganizacionId = organizacion, Correo = persona.Correo, RolId = rolId, Alcance = "[]", PermisosExtra = JsonSerializer.Serialize(extras),
                AccesoVenceEn = DateTimeOffset.UtcNow.AddDays(80), TokenHash = RandomNumberGenerator.GetBytes(32), TokenVenceEn = DateTimeOffset.UtcNow.AddHours(-1),
                Estado = EstadoInvitacion.Aceptada, InvitadoPor = persona.Id, PersonaId = persona.Id, AceptadaEn = DateTimeOffset.UtcNow,
            });
        }
        await plataforma.SaveChangesAsync(Ct);

        await using var alcanceServicios = Api.Services.CreateAsyncScope();
        var usuarios = alcanceServicios.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
        var usuario = new UsuarioAtlas { PersonaId = persona.Id, UserName = persona.Correo, Email = persona.Correo, CreadoEn = DateTimeOffset.UtcNow };
        Assert.True((await usuarios.CreateAsync(usuario)).Succeeded);
        var navegador = Navegador.De(Api);
        navegador.Ip = $"2001:db8::44:{RandomNumberGenerator.GetInt32(1, 0xffff):x}";
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(usuario.Id)).StatusCode);
        return new PersonaExterna(navegador, persona.Id, organizacion, [.. asignaciones.Select(a => a.Id)]);
    }
}

public sealed record PersonaExterna(Navegador Navegador, Guid PersonaId, Guid OrganizacionId, IReadOnlyList<Guid> Asignaciones);
