using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Administración de organizaciones e invitaciones (HU 1.5, ATLAS-ADR-0025), bajo <c>/api/v1/plataforma</c>.
/// Organizaciones: <c>atlas.organizaciones.gestionar</c> (y reautenticación para cambiarlas). Invitaciones:
/// <c>atlas.invitaciones.gestionar</c>; un <c>ext.admin_organizacion</c> solo ve y toca las de su organización.
/// </summary>
public static class EndpointsAdministracionExternos
{
    public const string EtiquetaOrganizaciones = "Organizaciones";
    public const string EtiquetaInvitaciones = "Invitaciones";

    [Description("Organización de Atlas: FLIT (interna), un cliente o un proveedor.")]
    public sealed record OrganizacionVista(
        Guid Id, string Nombre,
        [property: Description("interna, cliente o proveedor.")] string Tipo,
        [property: Description("activa o suspendida.")] string Estado,
        [property: Description("Si sus ext.admin_organizacion pueden invitar a colegas.")] bool AdminOrganizacionHabilitado,
        [property: Description("Personas de la organización.")] int Personas,
        [property: Description("Invitaciones pendientes.")] int InvitacionesPendientes,
        DateTimeOffset CreadaEn);

    [Description("Organización nueva o cambios de una existente.")]
    public sealed record SolicitudOrganizacion(
        [property: Description("Nombre único (hasta 200 caracteres).")] string Nombre,
        [property: Description("cliente o proveedor (la interna ya existe).")] string Tipo,
        [property: Description("Habilita el rol ext.admin_organizacion en esta organización.")] bool AdminOrganizacionHabilitado);

    public const string AccionOrganizacionCreada = "plataforma.organizacion_creada";
    public const string AccionOrganizacionActualizada = "plataforma.organizacion_actualizada";
    public const string AccionOrganizacionSuspendida = "plataforma.organizacion_suspendida";
    public const string AccionOrganizacionReactivada = "plataforma.organizacion_reactivada";
    public const string AccionOrganizacionEliminada = "plataforma.organizacion_eliminada";

    internal static void MapAdministracionExternos(this IEndpointRouteBuilder rutas)
    {
        var organizaciones = rutas.MapGroup("/api/v1/plataforma/organizaciones").WithTags(EtiquetaOrganizaciones);

        organizaciones.MapGet("/", ListarOrganizacionesAsync)
            .RequierePermiso(PermisosPlataforma.OrganizacionesGestionar)
            .WithName("ListarOrganizaciones")
            .WithSummary("Organizaciones")
            .WithDescription("FLIT (interna), clientes y proveedores, con sus personas e invitaciones pendientes. Requiere atlas.organizaciones.gestionar.")
            .Produces<List<OrganizacionVista>>();

        organizaciones.MapPost("/", CrearOrganizacionAsync)
            .RequierePermiso(PermisosPlataforma.OrganizacionesGestionar)
            .RequiereReautenticacion()
            .WithName("CrearOrganizacion")
            .WithSummary("Crear una organización")
            .WithDescription("Cliente o proveedor nuevo. Queda en la auditoría. Requiere atlas.organizaciones.gestionar y reautenticación.")
            .Produces<OrganizacionVista>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        organizaciones.MapPut("/{id:guid}", ActualizarOrganizacionAsync)
            .RequierePermiso(PermisosPlataforma.OrganizacionesGestionar)
            .RequiereReautenticacion()
            .WithName("ActualizarOrganizacion")
            .WithSummary("Editar una organización")
            .WithDescription(
                "Nombre, tipo y si habilita ext.admin_organizacion (al deshabilitarlo, ese rol deja de conceder permisos desde la " +
                "siguiente petición). De la interna solo cambia el nombre. Requiere atlas.organizaciones.gestionar y reautenticación.")
            .Produces<OrganizacionVista>()
            .ProducesValidationProblem()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        organizaciones.MapPost("/{id:guid}/suspender", ([Description("Id del recurso.")] Guid id, ClaimsPrincipal usuario, PlataformaDbContext db, IInvalidadorSesiones invalidador, AdministracionRoles roles, CancellationToken ct) =>
                CambiarEstadoAsync(id, EstadoOrganizacion.Suspendida, usuario, db, invalidador, roles, ct))
            .RequierePermiso(PermisosPlataforma.OrganizacionesGestionar)
            .RequiereReautenticacion()
            .WithName("SuspenderOrganizacion")
            .WithSummary("Suspender una organización")
            .WithDescription("Nadie de la organización entra ni conserva permisos, y sus sesiones se cierran de inmediato. La interna no se suspende. Requiere atlas.organizaciones.gestionar y reautenticación.")
            .Produces<OrganizacionVista>()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        organizaciones.MapPost("/{id:guid}/reactivar", ([Description("Id del recurso.")] Guid id, ClaimsPrincipal usuario, PlataformaDbContext db, IInvalidadorSesiones invalidador, AdministracionRoles roles, CancellationToken ct) =>
                CambiarEstadoAsync(id, EstadoOrganizacion.Activa, usuario, db, invalidador, roles, ct))
            .RequierePermiso(PermisosPlataforma.OrganizacionesGestionar)
            .RequiereReautenticacion()
            .WithName("ReactivarOrganizacion")
            .WithSummary("Reactivar una organización")
            .WithDescription("Sus personas vuelven a entrar con un ingreso nuevo. Requiere atlas.organizaciones.gestionar y reautenticación.")
            .Produces<OrganizacionVista>()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound);

