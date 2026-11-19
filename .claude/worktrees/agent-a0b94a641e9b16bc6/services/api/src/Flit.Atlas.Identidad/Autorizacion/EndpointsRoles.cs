using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Autorizacion;

/// <summary>
/// Roles, asignaciones con alcance y vigencia, mapeos de grupos de Entra y la matriz rol × permiso (HU 1.4,
/// ATLAS-ADR-0023), bajo <c>/api/v1/plataforma</c>. Leer exige <c>atlas.roles.ver</c>; cambiar, <c>atlas.roles.gestionar</c>
/// y un ingreso reciente. Viven en la identidad porque usan la reautenticación y la invalidación de sesiones.
/// </summary>
public static class EndpointsRoles
{
    public const string Etiqueta = "Permisos";

    [Description("Rol de Atlas con los permisos que concede según los manifiestos del código.")]
    public sealed record RolConPermisos(
        [property: Description("Código del rol (atlas.* o ext.*).")] string Codigo,
        string Nombre,
        [property: Description("interno, externo o implicito.")] string Tipo,
        string Descripcion,
        [property: Description("Si se puede asignar a mano (los implícitos y atlas.lector no).")] bool Asignable,
        IReadOnlyList<string> Permisos);

    [Description("Alcance de una asignación: global o un recurso (organizacion, aplicativo, paquete, documento, ambiente…).")]
    public sealed record AlcanceVista(
        [property: Description("Tipo de alcance.")] string Tipo,
        [property: Description("Id del recurso; vacío si es global.")] string? Id);

    [Description("Asignación de un rol a una persona, con su organización, alcance y vigencia.")]
    public sealed record AsignacionVista(
        Guid Id, Guid PersonaId, string Rol, string RolNombre,
        [property: Description("interno o externo.")] string TipoRol,
        Guid? OrganizacionId, AlcanceVista Alcance, DateTimeOffset VigenteDesde, DateTimeOffset? VigenteHasta,
        [property: Description("manual, grupo_entra o invitacion.")] string Origen,
        [property: Description("Si concede permisos en este momento (empezó y no ha vencido).")] bool Vigente,
        Guid? AsignadoPor, DateTimeOffset CreadoEn);

    [Description("Rol que se asigna a una persona. Un rol externo exige vencimiento (máximo 90 días) y queda en la organización de la persona.")]
    public sealed record SolicitudAsignacion(
        [property: Description("Código del rol (atlas.* o ext.*).")] string Rol,
        [property: Description("Organización de la asignación; por defecto, la de la persona.")] Guid? OrganizacionId,
        [property: Description("Alcance; por defecto, global.")] AlcanceVista? Alcance,
        [property: Description("Desde cuándo vale; por defecto, ahora.")] DateTimeOffset? VigenteDesde,
        [property: Description("Hasta cuándo vale; obligatoria para roles externos.")] DateTimeOffset? VigenteHasta);

    [Description("Grupo de Entra ID que otorga un rol interno a sus miembros al entrar.")]
    public sealed record MapeoGrupoVista(Guid Id, Guid GrupoId, string GrupoNombre, string Rol, DateTimeOffset CreadoEn);

    [Description("Grupo de Entra ID (object id y nombre) y el rol interno que otorga.")]
    public sealed record SolicitudMapeoGrupo(Guid GrupoId, string GrupoNombre, string Rol);

    [Description("Error de una operación de administración de roles.")]
    public sealed record ErrorRoles([property: Description("Código estable del error.")] string Error, string Mensaje);

