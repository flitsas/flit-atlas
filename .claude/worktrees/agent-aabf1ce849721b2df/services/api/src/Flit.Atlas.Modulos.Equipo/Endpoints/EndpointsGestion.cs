using System.ComponentModel;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Equipo.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Equipo.Endpoints;

/// <summary>
/// Traspaso acompañado (checklist de despliegues y guardias antes de habilitar a un secundario), revisión trimestral del
/// reparto y usuarios de GitHub de las personas (HU 2.3 y 2.4, ATLAS-ADR-0031).
/// </summary>
internal static partial class EndpointsGestion
{
    public const string RecursoTraspaso = "equipo.traspaso";
    public const string RecursoRevision = "equipo.revision";
    public const string RecursoGitHub = "equipo.usuario_github";

    /// <summary>Mínimo de cada actividad con el principal antes de habilitar (§8.2).</summary>
    public const int MinimoDespliegues = 1;
    public const int MinimoGuardias = 1;

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/traspasos", ListarTraspasosAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ListarTraspasos")
            .WithSummary("Traspasos en acompañamiento")
            .WithDescription("Los secundarios en acompañamiento con su checklist (al menos un despliegue y una guardia con el principal). Requiere atlas.equipo.ver.")
            .Produces<IReadOnlyList<TraspasoDto>>();

        grupo.MapPost("/responsabilidades/{id:guid}/traspaso/actividades", RegistrarActividadAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("RegistrarActividadTraspaso")
            .WithSummary("Registrar un despliegue o una guardia del traspaso")
            .WithDescription("Anota una actividad del secundario con el principal en el checklist del traspaso. Solo para un secundario en acompañamiento. Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<TraspasoDto>(StatusCodes.Status201Created)
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound)
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/responsabilidades/{id:guid}/traspaso/habilitacion", HabilitarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("HabilitarSecundario")
            .WithSummary("Habilitar al secundario")
            .WithDescription("Pasa al secundario a habilitado si el checklist está completo (409 checklist_incompleto si falta un despliegue o una guardia). Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<TraspasoDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound)
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict);

        grupo.MapGet("/revisiones", ListarRevisionesAsync)
            .RequierePermiso(PermisosEquipo.Ver)
            .WithName("ListarRevisionesReparto")
            .WithSummary("Revisiones trimestrales del reparto")
            .WithDescription("La próxima revisión, las hechas y cuántos días antes sale el recordatorio al líder. Requiere atlas.equipo.ver.")
            .Produces<RevisionesDto>();

        grupo.MapPut("/revisiones/proxima", ProgramarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("ProgramarRevisionReparto")
            .WithSummary("Programar la próxima revisión")
            .WithDescription("Fija el día de la próxima revisión (la crea si no hay); el recordatorio vuelve a salir para la fecha nueva. Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<RevisionDto>()
            .ProducesValidationProblem();

        grupo.MapPost("/revisiones/{id:guid}/cierre", CerrarAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("CerrarRevisionReparto")
            .WithSummary("Cerrar la revisión")
            .WithDescription("Marca la revisión como hecha y programa la siguiente (tres meses después si no se dice). Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<RevisionesDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound)
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/personas/{personaId:guid}/github", GuardarGitHubAsync)
            .RequierePermiso(PermisosEquipo.Gestionar)
            .WithName("GuardarUsuarioGitHub")
            .WithSummary("Usuario de GitHub de una persona")
            .WithDescription("Guarda (o, vacío, quita) el usuario de GitHub de un empleado, para el CODEOWNERS sugerido. Queda en la auditoría. Requiere atlas.equipo.gestionar.")
            .Produces<UsuarioGitHubDto>()
            .Produces<ErrorEquipo>(StatusCodes.Status404NotFound)
            .Produces<ErrorEquipo>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    // --- Traspasos ---

    internal static async Task<TraspasoDto> TraspasoAsync(
        EquipoDbContext equipo, DatosReparto datos, Responsabilidad secundario, Responsabilidad? principal, CancellationToken ct)
    {
        var actividades = await equipo.ActividadesTraspaso.AsNoTracking()
            .Where(a => a.ResponsabilidadId == secundario.Id && a.PersonaId == secundario.PersonaId)
            .OrderByDescending(a => a.Fecha).ThenByDescending(a => a.CreadoEn)
            .ToListAsync(ct);
        return Traspaso(datos, secundario, principal, actividades);
    }

    private static TraspasoDto Traspaso(DatosReparto datos, Responsabilidad secundario, Responsabilidad? principal, IReadOnlyList<ActividadTraspaso> actividades)
    {
        var despliegues = actividades.Count(a => a.Tipo == TipoActividadTraspaso.Despliegue);
        var guardias = actividades.Count(a => a.Tipo == TipoActividadTraspaso.Guardia);
        var aplicativo = datos.Aplicativos.TryGetValue(secundario.AplicativoId, out var a)
            ? AplicativoDto(a)
            : new AplicativoRefDto(secundario.AplicativoId, string.Empty, "Aplicativo eliminado del catálogo", "servicio");
        return new TraspasoDto(secundario.Id, aplicativo, PersonaDto(datos, secundario.PersonaId),
            principal is null ? null : PersonaDto(datos, principal.PersonaId), Textos.De(secundario.EstadoTraspaso), despliegues, guardias,
            despliegues >= MinimoDespliegues && guardias >= MinimoGuardias,
            [.. actividades.Select(x => new ActividadTraspasoDto(x.Id, Textos.De(x.Tipo), x.Fecha, x.Nota, x.CreadoEn))]);
    }

    private static async Task<IResult> ListarTraspasosAsync(
        System.Security.Claims.ClaimsPrincipal usuario, IPermisosDeSesion permisos, EquipoDbContext equipo, PlataformaDbContext plataforma,
        ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, await VeContactoAsync(permisos, usuario, ct), ct);
        var enCurso = datos.Responsabilidades
            .Where(r => r.Tipo == TipoResponsabilidad.Secundario && r.EstadoTraspaso == EstadoTraspaso.EnAcompanamiento)
            .OrderBy(r => r.OrdenAplicativo).ToList();
        var ids = enCurso.Select(r => r.Id).ToList();
        var actividades = await equipo.ActividadesTraspaso.AsNoTracking().Where(a => ids.Contains(a.ResponsabilidadId))
            .OrderByDescending(a => a.Fecha).ThenByDescending(a => a.CreadoEn).ToListAsync(ct);
        return Results.Ok(enCurso.Select(r => Traspaso(datos, r,
            datos.Responsabilidades.FirstOrDefault(p => p.AplicativoId == r.AplicativoId && p.Tipo == TipoResponsabilidad.Principal),
            [.. actividades.Where(a => a.ResponsabilidadId == r.Id && a.PersonaId == r.PersonaId)])).ToList());
    }

    private static async Task<(Responsabilidad? Secundario, IResult? Error)> SecundarioEnAcompanamientoAsync(EquipoDbContext equipo, Guid id, CancellationToken ct)
    {
        var responsabilidad = await equipo.Responsabilidades.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (responsabilidad is null)
        {
            return (null, NoEncontrado("La responsabilidad no existe."));
        }
        if (responsabilidad.Tipo != TipoResponsabilidad.Secundario)
        {
            return (null, Conflicto("no_es_secundario", "El traspaso acompañado es de los secundarios."));
        }
        if (responsabilidad.EstadoTraspaso == EstadoTraspaso.Habilitado)
        {
            return (null, Conflicto("ya_habilitado", "Este secundario ya está habilitado."));
        }
        return (responsabilidad, null);
    }

    private static async Task<IResult> RegistrarActividadAsync(
        [Description("Id de la responsabilidad secundaria.")] Guid id,
        [Description("Tipo, día y nota de la actividad.")] SolicitudActividad solicitud,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var tipo = Textos.Leer<TipoActividadTraspaso>(solicitud.Tipo);
        if (tipo is null)
        {
            return Invalido("tipo", $"La actividad es una de: {Textos.Lista<TipoActividadTraspaso>()}.");
        }
        var nota = Textos.Limpiar(solicitud.Nota);
        if (nota is { Length: > EquipoDbContext.LargoNota })
        {
            return Invalido("nota", $"La nota va hasta {EquipoDbContext.LargoNota} caracteres.");
        }
        var hoy = CalendarioBogota.Hoy(reloj);
        var fecha = solicitud.Fecha ?? hoy;
        if (fecha > hoy)
        {
            return Invalido("fecha", "La actividad ya debe haber ocurrido (hoy o antes).");
        }
        var (secundario, error) = await SecundarioEnAcompanamientoAsync(equipo, id, ct);
        if (error is not null)
        {
            return error;
        }
        var actividad = new ActividadTraspaso
        {
            ResponsabilidadId = secundario!.Id,
            PersonaId = secundario.PersonaId,
            Tipo = tipo.Value,
            Fecha = fecha,
            Nota = nota,
            RegistradaPorPersonaId = PersonaDeSesion(http.User),
        };
        equipo.ActividadesTraspaso.Add(actividad);
        await GuardarAsync(equipo, plataforma,
            [Evento(http, reloj, "traspaso_actividad", RecursoTraspaso, secundario.Id.ToString(), new { actividad = actividad.Id, tipo = Textos.De(tipo.Value), fecha })], ct);
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, false, ct);
        var principal = datos.Responsabilidades.FirstOrDefault(p => p.AplicativoId == secundario.AplicativoId && p.Tipo == TipoResponsabilidad.Principal);
        return Results.Created($"/api/v1/equipo/traspasos", await TraspasoAsync(equipo, datos, secundario, principal, ct));
    }

    private static async Task<IResult> HabilitarAsync(
        [Description("Id de la responsabilidad secundaria.")] Guid id,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, ILectorAplicativos catalogo, TimeProvider reloj, CancellationToken ct)
    {
        var (secundario, error) = await SecundarioEnAcompanamientoAsync(equipo, id, ct);
        if (error is not null)
        {
            return error;
        }
        var datos = await CargarAsync(equipo, plataforma, catalogo, reloj, false, ct);
        var principal = datos.Responsabilidades.FirstOrDefault(p => p.AplicativoId == secundario!.AplicativoId && p.Tipo == TipoResponsabilidad.Principal);
        var antes = await TraspasoAsync(equipo, datos, secundario!, principal, ct);
        if (!antes.Completo)
        {
            return Conflicto("checklist_incompleto",
                $"Antes de habilitarlo, el secundario necesita al menos {MinimoDespliegues} despliegue y {MinimoGuardias} guardia con el principal (lleva {antes.Despliegues} y {antes.Guardias}).");
        }
        secundario!.EstadoTraspaso = EstadoTraspaso.Habilitado;
        await GuardarAsync(equipo, plataforma,
            [Evento(http, reloj, "traspaso_habilitado", RecursoTraspaso, secundario.Id.ToString(), new { despliegues = antes.Despliegues, guardias = antes.Guardias })], ct);
        return Results.Ok(antes with { Estado = Textos.De(EstadoTraspaso.Habilitado) });
    }

    // --- Revisiones ---

    private static RevisionDto RevisionDto(RevisionProgramada r, IReadOnlyDictionary<Guid, string> nombres) =>
        new(r.Id, r.Fecha, Textos.De(r.Estado), r.RecordatorioEnviadoEn, r.HechaEn,
            r.HechaPorPersonaId is { } p ? nombres.GetValueOrDefault(p) ?? "Persona no disponible" : null, r.Nota);

    private static async Task<RevisionesDto> RevisionesAsync(EquipoDbContext equipo, PlataformaDbContext plataforma, OpcionesRevision opciones, CancellationToken ct)
    {
        var revisiones = await equipo.Revisiones.AsNoTracking().OrderByDescending(r => r.Fecha).Take(40).ToListAsync(ct);
        var ids = revisiones.Select(r => r.HechaPorPersonaId).OfType<Guid>().Distinct().ToList();
        var nombres = ids.Count == 0 ? [] : await plataforma.Personas.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
        var proxima = revisiones.FirstOrDefault(r => r.Estado == EstadoRevision.Pendiente);
        return new RevisionesDto(proxima is null ? null : RevisionDto(proxima, nombres),
            [.. revisiones.Where(r => r.Estado == EstadoRevision.Hecha).Select(r => RevisionDto(r, nombres))], opciones.DiasAnticipacion);
    }

    private static async Task<IResult> ListarRevisionesAsync(
        EquipoDbContext equipo, PlataformaDbContext plataforma, IOptions<OpcionesRevision> opciones, CancellationToken ct) =>
        Results.Ok(await RevisionesAsync(equipo, plataforma, opciones.Value, ct));

    private static async Task<IResult> ProgramarAsync(
        [Description("Día de la próxima revisión.")] SolicitudFechaRevision solicitud,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (solicitud.Fecha == default || solicitud.Fecha < CalendarioBogota.Hoy(reloj))
        {
            return Invalido("fecha", "La próxima revisión es hoy o después.");
        }
        var revision = await equipo.Revisiones.FirstOrDefaultAsync(r => r.Estado == EstadoRevision.Pendiente, ct);
        if (revision is null)
        {
            revision = new RevisionProgramada { Fecha = solicitud.Fecha };
            equipo.Revisiones.Add(revision);
        }
        else if (revision.Fecha != solicitud.Fecha)
        {
            revision.Fecha = solicitud.Fecha;
            revision.RecordatorioEnviadoEn = null;
        }
        await GuardarAsync(equipo, plataforma, [Evento(http, reloj, "revision_programada", RecursoRevision, revision.Id.ToString(), new { fecha = solicitud.Fecha })], ct);
        return Results.Ok(RevisionDto(revision, new Dictionary<Guid, string>()));
    }

    private static async Task<IResult> CerrarAsync(
        [Description("Id de la revisión.")] Guid id,
        [Description("Nota y, si se quiere, el día de la siguiente revisión.")] SolicitudCierreRevision solicitud,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, IOptions<OpcionesRevision> opciones, TimeProvider reloj, CancellationToken ct)
    {
        var nota = Textos.Limpiar(solicitud.Nota);
        if (nota is { Length: > EquipoDbContext.LargoNota })
        {
            return Invalido("nota", $"La nota va hasta {EquipoDbContext.LargoNota} caracteres.");
        }
        var revision = await equipo.Revisiones.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (revision is null)
        {
            return NoEncontrado("La revisión no existe.");
        }
        if (revision.Estado != EstadoRevision.Pendiente)
        {
            return Conflicto("ya_cerrada", "Esta revisión ya se cerró.");
        }
        var hoy = CalendarioBogota.Hoy(reloj);
        var siguiente = solicitud.Siguiente ?? (revision.Fecha > hoy ? revision.Fecha : hoy).AddMonths(3);
        if (siguiente <= hoy)
        {
            return Invalido("siguiente", "La siguiente revisión es después de hoy.");
        }
        revision.Estado = EstadoRevision.Hecha;
        revision.HechaEn = reloj.GetUtcNow();
        revision.HechaPorPersonaId = PersonaDeSesion(http.User);
        revision.Nota = nota;
        var nueva = new RevisionProgramada { Fecha = siguiente };
        await GuardarAsync(equipo, plataforma,
        [
            Evento(http, reloj, "revision_cerrada", RecursoRevision, revision.Id.ToString(), new { fecha = revision.Fecha }),
            Evento(http, reloj, "revision_programada", RecursoRevision, nueva.Id.ToString(), new { fecha = siguiente }),
        ], ct, async () =>
        {
            // En la misma transacción: primero se cierra (el índice único admite una sola pendiente) y luego se crea la siguiente.
            await equipo.SaveChangesAsync(ct);
            equipo.Revisiones.Add(nueva);
        });
        return Results.Ok(await RevisionesAsync(equipo, plataforma, opciones.Value, ct));
    }

    // --- GitHub ---

    private static async Task<IResult> GuardarGitHubAsync(
        [Description("Id del empleado.")] Guid personaId,
        [Description("Usuario de GitHub; vacío lo quita.")] SolicitudUsuarioGitHub solicitud,
        HttpContext http, EquipoDbContext equipo, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var usuario = Textos.Limpiar(solicitud.Usuario)?.TrimStart('@');
        if (usuario is not null && (usuario.Length > EquipoDbContext.LargoUsuarioGitHub || !FormatoUsuario().IsMatch(usuario)))
        {
            return Invalido("usuario", "El usuario de GitHub tiene hasta 39 caracteres: letras, números y guiones (sin empezar ni terminar en guion).");
        }
        if (!await plataforma.Personas.AsNoTracking().AnyAsync(p => p.Id == personaId && p.Tipo == TipoPersona.Empleado, ct))
        {
            return NoEncontrado("La persona no existe o no es un empleado.");
        }
        if (usuario is not null && await equipo.UsuariosGitHub.AnyAsync(u => u.PersonaId != personaId && EF.Functions.ILike(u.Usuario, usuario), ct))
        {
            return Conflicto("usuario_en_uso", "Ese usuario de GitHub ya es de otra persona.");
        }
        var actual = await equipo.UsuariosGitHub.FirstOrDefaultAsync(u => u.PersonaId == personaId, ct);
        if (usuario is null && actual is not null)
        {
            equipo.UsuariosGitHub.Remove(actual);
        }
        else if (usuario is not null && actual is null)
        {
            equipo.UsuariosGitHub.Add(new UsuarioGitHub { PersonaId = personaId, Usuario = usuario });
        }
        else if (usuario is not null)
        {
            actual!.Usuario = usuario;
        }
        await GuardarAsync(equipo, plataforma, [Evento(http, reloj, "usuario_github", RecursoGitHub, personaId.ToString(), new { tiene = usuario is not null })], ct);
        return Results.Ok(new UsuarioGitHubDto(personaId, usuario));
    }

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9]))*$")]
    private static partial Regex FormatoUsuario();
}