        organizaciones.MapDelete("/{id:guid}", EliminarOrganizacionAsync)
            .RequierePermiso(PermisosPlataforma.OrganizacionesGestionar)
            .RequiereReautenticacion()
            .WithName("EliminarOrganizacion")
            .WithSummary("Eliminar una organización vacía")
            .WithDescription("Solo si no tiene personas ni invitaciones (si las tiene, suspéndela). Queda en la auditoría. Requiere atlas.organizaciones.gestionar y reautenticación.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        var invitaciones = rutas.MapGroup("/api/v1/plataforma/invitaciones").WithTags(EtiquetaInvitaciones);

        invitaciones.MapGet("/", async (
                [Description("Solo las de esta organización.")] Guid? organizacionId, ServicioInvitaciones servicio, CancellationToken ct) =>
                await servicio.ListarAsync(organizacionId, ct))
            .RequierePermiso(PermisosPlataforma.InvitacionesGestionar)
            .WithName("ListarInvitaciones")
            .WithSummary("Invitaciones")
            .WithDescription("Las más recientes primero (hasta 500), con su estado calculado ahora. Un administrador de organización solo ve las de la suya. Requiere atlas.invitaciones.gestionar.")
            .Produces<List<InvitacionVista>>();

        invitaciones.MapGet("/opciones", (ClaimsPrincipal usuario, ServicioInvitaciones servicio, CancellationToken ct) =>
                servicio.OpcionesAsync(ActorInvitaciones.De(usuario), ct))
            .RequierePermiso(PermisosPlataforma.InvitacionesGestionar)
            .WithName("ObtenerOpcionesDeInvitacion")
            .WithSummary("Qué se puede elegir al invitar")
            .WithDescription("Organizaciones, roles externos, tipos de alcance, permisos extra y vencimiento máximo para la sesión. Requiere atlas.invitaciones.gestionar.")
            .Produces<OpcionesInvitacion>();

        invitaciones.MapPost("/", async ([Description("Correo, organización, rol, alcance, vencimiento y permisos extra.")] SolicitudInvitacion solicitud, ClaimsPrincipal usuario, ServicioInvitaciones servicio, HttpContext http, CancellationToken ct) =>
            {
                var resultado = await servicio.CrearAsync(solicitud, ActorInvitaciones.De(usuario), http, ct);
                return resultado.Error is { } error
                    ? Respuesta(error)
                    : Results.Created($"/api/v1/plataforma/invitaciones/{resultado.Valor!.Invitacion.Id}", resultado.Valor);
            })
            .RequierePermiso(PermisosPlataforma.InvitacionesGestionar)
            .WithName("CrearInvitacion")
            .WithSummary("Invitar a una persona externa")
            .WithDescription(
                "Crea la invitación (organización, rol externo, alcance, vencimiento y permisos extra) y envía el correo con un enlace " +
                "de un solo uso que vence en 72 h (en la base solo queda su hash). Un proveedor exige vencimiento y alcance. Requiere " +
                "atlas.invitaciones.gestionar; un administrador de organización, solo en la suya y dentro de su alcance (403 si no).")
            .Produces<InvitacionEnviada>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        invitaciones.MapPost("/{id:guid}/reenviar", async ([Description("Id del recurso.")] Guid id, ClaimsPrincipal usuario, ServicioInvitaciones servicio, HttpContext http, CancellationToken ct) =>
                Respuesta(await servicio.ReenviarAsync(id, ActorInvitaciones.De(usuario), http, ct)))
            .RequierePermiso(PermisosPlataforma.InvitacionesGestionar)
            .WithName("ReenviarInvitacion")
            .WithSummary("Reenviar una invitación pendiente")
            .WithDescription("Enlace nuevo de 72 h (el anterior deja de servir) y correo otra vez. Requiere atlas.invitaciones.gestionar.")
            .Produces<InvitacionEnviada>()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        invitaciones.MapPost("/{id:guid}/suspender", async ([Description("Id del recurso.")] Guid id, ClaimsPrincipal usuario, ServicioInvitaciones servicio, CancellationToken ct) =>
                Respuesta(await servicio.SuspenderAsync(id, ActorInvitaciones.De(usuario), ct)))
            .RequierePermiso(PermisosPlataforma.InvitacionesGestionar)
            .WithName("SuspenderInvitacion")
            .WithSummary("Suspender una invitación")
            .WithDescription("Pendiente: el enlace deja de servir. Aceptada: la persona queda suspendida y sus sesiones se cierran. Se deshace renovándola. Requiere atlas.invitaciones.gestionar.")
            .Produces<InvitacionVista>()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        invitaciones.MapPost("/{id:guid}/renovar", async ([Description("Id del recurso.")] Guid id, [Description("Vencimiento nuevo del acceso.")] SolicitudRenovacion solicitud, ClaimsPrincipal usuario, ServicioInvitaciones servicio, HttpContext http, CancellationToken ct) =>
                Respuesta(await servicio.RenovarAsync(id, solicitud, ActorInvitaciones.De(usuario), http, ct)))
            .RequierePermiso(PermisosPlataforma.InvitacionesGestionar)
            .WithName("RenovarInvitacion")
            .WithSummary("Renovar una invitación")
            .WithDescription(
                "Vencimiento nuevo del acceso. Si ya se aceptó, extiende sus asignaciones y reactiva a la persona; si no, vuelve a " +
                "pendiente con un enlace nuevo. Requiere atlas.invitaciones.gestionar.")
            .Produces<InvitacionEnviada>()
            .ProducesValidationProblem()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);