    internal static void MapRoles(this IEndpointRouteBuilder rutas)
    {
        var plataforma = rutas.MapGroup("/api/v1/plataforma").WithTags(Etiqueta);

        plataforma.MapGet("/roles", (CatalogoPermisos catalogo) => catalogo.Roles
                .Select(r => new RolConPermisos(r.Codigo, r.Nombre, TipoTexto(r.Tipo), r.Descripcion, EsAsignable(r), [.. catalogo.PermisosDe(r.Codigo)]))
                .ToList())
            .RequierePermiso(PermisosPlataforma.RolesVer)
            .WithName("ListarRoles")
            .WithSummary("Roles de Atlas")
            .WithDescription("Roles internos, externos e implícitos con los permisos que concede cada uno. Requiere atlas.roles.ver.");

        plataforma.MapGet("/permisos/matriz", (CatalogoPermisos catalogo) => catalogo.Matriz())
            .RequierePermiso(PermisosPlataforma.RolesVer)
            .WithName("ObtenerMatrizDePermisos")
            .WithSummary("Matriz rol × permiso")
            .WithDescription(
                "Matriz generada desde los manifiestos de permisos del código: los roles (en el orden de las columnas) y, por " +
                "módulo, cada permiso con los roles que lo conceden. atlas.superadmin los recibe todos. Es la misma de " +
                "docs/permisos/matriz-rol-permiso.md. Requiere atlas.roles.ver.");

        var asignaciones = plataforma.MapGroup("/personas/{personaId:guid}/asignaciones");
        asignaciones.MapGet("/", async (
                [Description("Persona de Atlas (plataforma_persona).")] Guid personaId,
                [Description("true para incluir las vencidas y las que aún no empiezan.")] bool? todas,
                PlataformaDbContext db, TimeProvider reloj, CancellationToken ct) =>
            {
                if (!await db.Personas.AnyAsync(p => p.Id == personaId, ct))
                {
                    return Results.NotFound();
                }
                var ahora = reloj.GetUtcNow();
                var fuente = db.AsignacionesRol.Where(a => a.PersonaId == personaId
                    && (todas == true || (a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora))));
                var lista = await Consultar(fuente, db, ahora).ToListAsync(ct);
                return Results.Ok(lista.OrderBy(v => v.Rol, StringComparer.Ordinal).ThenBy(v => v.VigenteDesde).ToList());
            })
            .RequierePermiso(PermisosPlataforma.RolesVer)
            .WithName("ListarAsignacionesDePersona")
            .WithSummary("Roles asignados a una persona")
            .WithDescription(
                "Asignaciones manuales, de grupos de Entra y de invitaciones, con alcance y vigencia. Por defecto solo las " +
                "vigentes. No incluye atlas.lector (lo tiene todo empleado) ni los roles implícitos. Requiere atlas.roles.ver.")
            .Produces<List<AsignacionVista>>()
            .Produces(StatusCodes.Status404NotFound);

        asignaciones.MapPost("/", async (
                [Description("Persona de Atlas (plataforma_persona).")] Guid personaId,
                [Description("Rol, organización, alcance y vigencia de la asignación.")] SolicitudAsignacion solicitud, ClaimsPrincipal usuario, AdministracionRoles administracion,
                PlataformaDbContext db, TimeProvider reloj, CancellationToken ct) =>
            {
                var resultado = await administracion.AsignarAsync(personaId, new PedidoAsignacion(
                    solicitud.Rol, solicitud.OrganizacionId, solicitud.Alcance?.Tipo, solicitud.Alcance?.Id,
                    solicitud.VigenteDesde, solicitud.VigenteHasta), PersonaId(usuario), ct: ct);
                if (resultado.Error is { } error)
                {
                    return Respuesta(error);
                }
                var id = resultado.Valor!.Id;
                var vista = await Consultar(db.AsignacionesRol.Where(a => a.Id == id), db, reloj.GetUtcNow()).SingleAsync(ct);
                return Results.Created($"/api/v1/plataforma/personas/{personaId}/asignaciones", vista);
            })
            .RequierePermiso(PermisosPlataforma.RolesGestionar)
            .RequiereReautenticacion()
            .WithName("AsignarRol")
            .WithSummary("Asignar un rol a una persona")
            .WithDescription(
                "Suma un rol con organización, alcance y vigencia a las asignaciones de la persona (las de grupos de Entra no " +
                "cambian). Un rol externo solo va a una persona externa, en su organización y con vencimiento de máximo 90 " +
                "días; un rol interno, a un empleado. Queda en la auditoría. Los permisos cambian desde la siguiente petición " +
                "de la persona. Requiere atlas.roles.gestionar y reautenticación.")
            .Produces<AsignacionVista>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErrorRoles>(StatusCodes.Status404NotFound)
            .Produces<ErrorRoles>(StatusCodes.Status409Conflict);

