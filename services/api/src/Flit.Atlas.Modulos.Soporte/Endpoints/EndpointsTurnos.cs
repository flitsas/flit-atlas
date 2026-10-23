using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

/// <summary>
/// Calendario y turnos: consulta por rango y por equipo (vistas de lista, semana y mes), quién atiende este sábado en
/// cada equipo, turnos a mano, horario por turno, estados y reemplazos.
/// </summary>
internal static class EndpointsTurnos
{
    /// <summary>Rango máximo de una consulta del calendario.</summary>
    public const int DiasMaximosConsulta = 400;

    /// <summary>Permiso que deja ver el teléfono de las personas (§8.2).</summary>
    public const string PermisoContacto = "atlas.equipo.ver_contacto";

    private const string SinPermisoEquipo = "No tienes atlas.soporte.gestionar sobre el equipo de este turno.";

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/turnos", ListarAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ListarTurnosSoporte")
            .WithSummary("Calendario de turnos")
            .WithDescription(
                $"Turnos y festivos entre dos fechas (incluidas, máximo {DiasMaximosConsulta} días), de un equipo o de todos, " +
                "para las vistas de lista, semana y mes. Sin fechas: desde hoy y tres meses hacia adelante. Incluye los " +
                "reemplazados y cancelados como historia. El teléfono solo sale con atlas.equipo.ver_contacto. Requiere atlas.soporte.ver.")
            .Produces<CalendarioDto>()
            .ProducesValidationProblem();

        grupo.MapGet("/este-sabado", EsteSabadoAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ObtenerSoporteEsteSabado")
            .WithSummary("Soporte de este sábado")
            .WithDescription(
                "A quién llamar el próximo sábado (hoy, si es sábado, en la hora de Bogotá) en cada equipo activo: lo que " +
                "muestra el widget de Inicio. Requiere atlas.soporte.ver.")
            .Produces<EsteSabadoDto>();

        grupo.MapPost("/turnos", CrearAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CrearTurnoSoporte")
            .WithSummary("Crear un turno a mano")
            .WithDescription(
                "Turno manual (estado programado) de una persona del equipo en un sábado. Sin horario, toma el del equipo o el " +
                "global. 409 si la persona ya tiene turno ese sábado en el equipo o si el equipo ya está completo. Requiere " +
                "atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<TurnoDto>(StatusCodes.Status201Created)
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/turnos/{id:guid}", CambiarAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CambiarTurnoSoporte")
            .WithSummary("Cambiar el horario de un turno")
            .WithDescription(
                "Horario propio y observación de un turno programado o confirmado. Un turno aplicado, reemplazado o cancelado ya " +
                "no cambia (409). Requiere atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<TurnoDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/turnos/{id:guid}/estado", CambiarEstadoAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CambiarEstadoTurnoSoporte")
            .WithSummary("Cambiar el estado de un turno")
            .WithDescription(
                "programado → confirmado → aplicado, o cancelado desde programado o confirmado. Un turno solo se aplica el día " +
                "del turno o después. Los estados aplicado, reemplazado y cancelado son finales. Queda en la auditoría. " +
                "Requiere atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<TurnoDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/turnos/{id:guid}/reemplazo", ReemplazarAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("ReemplazarTurnoSoporte")
            .WithSummary("Reemplazar a la persona de un turno")
            .WithDescription(
                "El turno queda reemplazado y se crea uno nuevo (manual, programado, con el mismo horario) para la persona del " +
                "mismo equipo que lo toma, enlazado al original, sin pasar por el flujo de solicitud, aceptación y aprobación " +
                "de /solicitudes-cambio. Requiere atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<TurnoDto>(StatusCodes.Status201Created)
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    internal static async Task<bool> VeContactoAsync(ClaimsPrincipal usuario, IPermisosDeSesion permisos, CancellationToken ct) =>
        (await permisos.ObtenerAsync(usuario, ct)).Tiene(PermisoContacto);

    private static async Task<IResult> ListarAsync(
        [Description("Primer día (aaaa-mm-dd); vacío, hoy.")] DateOnly? desde,
        [Description("Último día, incluido (aaaa-mm-dd); vacío, tres meses después del primero.")] DateOnly? hasta,
        [Description("Id del equipo; vacío, todos.")] Guid? equipo,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma,
        TimeProvider reloj, CancellationToken ct)
    {
        var inicio = desde ?? CalendarioBogota.Hoy(reloj);
        var fin = hasta ?? inicio.AddMonths(3);
        if (fin < inicio)
        {
            return Invalido("hasta", "La fecha final no puede ser anterior a la inicial.");
        }
        if (fin.DayNumber - inicio.DayNumber > DiasMaximosConsulta)
        {
            return Invalido("hasta", $"Consulta máximo {DiasMaximosConsulta} días.");
        }
        var consulta = soporte.Turnos.AsNoTracking().Where(t => t.Fecha >= inicio && t.Fecha <= fin);
        if (equipo is { } e)
        {
            consulta = consulta.Where(t => t.EquipoId == e);
        }
        var turnos = await consulta.OrderBy(t => t.Fecha).ThenBy(t => t.EquipoId).ThenBy(t => t.CreadoEn).ToListAsync(ct);
        var festivos = await FestivosAsync(soporte, inicio, fin, ct);
        var personas = await PersonasAsync(plataforma, turnos.Select(t => t.PersonaId), ct);
        var contacto = await VeContactoAsync(usuario, permisos, ct);
        var equipos = await NombresDeEquiposAsync(soporte, ct);
        var porFecha = PorFecha(festivos);
        return Results.Ok(new CalendarioDto(inicio, fin, equipo,
            [.. turnos.OrderBy(t => t.Fecha).ThenBy(t => equipos.GetValueOrDefault(t.EquipoId), StringComparer.CurrentCulture)
                .Select(t => TurnoDto(t, personas, contacto, porFecha, equipos))],
            festivos));
    }

    private static async Task<IResult> EsteSabadoAsync(
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma,
        TimeProvider reloj, CancellationToken ct)
    {
        var sabado = CalendarioBogota.SabadoDesde(CalendarioBogota.Hoy(reloj));
        var equipos = await soporte.Equipos.AsNoTracking().Where(e => e.Activo).OrderBy(e => e.Nombre).ToListAsync(ct);
        var turnos = await TurnosVigentes(soporte).Where(t => t.Fecha == sabado).OrderBy(t => t.CreadoEn).ToListAsync(ct);
        var festivos = PorFecha(await FestivosAsync(soporte, sabado, sabado, ct));
        var personas = await PersonasAsync(plataforma, turnos.Select(t => t.PersonaId), ct);
        var contacto = await VeContactoAsync(usuario, permisos, ct);
        var nombres = equipos.ToDictionary(e => e.Id, e => e.Nombre);
        return Results.Ok(new EsteSabadoDto(sabado, festivos.GetValueOrDefault(sabado),
            [.. equipos.Select(e =>
            {
                var delEquipo = turnos.Where(t => t.EquipoId == e.Id).ToList();
                return new EquipoEsteSabadoDto(new EquipoResumenDto(e.Id, e.Nombre),
                    [.. delEquipo.Select(t => TurnoDto(t, personas, contacto, festivos, nombres))],
                    Math.Max(0, e.PersonasMinimo - delEquipo.Count));
            })]));
    }

    /// <summary>Turnos programados, confirmados o aplicados (los que ocupan un puesto).</summary>
    internal static IQueryable<Turno> TurnosVigentes(SoporteDbContext soporte) =>
        soporte.Turnos.AsNoTracking()
            .Where(t => t.Estado == EstadoTurno.Programado || t.Estado == EstadoTurno.Confirmado || t.Estado == EstadoTurno.Aplicado);

    private static async Task<IResult> CrearAsync(
        [Description("Equipo, sábado, persona y horario del turno.")] NuevoTurno nuevo,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma,
        TimeProvider reloj, CancellationToken ct)
    {
        var equipo = await soporte.Equipos.AsNoTracking().FirstOrDefaultAsync(e => e.Id == nuevo.EquipoId, ct);
        if (equipo is null)
        {
            return Invalido("equipoId", "El equipo no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, equipo.Id, ct))
        {
            return SinPermiso(SinPermisoEquipo);
        }
        var errores = new Dictionary<string, string[]>();
        if (nuevo.Fecha == default || nuevo.Fecha.DayOfWeek != DayOfWeek.Saturday)
        {
            errores["fecha"] = ["El turno es de un sábado."];
        }
        var (inicio, fin) = LeerHorario(nuevo.HoraInicio, nuevo.HoraFin, "horaInicio", "horaFin", errores);
        var observacion = Limpiar(nuevo.Observacion);
        if (observacion is { Length: > SoporteDbContext.LargoObservacion })
        {
            errores["observacion"] = [$"La observación tiene máximo {SoporteDbContext.LargoObservacion} caracteres."];
        }
        if (!await soporte.Participantes.AnyAsync(p => p.EquipoId == equipo.Id && p.PersonaId == nuevo.PersonaId, ct))
        {
            errores["personaId"] = ["La persona de turno debe estar en la rotación del equipo."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var ocupados = await TurnosVigentes(soporte).Where(t => t.EquipoId == equipo.Id && t.Fecha == nuevo.Fecha).Select(t => t.PersonaId).ToListAsync(ct);
        if (ocupados.Contains(nuevo.PersonaId))
        {
            return Conflicto("turno_repetido", "Esa persona ya tiene turno ese sábado en el equipo.");
        }
        if (ocupados.Count >= equipo.PersonasMaximo)
        {
            return Conflicto("equipo_completo", $"El equipo ya tiene {equipo.PersonasMaximo} {(equipo.PersonasMaximo == 1 ? "persona" : "personas")} ese sábado: cambia o reemplaza un turno.");
        }
        var (porDefectoInicio, porDefectoFin) = HorarioDe(equipo, await ConfiguracionAsync(soporte, ct));
        var turno = new Turno
        {
            EquipoId = equipo.Id,
            Fecha = nuevo.Fecha,
            PersonaId = nuevo.PersonaId,
            Origen = OrigenTurno.Manual,
            HoraInicio = inicio ?? porDefectoInicio,
            HoraFin = inicio is null ? porDefectoFin : fin,
            Observacion = observacion,
            CreadoPorPersonaId = PersonaDeSesion(http.User),
        };
        soporte.Turnos.Add(turno);
        try
        {
            await GuardarAsync(soporte, plataforma,
                [Evento(http, reloj, "turno_creado", "soporte_turno", turno.Id, new { equipoId = equipo.Id, fecha = turno.Fecha, personaId = turno.PersonaId, origen = "manual" })], ct);
        }
        catch (DbUpdateException error) when (EsTurnoRepetido(error))
        {
            return Conflicto("turno_repetido", "Esa persona ya tiene turno ese sábado en el equipo.");
        }
        return Results.Created($"/api/v1/soporte/turnos/{turno.Id}", await DtoAsync(turno, http.User, permisos, soporte, plataforma, ct));
    }

    private static async Task<TurnoDto> DtoAsync(
        Turno turno, ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var personas = await PersonasAsync(plataforma, [turno.PersonaId], ct);
        var festivos = PorFecha(await FestivosAsync(soporte, turno.Fecha, turno.Fecha, ct));
        return TurnoDto(turno, personas, await VeContactoAsync(usuario, permisos, ct), festivos, await NombresDeEquiposAsync(soporte, ct));
    }

    private static async Task<IResult> CambiarAsync(
        [Description("Id del turno.")] Guid id,
        [Description("Horario y observación del turno.")] CambioTurno cambio,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma,
        TimeProvider reloj, CancellationToken ct)
    {
        var turno = await soporte.Turnos.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (turno is null)
        {
            return NoEncontrado("turno_no_encontrado", "El turno no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, turno.EquipoId, ct))
        {
            return SinPermiso(SinPermisoEquipo);
        }
        if (ReglasTurno.EsFinal(turno.Estado))
        {
            return Conflicto("turno_cerrado", $"El turno está {ReglasTurno.Texto(turno.Estado)}: ya no cambia.");
        }
        var errores = new Dictionary<string, string[]>();
        var (inicio, fin) = LeerHorario(cambio.HoraInicio, cambio.HoraFin, "horaInicio", "horaFin", errores);
        var observacion = Limpiar(cambio.Observacion);
        if (observacion is { Length: > SoporteDbContext.LargoObservacion })
        {
            errores["observacion"] = [$"La observación tiene máximo {SoporteDbContext.LargoObservacion} caracteres."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        turno.HoraInicio = inicio;
        turno.HoraFin = fin;
        turno.Observacion = observacion;
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "turno_cambiado", "soporte_turno", turno.Id, new { horaInicio = Hora(inicio), horaFin = Hora(fin) })], ct);
        return Results.Ok(await DtoAsync(turno, http.User, permisos, soporte, plataforma, ct));
    }

    private static async Task<IResult> CambiarEstadoAsync(
        [Description("Id del turno.")] Guid id,
        [Description("Estado nuevo y observación.")] CambioEstadoTurno cambio,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma,
        TimeProvider reloj, CancellationToken ct)
    {
        var estado = ReglasTurno.Leer<EstadoTurno>(cambio.Estado);
        if (estado is null or EstadoTurno.Programado or EstadoTurno.Reemplazado)
        {
            return Invalido("estado", "El estado nuevo es confirmado, aplicado o cancelado (para reemplazar usa la operación de reemplazo).");
        }
        var observacion = Limpiar(cambio.Observacion);
        var turno = await soporte.Turnos.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (turno is null)
        {
            return NoEncontrado("turno_no_encontrado", "El turno no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, turno.EquipoId, ct))
        {
            return SinPermiso(SinPermisoEquipo);
        }
        if (!ReglasTurno.PuedePasar(turno.Estado, estado.Value))
        {
            return Conflicto("transicion_invalida", $"Un turno {ReglasTurno.Texto(turno.Estado)} no puede pasar a {ReglasTurno.Texto(estado.Value)}.");
        }
        if (estado == EstadoTurno.Aplicado && turno.Fecha > CalendarioBogota.Hoy(reloj))
        {
            return Conflicto("turno_futuro", "Un turno solo se marca aplicado el día del turno o después.");
        }
        var juntas = observacion is null ? turno.Observacion : string.IsNullOrEmpty(turno.Observacion) ? observacion : $"{turno.Observacion}\n{observacion}";
        if (juntas is { Length: > SoporteDbContext.LargoObservacion })
        {
            return Invalido("observacion", $"La observación del turno quedaría con más de {SoporteDbContext.LargoObservacion} caracteres.");
        }
        var anterior = turno.Estado;
        turno.Estado = estado.Value;
        turno.Observacion = juntas;
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "turno_estado_cambiado", "soporte_turno", turno.Id,
                new { fecha = turno.Fecha, desde = ReglasTurno.Texto(anterior), hacia = ReglasTurno.Texto(estado.Value) })], ct);
        return Results.Ok(await DtoAsync(turno, http.User, permisos, soporte, plataforma, ct));
    }

    private static async Task<IResult> ReemplazarAsync(
        [Description("Id del turno que se reemplaza.")] Guid id,
        [Description("Persona que toma el turno.")] ReemplazoTurno reemplazo,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma,
        TimeProvider reloj, CancellationToken ct)
    {
        var observacion = Limpiar(reemplazo.Observacion);
        if (observacion is { Length: > SoporteDbContext.LargoObservacion })
        {
            return Invalido("observacion", $"La observación tiene máximo {SoporteDbContext.LargoObservacion} caracteres.");
        }
        var original = await soporte.Turnos.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (original is null)
        {
            return NoEncontrado("turno_no_encontrado", "El turno no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, original.EquipoId, ct))
        {
            return SinPermiso(SinPermisoEquipo);
        }
        if (!ReglasTurno.PuedePasar(original.Estado, EstadoTurno.Reemplazado))
        {
            return Conflicto("transicion_invalida", $"Un turno {ReglasTurno.Texto(original.Estado)} ya no se puede reemplazar.");
        }
        if (reemplazo.PersonaId == original.PersonaId)
        {
            return Invalido("personaId", "El reemplazo debe ser otra persona.");
        }
        if (!await soporte.Participantes.AnyAsync(p => p.EquipoId == original.EquipoId && p.PersonaId == reemplazo.PersonaId, ct))
        {
            return Invalido("personaId", "Quien toma el turno debe estar en la rotación del mismo equipo.");
        }
        if (await TurnosVigentes(soporte).AnyAsync(t => t.EquipoId == original.EquipoId && t.Fecha == original.Fecha && t.PersonaId == reemplazo.PersonaId, ct))
        {
            return Conflicto("turno_repetido", "Esa persona ya tiene turno ese sábado en el equipo.");
        }
        original.Estado = EstadoTurno.Reemplazado;
        var nuevo = new Turno
        {
            EquipoId = original.EquipoId,
            Fecha = original.Fecha,
            PersonaId = reemplazo.PersonaId,
            HoraInicio = original.HoraInicio,
            HoraFin = original.HoraFin,
            Origen = OrigenTurno.Manual,
            TurnoReemplazadoId = original.Id,
            Observacion = observacion,
            CreadoPorPersonaId = PersonaDeSesion(http.User),
        };
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "turno_reemplazado", "soporte_turno", original.Id,
                new { equipoId = original.EquipoId, fecha = original.Fecha, dePersonaId = original.PersonaId, aPersonaId = nuevo.PersonaId, turnoNuevoId = nuevo.Id })], ct,
            antes: async () =>
            {
                await soporte.SaveChangesAsync(ct);
                soporte.Turnos.Add(nuevo);
            });
        return Results.Created($"/api/v1/soporte/turnos/{nuevo.Id}", await DtoAsync(nuevo, http.User, permisos, soporte, plataforma, ct));
    }
}