        invitaciones.MapPost("/{id:guid}/revocar", async ([Description("Id del recurso.")] Guid id, ClaimsPrincipal usuario, ServicioInvitaciones servicio, CancellationToken ct) =>
                Respuesta(await servicio.RevocarAsync(id, ActorInvitaciones.De(usuario), ct)))
            .RequierePermiso(PermisosPlataforma.InvitacionesGestionar)
            .WithName("RevocarInvitacion")
            .WithSummary("Revocar una invitación")
            .WithDescription("Definitivo: el enlace deja de servir y, si se había aceptado, se retiran sus roles y se cierran sus sesiones. Requiere atlas.invitaciones.gestionar.")
            .Produces<InvitacionVista>()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound);
    }

    private static IResult Respuesta<T>(Resultado<T> resultado) =>
        resultado.Error is { } error ? Respuesta(error) : Results.Ok(resultado.Valor);

    internal static IResult Respuesta(ErrorAdministracion error) => error.Tipo switch
    {
        TipoError.Invalido => Results.ValidationProblem(
            new Dictionary<string, string[]> { [error.Campo ?? "solicitud"] = [error.Mensaje] },
            extensions: new Dictionary<string, object?> { ["codigo"] = error.Codigo }),
        TipoError.NoEncontrado => Results.Json(new ErrorExternos(error.Codigo, error.Mensaje), statusCode: StatusCodes.Status404NotFound),
        TipoError.Prohibido => Results.Json(new ErrorExternos(error.Codigo, error.Mensaje), statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Json(new ErrorExternos(error.Codigo, error.Mensaje), statusCode: StatusCodes.Status409Conflict),
    };

    private static string TipoTexto(TipoOrganizacion tipo) => tipo switch
    {
        TipoOrganizacion.Interna => "interna",
        TipoOrganizacion.Proveedor => "proveedor",
        _ => "cliente",
    };

    private static IQueryable<OrganizacionVista> Vistas(PlataformaDbContext db, IQueryable<Organizacion> fuente) =>
        fuente.AsNoTracking().Select(o => new OrganizacionVista(
            o.Id, o.Nombre,
            o.Tipo == TipoOrganizacion.Interna ? "interna" : o.Tipo == TipoOrganizacion.Proveedor ? "proveedor" : "cliente",
            o.Estado == EstadoOrganizacion.Suspendida ? "suspendida" : "activa",
            o.AdminOrganizacionHabilitado,
            db.Personas.Count(p => p.OrganizacionId == o.Id),
            db.Invitaciones.Count(i => i.OrganizacionId == o.Id && i.Estado == EstadoInvitacion.Pendiente),
            o.CreadoEn));

    private static async Task<List<OrganizacionVista>> ListarOrganizacionesAsync(PlataformaDbContext db, CancellationToken ct) =>
        await Vistas(db, db.Organizaciones.OrderBy(o => o.Tipo == TipoOrganizacion.Interna ? 0 : 1).ThenBy(o => o.Nombre)).ToListAsync(ct);

    private static (string? Nombre, TipoOrganizacion? Tipo, IResult? Error) Validar(SolicitudOrganizacion solicitud)
    {
        var nombre = (solicitud.Nombre ?? string.Empty).Trim();
        if (nombre.Length is 0 or > 200)
        {
            return (null, null, Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["El nombre es obligatorio (hasta 200 caracteres)."] },
                extensions: new Dictionary<string, object?> { ["codigo"] = "nombre_invalido" }));
        }
        TipoOrganizacion? tipo = solicitud.Tipo switch
        {
            "cliente" => TipoOrganizacion.Cliente,
            "proveedor" => TipoOrganizacion.Proveedor,
            "interna" => TipoOrganizacion.Interna,
            _ => null,
        };
        if (tipo is null)
        {
            return (null, null, Results.ValidationProblem(new Dictionary<string, string[]> { ["tipo"] = ["El tipo es cliente o proveedor."] },
                extensions: new Dictionary<string, object?> { ["codigo"] = "tipo_invalido" }));
        }
        return (nombre, tipo, null);
    }

    private static async Task<IResult> CrearOrganizacionAsync(
        [Description("Nombre, tipo y administrador de la organización.")] SolicitudOrganizacion solicitud, ClaimsPrincipal usuario, PlataformaDbContext db, AdministracionRoles roles, CancellationToken ct)
    {
        var (nombre, tipo, error) = Validar(solicitud);
        if (error is not null)
        {
            return error;
        }
        if (tipo == TipoOrganizacion.Interna)
        {
            return Results.Json(new ErrorExternos("organizacion_interna", "La organización interna (FLIT) ya existe."), statusCode: StatusCodes.Status409Conflict);
        }
        if (await db.Organizaciones.AnyAsync(o => o.Nombre == nombre, ct))
        {
            return Results.Json(new ErrorExternos("nombre_en_uso", "Ya existe una organización con ese nombre."), statusCode: StatusCodes.Status409Conflict);
        }
        var organizacion = new Organizacion { Nombre = nombre!, Tipo = tipo!.Value, AdminOrganizacionHabilitado = solicitud.AdminOrganizacionHabilitado };
        db.Organizaciones.Add(organizacion);
        db.Auditoria.Add(roles.Evento(AccionOrganizacionCreada, "organizacion", organizacion.Id, organizacion.Id, Actor(usuario),
            new { nombre = organizacion.Nombre, tipo = TipoTexto(organizacion.Tipo), admin_organizacion = organizacion.AdminOrganizacionHabilitado }));
        await db.SaveChangesAsync(ct);
        var vista = await Vistas(db, db.Organizaciones.Where(o => o.Id == organizacion.Id)).SingleAsync(ct);
        return Results.Created($"/api/v1/plataforma/organizaciones/{organizacion.Id}", vista);
    }

    private static async Task<IResult> ActualizarOrganizacionAsync(
        [Description("Id de la organización.")] Guid id, [Description("Nombre, tipo y administrador de la organización.")] SolicitudOrganizacion solicitud, ClaimsPrincipal usuario, PlataformaDbContext db,
        AdministracionRoles roles, CancellationToken ct)
    {
        var organizacion = await db.Organizaciones.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (organizacion is null)
        {
            return Results.Json(new ErrorExternos("organizacion_no_encontrada", "La organización no existe."), statusCode: StatusCodes.Status404NotFound);
        }
        var (nombre, tipo, error) = Validar(solicitud);
        if (error is not null)
        {
            return error;
        }
        if ((organizacion.Tipo == TipoOrganizacion.Interna) != (tipo == TipoOrganizacion.Interna))
        {
            return Results.Json(new ErrorExternos("tipo_no_cambia", "La organización interna sigue siendo interna, y ninguna externa pasa a serlo."),
                statusCode: StatusCodes.Status409Conflict);
        }
        if (await db.Organizaciones.AnyAsync(o => o.Nombre == nombre && o.Id != id, ct))
        {
            return Results.Json(new ErrorExternos("nombre_en_uso", "Ya existe una organización con ese nombre."), statusCode: StatusCodes.Status409Conflict);
        }
        var antes = new { nombre = organizacion.Nombre, tipo = TipoTexto(organizacion.Tipo), admin_organizacion = organizacion.AdminOrganizacionHabilitado };
        organizacion.Nombre = nombre!;
        organizacion.Tipo = tipo!.Value;
        organizacion.AdminOrganizacionHabilitado = organizacion.Tipo != TipoOrganizacion.Interna && solicitud.AdminOrganizacionHabilitado;
        db.Auditoria.Add(roles.Evento(AccionOrganizacionActualizada, "organizacion", organizacion.Id, organizacion.Id, Actor(usuario), new
        {
            antes,
            despues = new { nombre = organizacion.Nombre, tipo = TipoTexto(organizacion.Tipo), admin_organizacion = organizacion.AdminOrganizacionHabilitado },
        }));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Vistas(db, db.Organizaciones.Where(o => o.Id == id)).SingleAsync(ct));
    }

    private static async Task<IResult> CambiarEstadoAsync(
        Guid id, EstadoOrganizacion estado, ClaimsPrincipal usuario, PlataformaDbContext db, IInvalidadorSesiones invalidador,
        AdministracionRoles roles, CancellationToken ct)
    {
        var organizacion = await db.Organizaciones.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (organizacion is null)
        {
            return Results.Json(new ErrorExternos("organizacion_no_encontrada", "La organización no existe."), statusCode: StatusCodes.Status404NotFound);
        }
        if (organizacion.Tipo == TipoOrganizacion.Interna && estado == EstadoOrganizacion.Suspendida)
        {
            return Results.Json(new ErrorExternos("organizacion_interna", "La organización interna no se suspende."), statusCode: StatusCodes.Status409Conflict);
        }
        if (organizacion.Estado != estado)
        {
            // Primero el estado y después el sello de cada persona (ATLAS-ADR-0022).
            organizacion.Estado = estado;
            db.Auditoria.Add(roles.Evento(estado == EstadoOrganizacion.Suspendida ? AccionOrganizacionSuspendida : AccionOrganizacionReactivada,
                "organizacion", organizacion.Id, organizacion.Id, Actor(usuario), new { nombre = organizacion.Nombre }));
            await db.SaveChangesAsync(ct);
            if (estado == EstadoOrganizacion.Suspendida)
            {
                foreach (var persona in await db.Personas.Where(p => p.OrganizacionId == id).Select(p => p.Id).ToListAsync(ct))
                {
                    await invalidador.InvalidarSesionesAsync(persona, MotivosInvalidacion.Suspension, Actor(usuario), ct);
                }
            }
        }
        return Results.Ok(await Vistas(db, db.Organizaciones.Where(o => o.Id == id)).SingleAsync(ct));
    }

    private static async Task<IResult> EliminarOrganizacionAsync(
        [Description("Id de la organización.")] Guid id, ClaimsPrincipal usuario, PlataformaDbContext db, AdministracionRoles roles, CancellationToken ct)
    {
        var organizacion = await db.Organizaciones.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (organizacion is null)
        {
            return Results.Json(new ErrorExternos("organizacion_no_encontrada", "La organización no existe."), statusCode: StatusCodes.Status404NotFound);
        }
        if (organizacion.Tipo == TipoOrganizacion.Interna || await db.Personas.AnyAsync(p => p.OrganizacionId == id, ct)
            || await db.Invitaciones.AnyAsync(i => i.OrganizacionId == id, ct) || await db.AsignacionesRol.AnyAsync(a => a.OrganizacionId == id, ct))
        {
            return Results.Json(new ErrorExternos("organizacion_con_datos", "La organización tiene personas o invitaciones: suspéndela en lugar de eliminarla."),
                statusCode: StatusCodes.Status409Conflict);
        }
        db.Organizaciones.Remove(organizacion);
        db.Auditoria.Add(roles.Evento(AccionOrganizacionEliminada, "organizacion", organizacion.Id, organizacion.Id, Actor(usuario),
            new { nombre = organizacion.Nombre, tipo = TipoTexto(organizacion.Tipo) }));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Guid? Actor(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var id) ? id : null;
}