        plataforma.MapDelete("/asignaciones/{id:guid}", async (
                [Description("Id de la asignación.")] Guid id, ClaimsPrincipal usuario, AdministracionRoles administracion,
                CancellationToken ct) =>
            {
                var resultado = await administracion.RetirarAsync(id, PersonaId(usuario), ct);
                return resultado.Error is { } error ? Respuesta(error) : Results.NoContent();
            })
            .RequierePermiso(PermisosPlataforma.RolesGestionar)
            .RequiereReautenticacion()
            .WithName("RetirarAsignacion")
            .WithSummary("Retirar una asignación de rol")
            .WithDescription(
                "Retira una asignación manual o de invitación, lo deja en la auditoría e invalida las sesiones de la persona. " +
                "Las de grupos de Entra responden 409: se quitan en Entra o eliminando el mapeo. Requiere atlas.roles.gestionar " +
                "y reautenticación.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorRoles>(StatusCodes.Status404NotFound)
            .Produces<ErrorRoles>(StatusCodes.Status409Conflict);

        var mapeos = plataforma.MapGroup("/mapeos-grupos-entra");
        mapeos.MapGet("/", async (PlataformaDbContext db, CancellationToken ct) =>
                await db.MapeosGrupoEntra
                    .Join(db.Roles, m => m.RolId, r => r.Id, (m, r) => new { m, r.Codigo })
                    .OrderBy(x => x.m.GrupoNombre).ThenBy(x => x.Codigo)
                    .Select(x => new MapeoGrupoVista(x.m.Id, x.m.GrupoId, x.m.GrupoNombre, x.Codigo, x.m.CreadoEn))
                    .ToListAsync(ct))
            .RequierePermiso(PermisosPlataforma.RolesVer)
            .WithName("ListarMapeosDeGruposEntra")
            .WithSummary("Grupos de Entra que otorgan roles")
            .WithDescription("Mapeos grupo de Entra → rol interno que se aplican en cada ingreso de un empleado. Requiere atlas.roles.ver.");

        mapeos.MapPost("/", async ([Description("Grupo de Entra y rol interno que otorga.")] SolicitudMapeoGrupo solicitud, ClaimsPrincipal usuario, AdministracionRoles administracion,
                PlataformaDbContext db, CancellationToken ct) =>
            {
                var resultado = await administracion.CrearMapeoAsync(solicitud.GrupoId, solicitud.GrupoNombre, solicitud.Rol, PersonaId(usuario), ct);
                if (resultado.Error is { } error)
                {
                    return Respuesta(error);
                }
                var m = resultado.Valor!;
                return Results.Created($"/api/v1/plataforma/mapeos-grupos-entra/{m.Id}",
                    new MapeoGrupoVista(m.Id, m.GrupoId, m.GrupoNombre, solicitud.Rol, m.CreadoEn));
            })
            .RequierePermiso(PermisosPlataforma.RolesGestionar)
            .RequiereReautenticacion()
            .WithName("CrearMapeoDeGrupoEntra")
            .WithSummary("Mapear un grupo de Entra a un rol")
            .WithDescription(
                "Los miembros del grupo reciben el rol interno en su siguiente ingreso, sumado a sus asignaciones manuales. " +
                "Queda en la auditoría. Requiere atlas.roles.gestionar y reautenticación.")
            .Produces<MapeoGrupoVista>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErrorRoles>(StatusCodes.Status409Conflict);

        mapeos.MapDelete("/{id:guid}", async ([Description("Id del mapeo.")] Guid id, ClaimsPrincipal usuario,
                AdministracionRoles administracion, CancellationToken ct) =>
            {
                var resultado = await administracion.EliminarMapeoAsync(id, PersonaId(usuario), ct);
                return resultado.Error is { } error ? Respuesta(error) : Results.NoContent();
            })
            .RequierePermiso(PermisosPlataforma.RolesGestionar)
            .RequiereReautenticacion()
            .WithName("EliminarMapeoDeGrupoEntra")
            .WithSummary("Eliminar un mapeo de grupo de Entra")
            .WithDescription(
                "Si ningún otro grupo otorga el mismo rol, retira ya ese rol a quienes lo tenían por grupo e invalida sus " +
                "sesiones; si otro grupo lo otorga, se resuelve en el siguiente ingreso. Queda en la auditoría. Requiere " +
                "atlas.roles.gestionar y reautenticación.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorRoles>(StatusCodes.Status404NotFound);
    }

    private static IQueryable<AsignacionVista> Consultar(IQueryable<AsignacionRol> fuente, PlataformaDbContext db, DateTimeOffset ahora) =>
        fuente.AsNoTracking().Join(db.Roles, a => a.RolId, r => r.Id, (a, r) => new AsignacionVista(
            a.Id, a.PersonaId, r.Codigo, r.Nombre, r.Tipo == TipoRol.Externo ? "externo" : "interno", a.OrganizacionId,
            new AlcanceVista(a.AlcanceTipo, a.AlcanceId), a.VigenteDesde, a.VigenteHasta,
            a.Origen == OrigenAsignacion.GrupoEntra ? "grupo_entra" : a.Origen == OrigenAsignacion.Invitacion ? "invitacion" : "manual",
            a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora), a.AsignadoPor, a.CreadoEn));

    private static IResult Respuesta(ErrorAdministracion error) => error.Tipo switch
    {
        TipoError.Invalido => Results.ValidationProblem(
            new Dictionary<string, string[]> { [error.Campo ?? "solicitud"] = [error.Mensaje] },
            extensions: new Dictionary<string, object?> { ["codigo"] = error.Codigo }),
        TipoError.NoEncontrado => Results.NotFound(new ErrorRoles(error.Codigo, error.Mensaje)),
        _ => Results.Conflict(new ErrorRoles(error.Codigo, error.Mensaje)),
    };

    private static string TipoTexto(TipoRol tipo) => tipo switch
    {
        TipoRol.Interno => "interno",
        TipoRol.Externo => "externo",
        _ => "implicito",
    };

    private static bool EsAsignable(DefinicionRol rol) => rol.Tipo != TipoRol.Implicito && rol.Codigo != RolesAtlas.Lector;

    private static Guid? PersonaId(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var id) ? id : null;
}
