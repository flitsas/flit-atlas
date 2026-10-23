using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

/// <summary>
/// Cambios de turno y compensaciones (HU 2.9, prompt maestro §8.3, ATLAS-ADR-0029 y ATLAS-ADR-0032).
/// <list type="number">
/// <item>La persona de turno solicita el cambio y propone un reemplazo del mismo equipo (<c>atlas.soporte.solicitar_cambio</c>).</item>
/// <item>El reemplazo acepta o rechaza (<c>atlas.soporte.solicitar_cambio</c>, solo él).</item>
/// <item>Alguien con <c>atlas.soporte.aprobar</c> global o con alcance al equipo aprueba o rechaza. Al aprobar, el turno
/// original queda <c>reemplazado</c> y se crea el del reemplazo.</item>
/// </list>
/// Cada paso queda en el historial y avisa a los involucrados en el centro de notificaciones. Cada acción trae una llave
/// de idempotencia: reenviarla (la PWA la encola sin conexión) devuelve la solicitud tal como quedó, sin repetir nada.
/// </summary>
internal static class EndpointsCambios
{
    public const int LimiteSolicitudes = 200;
    public const int LimiteCompensaciones = 500;
    public const int LimiteAprobadoresNotificados = 50;

    /// <summary>Un «hecha en» del dispositivo más viejo que esto no se cree (relojes desfasados o reenvíos raros).</summary>
    private static readonly TimeSpan AntiguedadMaximaSinConexion = TimeSpan.FromDays(30);

    public const string Enlace = "/soporte/cambios";

    /// <summary>Tipo de notificación de los cambios de turno y compensaciones (centro de notificaciones, HU 1.6).</summary>
    public static readonly TipoNotificacion TipoCambios = new(
        "soporte.cambio_turno", "Cambios de turno de soporte",
        "Solicitudes de cambio de turno en las que participas y compensaciones que te registran.",
        new HashSet<CanalNotificacion> { CanalNotificacion.App, CanalNotificacion.Push });

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/solicitudes-cambio", ListarAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ListarSolicitudesCambioSoporte")
            .WithSummary("Solicitudes de cambio de turno")
            .WithDescription(
                $"Las solicitudes en las que participas (las pediste o te proponen como reemplazo) y las de los equipos que puedes " +
                $"aprobar, con su historial. estado: abiertas (por defecto), cerradas o todas. Hasta {LimiteSolicitudes}, de la más " +
                "reciente a la más antigua. Requiere atlas.soporte.ver.")
            .Produces<List<SolicitudCambioDto>>()
            .ProducesValidationProblem();

        grupo.MapGet("/solicitudes-cambio/{id:guid}", ObtenerAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ObtenerSolicitudCambioSoporte")
            .WithSummary("Una solicitud de cambio de turno")
            .WithDescription(
                "La solicitud con su historial completo, si participas en ella o puedes aprobar o gestionar su equipo (si no, 404). " +
                "Requiere atlas.soporte.ver.")
            .Produces<SolicitudCambioDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound);

        grupo.MapPost("/solicitudes-cambio", SolicitarAsync)
            .RequierePermiso(PermisosSoporte.SolicitarCambio)
            .WithName("SolicitarCambioTurnoSoporte")
            .WithSummary("Solicitar un cambio de turno")
            .WithDescription(
                "La persona de un turno programado o confirmado (de hoy en adelante) propone a otra persona activa de la rotación " +
                "del mismo equipo como reemplazo; se le avisa al reemplazo. Una solicitud abierta por turno (409). Con la misma " +
                "llave de idempotencia responde 200 con la solicitud ya creada. Requiere atlas.soporte.solicitar_cambio.")
            .Produces<SolicitudCambioDto>(StatusCodes.Status201Created)
            .Produces<SolicitudCambioDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/solicitudes-cambio/{id:guid}/respuesta", ResponderAsync)
            .RequierePermiso(PermisosSoporte.SolicitarCambio)
            .WithName("ResponderSolicitudCambioSoporte")
            .WithSummary("Aceptar o rechazar un reemplazo")
            .WithDescription(
                "Solo el reemplazo propuesto, mientras la solicitud espera su respuesta. Si acepta, pasa a pendiente_aprobacion y " +
                "se avisa a quien la pidió y a quienes pueden aprobar; si rechaza, queda rechazada. Idempotente por llave. " +
                "Requiere atlas.soporte.solicitar_cambio.")
            .Produces<SolicitudCambioDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/solicitudes-cambio/{id:guid}/resolucion", ResolverAsync)
            .RequierePermiso(PermisosSoporte.Aprobar)
            .WithName("ResolverSolicitudCambioSoporte")
            .WithSummary("Aprobar o rechazar un cambio de turno")
            .WithDescription(
                "Quien tiene atlas.soporte.aprobar global o con alcance al equipo, cuando el reemplazo ya aceptó. Al aprobar, el " +
                "turno original queda reemplazado y se crea el del reemplazo (manual, programado, con el mismo horario); 409 si " +
                "el turno cambió entretanto. Avisa a los dos. Idempotente por llave. Requiere atlas.soporte.aprobar.")
            .Produces<SolicitudCambioDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/solicitudes-cambio/{id:guid}/cancelacion", CancelarAsync)
            .RequierePermiso(PermisosSoporte.SolicitarCambio)
            .WithName("CancelarSolicitudCambioSoporte")
            .WithSummary("Cancelar una solicitud de cambio")
            .WithDescription(
                "Solo quien la pidió, mientras siga abierta. Avisa al reemplazo. Idempotente por llave. Requiere " +
                "atlas.soporte.solicitar_cambio.")
            .Produces<SolicitudCambioDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapGet("/compensaciones", ListarCompensacionesAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ListarCompensacionesSoporte")
            .WithSummary("Compensaciones")
            .WithDescription(
                $"Días compensados por turnos, con filtros por estado (pendiente, tomada o todas), persona y equipo; hasta " +
                $"{LimiteCompensaciones}, del turno más reciente al más antiguo. La nota solo la ven la persona compensada y " +
                "quien aprueba o gestiona el equipo. Requiere atlas.soporte.ver.")
            .Produces<List<CompensacionDto>>()
            .ProducesValidationProblem();

        grupo.MapGet("/compensaciones/pendientes", ReportePendientesAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ReporteCompensacionesPendientesSoporte")
            .WithSummary("Compensaciones pendientes por persona")
            .WithDescription("Reporte de las compensaciones pendientes por persona (de un equipo o de todos). Requiere atlas.soporte.ver.")
            .Produces<ReporteCompensacionesDto>();

        grupo.MapPost("/compensaciones", RegistrarCompensacionAsync)
            .RequierePermiso(PermisosSoporte.Aprobar)
            .WithName("RegistrarCompensacionSoporte")
            .WithSummary("Registrar una compensación")
            .WithDescription(
                "Día compensado para la persona de un turno vigente (pendiente, con quien la registra como quien aprobó). Una por " +
                "turno y persona (409). Avisa a la persona. Requiere atlas.soporte.aprobar global o con alcance al equipo del turno.")
            .Produces<CompensacionDto>(StatusCodes.Status201Created)
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPut("/compensaciones/{id:guid}", CambiarCompensacionAsync)
            .RequierePermiso(PermisosSoporte.Aprobar)
            .WithName("CambiarCompensacionSoporte")
            .WithSummary("Cambiar una compensación")
            .WithDescription(
                "El día compensado y el estado (tomada exige el día). Requiere atlas.soporte.aprobar global o con alcance al " +
                "equipo del turno.")
            .Produces<CompensacionDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    // --- Lectura ---

    private static bool Abierta(EstadoSolicitud estado) => estado is EstadoSolicitud.PendienteReemplazo or EstadoSolicitud.PendienteAprobacion;

    /// <summary>Arma los DTO de varias solicitudes con una sola lectura de turnos, personas, historial y festivos.</summary>
    private static async Task<List<SolicitudCambioDto>> DtosAsync(
        List<SolicitudCambio> solicitudes, ClaimsPrincipal usuario, IPermisosDeSesion permisos,
        SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        if (solicitudes.Count == 0)
        {
            return [];
        }
        var ids = solicitudes.Select(s => s.Id).ToList();
        var idsTurnos = solicitudes.Select(s => s.TurnoId).Distinct().ToList();
        var turnos = await soporte.Turnos.AsNoTracking().Where(t => idsTurnos.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var eventos = await soporte.EventosSolicitud.AsNoTracking().Where(e => ids.Contains(e.SolicitudId))
            .OrderBy(e => e.CreadoEn).ThenBy(e => e.Id).ToListAsync(ct);
        var personas = await PersonasAsync(plataforma,
            solicitudes.SelectMany(s => new[] { s.SolicitantePersonaId, s.ReemplazoPersonaId, s.ResueltaPorPersonaId ?? Guid.Empty })
                .Concat(eventos.Select(e => e.PersonaId)).Concat(turnos.Values.Select(t => t.PersonaId)).Where(g => g != Guid.Empty), ct);
        var contacto = await EndpointsTurnos.VeContactoAsync(usuario, permisos, ct);
        var efectivos = await permisos.ObtenerAsync(usuario, ct);
        var equipos = await NombresDeEquiposAsync(soporte, ct);
        var desde = solicitudes.Min(s => s.Fecha);
        var hasta = solicitudes.Max(s => s.Fecha);
        var festivos = PorFecha(await FestivosAsync(soporte, desde, hasta, ct));
        var yo = PersonaDeSesion(usuario);
        return [.. solicitudes.Select(s => new SolicitudCambioDto(
            s.Id, TurnoDto(turnos[s.TurnoId], personas, contacto, festivos, equipos),
            PersonaDto(personas, s.SolicitantePersonaId, contacto), PersonaDto(personas, s.ReemplazoPersonaId, contacto),
            ReglasTurno.Texto(s.Estado), s.Motivo, s.TurnoNuevoId,
            s.ResueltaPorPersonaId is { } r ? PersonaDto(personas, r, contacto) : null, s.ResueltaEn, s.CreadoEn,
            [.. eventos.Where(e => e.SolicitudId == s.Id).Select(e => new EventoSolicitudDto(
                ReglasTurno.Texto(e.Accion), new PersonaResumenDto(e.PersonaId, NombreDe(personas, e.PersonaId)), e.Nota, e.CreadoEn, e.HechaEn))],
            s.Estado == EstadoSolicitud.PendienteReemplazo && s.ReemplazoPersonaId == yo && efectivos.Tiene(PermisosSoporte.SolicitarCambio),
            s.Estado == EstadoSolicitud.PendienteAprobacion && efectivos.TieneEn(PermisosSoporte.Aprobar, Alcance(s.EquipoId)),
            Abierta(s.Estado) && s.SolicitantePersonaId == yo && efectivos.Tiene(PermisosSoporte.SolicitarCambio)))];
    }

    private static async Task<SolicitudCambioDto> DtoAsync(
        SolicitudCambio solicitud, ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct) =>
        (await DtosAsync([solicitud], usuario, permisos, soporte, plataforma, ct))[0];

    /// <summary>¿Participa en la solicitud o puede aprobar o gestionar su equipo?</summary>
    private static bool PuedeVer(SolicitudCambio s, Guid yo, PermisosEfectivos efectivos) =>
        s.SolicitantePersonaId == yo || s.ReemplazoPersonaId == yo
        || efectivos.TieneEn(PermisosSoporte.Aprobar, Alcance(s.EquipoId)) || efectivos.TieneEn(PermisosSoporte.Gestionar, Alcance(s.EquipoId));

    private static async Task<IResult> ListarAsync(
        [Description("abiertas (por defecto), cerradas o todas.")] string? estado,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var filtro = (estado ?? "abiertas").Trim();
        if (filtro is not ("abiertas" or "cerradas" or "todas"))
        {
            return Invalido("estado", "El estado es abiertas, cerradas o todas.");
        }
        var yo = PersonaDeSesion(usuario);
        var efectivos = await permisos.ObtenerAsync(usuario, ct);
        var equipos = await soporte.Equipos.AsNoTracking().Select(e => e.Id).ToListAsync(ct);
        var aprueba = equipos.Where(e => efectivos.TieneEn(PermisosSoporte.Aprobar, Alcance(e)) || efectivos.TieneEn(PermisosSoporte.Gestionar, Alcance(e))).ToList();
        var consulta = soporte.SolicitudesCambio.AsNoTracking()
            .Where(s => s.SolicitantePersonaId == yo || s.ReemplazoPersonaId == yo || aprueba.Contains(s.EquipoId));
        consulta = filtro switch
        {
            "abiertas" => consulta.Where(s => s.Estado == EstadoSolicitud.PendienteReemplazo || s.Estado == EstadoSolicitud.PendienteAprobacion),
            "cerradas" => consulta.Where(s => s.Estado != EstadoSolicitud.PendienteReemplazo && s.Estado != EstadoSolicitud.PendienteAprobacion),
            _ => consulta,
        };
        var lista = await consulta.OrderByDescending(s => s.CreadoEn).ThenByDescending(s => s.Id).Take(LimiteSolicitudes).ToListAsync(ct);
        return Results.Ok(await DtosAsync(lista, usuario, permisos, soporte, plataforma, ct));
    }

    private static async Task<IResult> ObtenerAsync(
        [Description("Id de la solicitud.")] Guid id,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var solicitud = await soporte.SolicitudesCambio.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (solicitud is null || !PuedeVer(solicitud, PersonaDeSesion(usuario), await permisos.ObtenerAsync(usuario, ct)))
        {
            return NoEncontrado("solicitud_no_encontrada", "La solicitud no existe.");
        }
        return Results.Ok(await DtoAsync(solicitud, usuario, permisos, soporte, plataforma, ct));
    }

    // --- Idempotencia ---

    /// <summary>
    /// Si la llave ya se usó: la solicitud tal como quedó (misma persona, misma acción y misma solicitud) o 409 si la llave
    /// es de otra cosa. <c>(null, null)</c> si la llave es nueva.
    /// </summary>
    private static async Task<(IResult? Repetida, bool Usada)> RepetidaAsync(
        Guid llave, Guid? solicitudId, AccionSolicitud[] acciones, ClaimsPrincipal usuario, IPermisosDeSesion permisos,
        SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var previo = await soporte.EventosSolicitud.AsNoTracking().FirstOrDefaultAsync(e => e.LlaveIdempotencia == llave, ct);
        if (previo is null)
        {
            return (null, false);
        }
        if (previo.PersonaId != PersonaDeSesion(usuario) || !acciones.Contains(previo.Accion) || (solicitudId is { } s && previo.SolicitudId != s))
        {
            return (Conflicto("llave_usada", "Esa llave de idempotencia ya se usó para otra acción."), true);
        }
        var solicitud = await soporte.SolicitudesCambio.AsNoTracking().FirstAsync(x => x.Id == previo.SolicitudId, ct);
        return (Results.Ok(await DtoAsync(solicitud, usuario, permisos, soporte, plataforma, ct)), true);
    }

    private static bool EsLlaveRepetida(DbUpdateException error) =>
        error.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == "ux_soporte_solicitud_evento_llave";

    private static bool EsSolicitudAbiertaRepetida(DbUpdateException error) =>
        error.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == "ux_soporte_solicitud_cambio_abierta";

    /// <summary>«Hecha en» del dispositivo: se guarda si es creíble (no en el futuro ni de hace más de 30 días).</summary>
    private static DateTimeOffset? HechaEn(DateTimeOffset? valor, TimeProvider reloj)
    {
        var ahora = reloj.GetUtcNow();
        return valor is { } v && v <= ahora.AddMinutes(5) && v >= ahora - AntiguedadMaximaSinConexion ? v : null;
    }

    private static IResult? ValidarNota(string? nota, string campo = "nota") =>
        nota is { Length: > SoporteDbContext.LargoObservacion } ? Invalido(campo, $"La nota tiene máximo {SoporteDbContext.LargoObservacion} caracteres.") : null;

    // --- Notificaciones ---

    private static string Dia(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static Notificacion Aviso(Guid persona, string titulo, string cuerpo) => new()
    {
        PersonaId = persona, Tipo = TipoCambios.Codigo, Titulo = titulo, Cuerpo = cuerpo, Enlace = Enlace,
    };

    /// <summary>
    /// Personas internas activas que pueden aprobar en el equipo (rol vigente con <c>atlas.soporte.aprobar</c>, global o
    /// con alcance al equipo), sin contar a <paramref name="sin"/>. Incluye los roles que vienen de grupos de Entra (se
    /// guardan como asignaciones).
    /// </summary>
    private static async Task<List<Guid>> AprobadoresAsync(
        PlataformaDbContext plataforma, CatalogoPermisos catalogo, TimeProvider reloj, Guid equipoId, IEnumerable<Guid> sin, CancellationToken ct)
    {
        var roles = catalogo.Roles.Where(r => catalogo.PermisosDe(r.Codigo).Contains(PermisosSoporte.Aprobar)).Select(r => r.Codigo).ToList();
        var ahora = reloj.GetUtcNow();
        var alcance = equipoId.ToString("D");
        var excluir = sin.ToList();
        return await plataforma.AsignacionesRol.AsNoTracking()
            .Where(a => a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora)
                && (a.AlcanceTipo == TiposAlcance.Global || (a.AlcanceTipo == AlcanceEquipo && a.AlcanceId == alcance)))
            .Join(plataforma.Roles.Where(r => roles.Contains(r.Codigo)), a => a.RolId, r => r.Id, (a, r) => a.PersonaId)
            .Join(plataforma.Personas.Where(p => p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa), id => id, p => p.Id, (id, p) => p.Id)
            .Where(id => !excluir.Contains(id))
            .Distinct().OrderBy(id => id).Take(LimiteAprobadoresNotificados).ToListAsync(ct);
    }

    // --- Solicitar ---

    private static async Task<IResult> SolicitarAsync(
        [Description("Turno, reemplazo propuesto, motivo y llave de idempotencia.")] NuevaSolicitudCambio nueva,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (nueva.LlaveIdempotencia == Guid.Empty)
        {
            return Invalido("llaveIdempotencia", "Envía una llave de idempotencia (uuid) por acción.");
        }
        var motivo = Limpiar(nueva.Motivo);
        if (ValidarNota(motivo, "motivo") is { } malo)
        {
            return malo;
        }
        var (repetida, usada) = await RepetidaAsync(nueva.LlaveIdempotencia, null, [AccionSolicitud.Solicitada], http.User, permisos, soporte, plataforma, ct);
        if (usada)
        {
            return repetida!;
        }
        var turno = await soporte.Turnos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == nueva.TurnoId, ct);
        if (turno is null)
        {
            return NoEncontrado("turno_no_encontrado", "El turno no existe.");
        }
        var yo = PersonaDeSesion(http.User);
        if (turno.PersonaId != yo)
        {
            return Results.Json(new ErrorSoporte("no_es_tu_turno", "Solo la persona de turno puede pedir el cambio."), statusCode: StatusCodes.Status403Forbidden);
        }
        if (turno.Estado is not (EstadoTurno.Programado or EstadoTurno.Confirmado))
        {
            return Conflicto("turno_cerrado", $"El turno está {ReglasTurno.Texto(turno.Estado)}: ya no se puede cambiar.");
        }
        if (turno.Fecha < CalendarioBogota.Hoy(reloj))
        {
            return Conflicto("turno_pasado", "El turno ya pasó.");
        }
        if (nueva.ReemplazoPersonaId == yo)
        {
            return Invalido("reemplazoPersonaId", "El reemplazo debe ser otra persona.");
        }
        var participa = await soporte.Participantes.AsNoTracking()
            .AnyAsync(p => p.EquipoId == turno.EquipoId && p.PersonaId == nueva.ReemplazoPersonaId && p.Activo, ct);
        var reemplazo = await plataforma.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == nueva.ReemplazoPersonaId, ct);
        if (!participa || reemplazo is null || reemplazo.Estado != EstadoPersona.Activa)
        {
            return Invalido("reemplazoPersonaId", "El reemplazo debe ser una persona activa de la rotación del mismo equipo.");
        }
        if (await EndpointsTurnos.TurnosVigentes(soporte).AnyAsync(t => t.EquipoId == turno.EquipoId && t.Fecha == turno.Fecha && t.PersonaId == reemplazo.Id, ct))
        {
            return Conflicto("turno_repetido", $"{reemplazo.Nombre} ya tiene turno ese sábado en el equipo.");
        }
        if (await soporte.SolicitudesCambio.AnyAsync(s => s.TurnoId == turno.Id
            && (s.Estado == EstadoSolicitud.PendienteReemplazo || s.Estado == EstadoSolicitud.PendienteAprobacion), ct))
        {
            return Conflicto("solicitud_abierta", "Este turno ya tiene una solicitud de cambio abierta.");
        }
        var solicitud = new SolicitudCambio
        {
            TurnoId = turno.Id,
            EquipoId = turno.EquipoId,
            Fecha = turno.Fecha,
            SolicitantePersonaId = yo,
            ReemplazoPersonaId = reemplazo.Id,
            Motivo = motivo,
        };
        soporte.SolicitudesCambio.Add(solicitud);
        soporte.EventosSolicitud.Add(new EventoSolicitudCambio
        {
            SolicitudId = solicitud.Id, Accion = AccionSolicitud.Solicitada, PersonaId = yo, Nota = motivo,
            LlaveIdempotencia = nueva.LlaveIdempotencia, HechaEn = HechaEn(nueva.HechaEn, reloj),
        });
        var nombres = await PersonasAsync(plataforma, [yo], ct);
        var equipo = (await NombresDeEquiposAsync(soporte, ct)).GetValueOrDefault(turno.EquipoId) ?? "Soporte";
        try
        {
            await GuardarAsync(soporte, plataforma,
                [Evento(http, reloj, "solicitud_cambio_creada", "soporte_solicitud_cambio", solicitud.Id,
                    new { turnoId = turno.Id, equipoId = turno.EquipoId, fecha = turno.Fecha, reemplazoPersonaId = reemplazo.Id })], ct,
                notificaciones: [Aviso(reemplazo.Id, "Te proponen como reemplazo",
                    $"{NombreDe(nombres, yo)} te pide tomar su turno de {equipo} del sábado {Dia(turno.Fecha)}. Acepta o rechaza en Cambios de turno.")]);
        }
        catch (DbUpdateException error) when (EsLlaveRepetida(error))
        {
            soporte.ChangeTracker.Clear();
            plataforma.ChangeTracker.Clear();
            return (await RepetidaAsync(nueva.LlaveIdempotencia, null, [AccionSolicitud.Solicitada], http.User, permisos, soporte, plataforma, ct)).Repetida!;
        }
        catch (DbUpdateException error) when (EsSolicitudAbiertaRepetida(error))
        {
            return Conflicto("solicitud_abierta", "Este turno ya tiene una solicitud de cambio abierta.");
        }
        return Results.Created($"/api/v1/soporte/solicitudes-cambio/{solicitud.Id}", await DtoAsync(solicitud, http.User, permisos, soporte, plataforma, ct));
    }

    // --- Responder, resolver y cancelar ---

    /// <summary>Lo común de las acciones sobre una solicitud: llave, repetición, existencia y nota.</summary>
    private static async Task<(SolicitudCambio? Solicitud, IResult? Resultado)> PrepararAsync(
        Guid id, Guid llave, string? nota, AccionSolicitud[] acciones, HttpContext http, IPermisosDeSesion permisos,
        SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        if (llave == Guid.Empty)
        {
            return (null, Invalido("llaveIdempotencia", "Envía una llave de idempotencia (uuid) por acción."));
        }
        if (ValidarNota(nota) is { } malo)
        {
            return (null, malo);
        }
        var (repetida, usada) = await RepetidaAsync(llave, id, acciones, http.User, permisos, soporte, plataforma, ct);
        if (usada)
        {
            return (null, repetida);
        }
        var solicitud = await soporte.SolicitudesCambio.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (solicitud is null || !PuedeVer(solicitud, PersonaDeSesion(http.User), await permisos.ObtenerAsync(http.User, ct)))
        {
            return (null, NoEncontrado("solicitud_no_encontrada", "La solicitud no existe."));
        }
        return (solicitud, null);
    }

    /// <summary>Guarda una acción (con su evento y avisos) y, si la llave entró a la vez por otra petición, responde la repetición.</summary>
    private static async Task<IResult> GuardarAccionAsync(
        SolicitudCambio solicitud, EventoSolicitudCambio evento, EventoAuditoria[] auditoria, IEnumerable<Notificacion> avisos,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct,
        Func<Task>? antes = null)
    {
        soporte.EventosSolicitud.Add(evento);
        try
        {
            await GuardarAsync(soporte, plataforma, auditoria, ct, antes, avisos);
        }
        catch (DbUpdateException error) when (EsLlaveRepetida(error))
        {
            soporte.ChangeTracker.Clear();
            plataforma.ChangeTracker.Clear();
            return (await RepetidaAsync(evento.LlaveIdempotencia, solicitud.Id, [evento.Accion], http.User, permisos, soporte, plataforma, ct)).Repetida!;
        }
        catch (DbUpdateException error) when (EsTurnoRepetido(error))
        {
            return Conflicto("turno_repetido", "El reemplazo ya tiene turno ese sábado en el equipo.");
        }
        return Results.Ok(await DtoAsync(solicitud, http.User, permisos, soporte, plataforma, ct));
    }

    private static async Task<IResult> ResponderAsync(
        [Description("Id de la solicitud.")] Guid id,
        [Description("Si el reemplazo acepta, su nota y la llave de idempotencia.")] RespuestaSolicitud respuesta,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, CatalogoPermisos catalogo,
        TimeProvider reloj, CancellationToken ct)
    {
        var nota = Limpiar(respuesta.Nota);
        var (solicitud, resultado) = await PrepararAsync(id, respuesta.LlaveIdempotencia, nota,
            [AccionSolicitud.Aceptada, AccionSolicitud.RechazadaPorReemplazo], http, permisos, soporte, plataforma, ct);
        if (solicitud is null)
        {
            return resultado!;
        }
        var yo = PersonaDeSesion(http.User);
        if (solicitud.ReemplazoPersonaId != yo)
        {
            return Results.Json(new ErrorSoporte("no_eres_el_reemplazo", "Solo el reemplazo propuesto puede responder."), statusCode: StatusCodes.Status403Forbidden);
        }
        if (solicitud.Estado != EstadoSolicitud.PendienteReemplazo)
        {
            return Conflicto("solicitud_cerrada", $"La solicitud está {ReglasTurno.Texto(solicitud.Estado)}: ya no espera tu respuesta.");
        }
        var nombres = await PersonasAsync(plataforma, [yo], ct);
        var equipo = (await NombresDeEquiposAsync(soporte, ct)).GetValueOrDefault(solicitud.EquipoId) ?? "Soporte";
        var avisos = new List<Notificacion>();
        if (respuesta.Acepta)
        {
            solicitud.Estado = EstadoSolicitud.PendienteAprobacion;
            avisos.Add(Aviso(solicitud.SolicitantePersonaId, "Tu reemplazo aceptó",
                $"{NombreDe(nombres, yo)} aceptó tomar tu turno de {equipo} del sábado {Dia(solicitud.Fecha)}. Falta la aprobación."));
            foreach (var aprobador in await AprobadoresAsync(plataforma, catalogo, reloj, solicitud.EquipoId, [yo, solicitud.SolicitantePersonaId], ct))
            {
                avisos.Add(Aviso(aprobador, "Cambio de turno por aprobar",
                    $"Cambio de turno de {equipo} del sábado {Dia(solicitud.Fecha)}: el reemplazo ya aceptó y falta tu aprobación."));
            }
        }
        else
        {
            solicitud.Estado = EstadoSolicitud.Rechazada;
            avisos.Add(Aviso(solicitud.SolicitantePersonaId, "Tu reemplazo no aceptó",
                $"{NombreDe(nombres, yo)} no puede tomar tu turno de {equipo} del sábado {Dia(solicitud.Fecha)}. Propón a otra persona."));
        }
        var accion = respuesta.Acepta ? AccionSolicitud.Aceptada : AccionSolicitud.RechazadaPorReemplazo;
        return await GuardarAccionAsync(solicitud,
            new EventoSolicitudCambio
            {
                SolicitudId = solicitud.Id, Accion = accion, PersonaId = yo, Nota = nota, LlaveIdempotencia = respuesta.LlaveIdempotencia,
                HechaEn = HechaEn(respuesta.HechaEn, reloj),
            },
            [Evento(http, reloj, respuesta.Acepta ? "solicitud_cambio_aceptada" : "solicitud_cambio_rechazada_por_reemplazo", "soporte_solicitud_cambio", solicitud.Id)],
            avisos, http, permisos, soporte, plataforma, ct);
    }

    private static async Task<IResult> ResolverAsync(
        [Description("Id de la solicitud.")] Guid id,
        [Description("Si aprueba, su nota y la llave de idempotencia.")] ResolucionSolicitud resolucion,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var nota = Limpiar(resolucion.Nota);
        var (solicitud, resultado) = await PrepararAsync(id, resolucion.LlaveIdempotencia, nota,
            [AccionSolicitud.Aprobada, AccionSolicitud.Rechazada], http, permisos, soporte, plataforma, ct);
        if (solicitud is null)
        {
            return resultado!;
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Aprobar, solicitud.EquipoId, ct))
        {
            return SinPermiso("No tienes atlas.soporte.aprobar sobre el equipo de este turno.");
        }
        if (solicitud.Estado != EstadoSolicitud.PendienteAprobacion)
        {
            return Conflicto("solicitud_cerrada", $"La solicitud está {ReglasTurno.Texto(solicitud.Estado)}: no espera aprobación.");
        }
        var yo = PersonaDeSesion(http.User);
        var equipo = (await NombresDeEquiposAsync(soporte, ct)).GetValueOrDefault(solicitud.EquipoId) ?? "Soporte";
        var ahora = reloj.GetUtcNow();
        Func<Task>? antes = null;
        Guid? turnoNuevo = null;
        var auditoria = new List<EventoAuditoria>();
        string titulo;
        string cuerpo;
        if (resolucion.Aprueba)
        {
            var original = await soporte.Turnos.FirstAsync(t => t.Id == solicitud.TurnoId, ct);
            if (original.PersonaId != solicitud.SolicitantePersonaId || !ReglasTurno.PuedePasar(original.Estado, EstadoTurno.Reemplazado))
            {
                return Conflicto("turno_cambiado", "El turno cambió desde la solicitud: ya no se puede aprobar. Recházala.");
            }
            if (await EndpointsTurnos.TurnosVigentes(soporte).AnyAsync(t => t.EquipoId == original.EquipoId && t.Fecha == original.Fecha
                && t.PersonaId == solicitud.ReemplazoPersonaId, ct))
            {
                return Conflicto("turno_repetido", "El reemplazo ya tiene turno ese sábado en el equipo.");
            }
            original.Estado = EstadoTurno.Reemplazado;
            var nuevo = new Turno
            {
                EquipoId = original.EquipoId,
                Fecha = original.Fecha,
                PersonaId = solicitud.ReemplazoPersonaId,
                HoraInicio = original.HoraInicio,
                HoraFin = original.HoraFin,
                Origen = OrigenTurno.Manual,
                TurnoReemplazadoId = original.Id,
                Observacion = "Cambio de turno aprobado.",
                CreadoPorPersonaId = yo,
            };
            solicitud.Estado = EstadoSolicitud.Aprobada;
            turnoNuevo = nuevo.Id;
            // El original pasa a reemplazado antes de crear el nuevo (índice único de turnos vigentes); la solicitud apunta
            // al nuevo cuando ya existe.
            antes = async () =>
            {
                await soporte.SaveChangesAsync(ct);
                soporte.Turnos.Add(nuevo);
                solicitud.TurnoNuevoId = nuevo.Id;
            };
            auditoria.Add(Evento(http, reloj, "turno_reemplazado", "soporte_turno", original.Id, new
            {
                equipoId = original.EquipoId, fecha = original.Fecha, dePersonaId = original.PersonaId, aPersonaId = nuevo.PersonaId, turnoNuevoId = nuevo.Id,
                solicitudId = solicitud.Id,
            }));
            titulo = "Cambio de turno aprobado";
            cuerpo = $"Se aprobó el cambio del turno de {equipo} del sábado {Dia(solicitud.Fecha)}.";
        }
        else
        {
            solicitud.Estado = EstadoSolicitud.Rechazada;
            titulo = "Cambio de turno rechazado";
            cuerpo = $"No se aprobó el cambio del turno de {equipo} del sábado {Dia(solicitud.Fecha)}.";
        }
        solicitud.ResueltaPorPersonaId = yo;
        solicitud.ResueltaEn = ahora;
        auditoria.Add(Evento(http, reloj, resolucion.Aprueba ? "solicitud_cambio_aprobada" : "solicitud_cambio_rechazada", "soporte_solicitud_cambio", solicitud.Id,
            new { turnoId = solicitud.TurnoId, turnoNuevoId = turnoNuevo }));
        return await GuardarAccionAsync(solicitud,
            new EventoSolicitudCambio
            {
                SolicitudId = solicitud.Id, Accion = resolucion.Aprueba ? AccionSolicitud.Aprobada : AccionSolicitud.Rechazada, PersonaId = yo,
                Nota = nota, LlaveIdempotencia = resolucion.LlaveIdempotencia, HechaEn = HechaEn(resolucion.HechaEn, reloj),
            },
            [.. auditoria],
            [Aviso(solicitud.SolicitantePersonaId, titulo, cuerpo), Aviso(solicitud.ReemplazoPersonaId, titulo, cuerpo)],
            http, permisos, soporte, plataforma, ct, antes);
    }

    private static async Task<IResult> CancelarAsync(
        [Description("Id de la solicitud.")] Guid id,
        [Description("Nota y llave de idempotencia.")] CancelacionSolicitud cancelacion,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var nota = Limpiar(cancelacion.Nota);
        var (solicitud, resultado) = await PrepararAsync(id, cancelacion.LlaveIdempotencia, nota, [AccionSolicitud.Cancelada], http, permisos, soporte, plataforma, ct);
        if (solicitud is null)
        {
            return resultado!;
        }
        var yo = PersonaDeSesion(http.User);
        if (solicitud.SolicitantePersonaId != yo)
        {
            return Results.Json(new ErrorSoporte("no_es_tu_solicitud", "Solo quien pidió el cambio puede cancelarlo."), statusCode: StatusCodes.Status403Forbidden);
        }
        if (!Abierta(solicitud.Estado))
        {
            return Conflicto("solicitud_cerrada", $"La solicitud está {ReglasTurno.Texto(solicitud.Estado)}: ya no se puede cancelar.");
        }
        solicitud.Estado = EstadoSolicitud.Cancelada;
        var equipo = (await NombresDeEquiposAsync(soporte, ct)).GetValueOrDefault(solicitud.EquipoId) ?? "Soporte";
        return await GuardarAccionAsync(solicitud,
            new EventoSolicitudCambio
            {
                SolicitudId = solicitud.Id, Accion = AccionSolicitud.Cancelada, PersonaId = yo, Nota = nota,
                LlaveIdempotencia = cancelacion.LlaveIdempotencia, HechaEn = HechaEn(cancelacion.HechaEn, reloj),
            },
            [Evento(http, reloj, "solicitud_cambio_cancelada", "soporte_solicitud_cambio", solicitud.Id)],
            [Aviso(solicitud.ReemplazoPersonaId, "Solicitud de cambio cancelada",
                $"Se canceló la solicitud para que tomaras el turno de {equipo} del sábado {Dia(solicitud.Fecha)}.")],
            http, permisos, soporte, plataforma, ct);
    }

    // --- Compensaciones ---

    private static async Task<List<CompensacionDto>> CompensacionesDtoAsync(
        List<Compensacion> compensaciones, ClaimsPrincipal usuario, IPermisosDeSesion permisos,
        SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        if (compensaciones.Count == 0)
        {
            return [];
        }
        var idsTurnos = compensaciones.Select(c => c.TurnoId).Distinct().ToList();
        var turnos = await soporte.Turnos.AsNoTracking().Where(t => idsTurnos.Contains(t.Id))
            .Select(t => new { t.Id, t.Fecha, t.EquipoId }).ToDictionaryAsync(t => t.Id, ct);
        var personas = await PersonasAsync(plataforma,
            compensaciones.Select(c => c.PersonaId).Concat(compensaciones.Where(c => c.AprobadaPorPersonaId.HasValue).Select(c => c.AprobadaPorPersonaId!.Value)), ct);
        var contacto = await EndpointsTurnos.VeContactoAsync(usuario, permisos, ct);
        var efectivos = await permisos.ObtenerAsync(usuario, ct);
        var equipos = await NombresDeEquiposAsync(soporte, ct);
        var yo = PersonaDeSesion(usuario);
        return [.. compensaciones.Select(c =>
        {
            var turno = turnos[c.TurnoId];
            var aprueba = efectivos.TieneEn(PermisosSoporte.Aprobar, Alcance(turno.EquipoId));
            var veNota = aprueba || c.PersonaId == yo || efectivos.TieneEn(PermisosSoporte.Gestionar, Alcance(turno.EquipoId));
            return new CompensacionDto(c.Id,
                new TurnoResumenDto(turno.Id, turno.Fecha, new EquipoResumenDto(turno.EquipoId, equipos.GetValueOrDefault(turno.EquipoId) ?? "Equipo")),
                PersonaDto(personas, c.PersonaId, contacto), c.FechaCompensada, ReglasTurno.Texto(c.Estado),
                c.AprobadaPorPersonaId is { } a ? PersonaDto(personas, a, contacto) : null, veNota ? c.Nota : null, aprueba, c.CreadoEn);
        })];
    }

    private static async Task<IResult> ListarCompensacionesAsync(
        [Description("pendiente, tomada o todas (por defecto).")] string? estado,
        [Description("Id de la persona compensada.")] Guid? persona,
        [Description("Id del equipo del turno.")] Guid? equipo,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, CancellationToken ct)
    {
        var filtro = (estado ?? "todas").Trim();
        EstadoCompensacion? exacto = null;
        if (filtro != "todas")
        {
            exacto = ReglasTurno.Leer<EstadoCompensacion>(filtro);
            if (exacto is null)
            {
                return Invalido("estado", "El estado es pendiente, tomada o todas.");
            }
        }
        var consulta = soporte.Compensaciones.AsNoTracking()
            .Join(soporte.Turnos, c => c.TurnoId, t => t.Id, (c, t) => new { c, t.Fecha, t.EquipoId });
        if (exacto is { } e)
        {
            consulta = consulta.Where(x => x.c.Estado == e);
        }
        if (persona is { } p)
        {
            consulta = consulta.Where(x => x.c.PersonaId == p);
        }
        if (equipo is { } q)
        {
            consulta = consulta.Where(x => x.EquipoId == q);
        }
        var lista = await consulta.OrderByDescending(x => x.Fecha).ThenBy(x => x.c.Id).Take(LimiteCompensaciones).Select(x => x.c).ToListAsync(ct);
        return Results.Ok(await CompensacionesDtoAsync(lista, usuario, permisos, soporte, plataforma, ct));
    }

    private static async Task<IResult> ReportePendientesAsync(
        [Description("Id del equipo del turno; vacío, todos.")] Guid? equipo,
        ClaimsPrincipal usuario, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var consulta = soporte.Compensaciones.AsNoTracking().Where(c => c.Estado == EstadoCompensacion.Pendiente)
            .Join(soporte.Turnos, c => c.TurnoId, t => t.Id, (c, t) => new { c, t.Fecha, t.EquipoId });
        if (equipo is { } q)
        {
            consulta = consulta.Where(x => x.EquipoId == q);
        }
        var pendientes = await consulta.OrderBy(x => x.Fecha).Select(x => x.c).Take(LimiteCompensaciones * 4).ToListAsync(ct);
        var dtos = await CompensacionesDtoAsync(pendientes, usuario, permisos, soporte, plataforma, ct);
        var hoy = CalendarioBogota.Hoy(reloj);
        var personas = dtos.GroupBy(d => d.Persona.Id).Select(g => new PendientesPersonaDto(
                g.First().Persona, g.Count(), g.Count(d => d.FechaCompensada is null),
                g.Where(d => d.FechaCompensada >= hoy).Select(d => d.FechaCompensada).Min(), [.. g]))
            .OrderByDescending(p => p.Pendientes).ThenBy(p => p.Persona.Nombre, StringComparer.CurrentCulture).ToList();
        return Results.Ok(new ReporteCompensacionesDto(equipo, dtos.Count, personas));
    }

    private static async Task<IResult> RegistrarCompensacionAsync(
        [Description("Turno, día compensado y nota.")] NuevaCompensacion nueva,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var nota = Limpiar(nueva.Nota);
        if (ValidarNota(nota) is { } malo)
        {
            return malo;
        }
        var turno = await soporte.Turnos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == nueva.TurnoId, ct);
        if (turno is null)
        {
            return NoEncontrado("turno_no_encontrado", "El turno no existe.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Aprobar, turno.EquipoId, ct))
        {
            return SinPermiso("No tienes atlas.soporte.aprobar sobre el equipo de este turno.");
        }
        if (!ReglasTurno.Vigentes.Contains(turno.Estado))
        {
            return Conflicto("turno_cerrado", $"El turno está {ReglasTurno.Texto(turno.Estado)}: la compensación es para quien lo hizo (el turno vigente).");
        }
        if (await soporte.Compensaciones.AnyAsync(c => c.TurnoId == turno.Id && c.PersonaId == turno.PersonaId, ct))
        {
            return Conflicto("compensacion_existente", "Ese turno ya tiene su compensación registrada.");
        }
        var compensacion = new Compensacion
        {
            TurnoId = turno.Id,
            PersonaId = turno.PersonaId,
            FechaCompensada = nueva.FechaCompensada,
            AprobadaPorPersonaId = PersonaDeSesion(http.User),
            Nota = nota,
        };
        soporte.Compensaciones.Add(compensacion);
        var cuando = nueva.FechaCompensada is { } f ? $" para el {Dia(f)}" : string.Empty;
        try
        {
            await GuardarAsync(soporte, plataforma,
                [Evento(http, reloj, "compensacion_registrada", "soporte_compensacion", compensacion.Id,
                    new { turnoId = turno.Id, personaId = turno.PersonaId, fecha = nueva.FechaCompensada })], ct,
                notificaciones: [Aviso(turno.PersonaId, "Compensación registrada",
                    $"Se registró una compensación{cuando} por tu turno del sábado {Dia(turno.Fecha)}.")]);
        }
        catch (DbUpdateException)
        {
            return Conflicto("compensacion_existente", "Ese turno ya tiene su compensación registrada.");
        }
        return Results.Created($"/api/v1/soporte/compensaciones/{compensacion.Id}",
            (await CompensacionesDtoAsync([compensacion], http.User, permisos, soporte, plataforma, ct))[0]);
    }

    private static async Task<IResult> CambiarCompensacionAsync(
        [Description("Id de la compensación.")] Guid id,
        [Description("Día compensado, estado y nota.")] CambioCompensacion cambio,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var nota = Limpiar(cambio.Nota);
        if (ValidarNota(nota) is { } malo)
        {
            return malo;
        }
        var estado = ReglasTurno.Leer<EstadoCompensacion>(cambio.Estado);
        if (estado is null)
        {
            return Invalido("estado", "El estado es pendiente o tomada.");
        }
        if (estado == EstadoCompensacion.Tomada && cambio.FechaCompensada is null)
        {
            return Invalido("fechaCompensada", "Para marcarla tomada, indica el día compensado.");
        }
        var compensacion = await soporte.Compensaciones.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (compensacion is null)
        {
            return NoEncontrado("compensacion_no_encontrada", "La compensación no existe.");
        }
        var equipo = await soporte.Turnos.Where(t => t.Id == compensacion.TurnoId).Select(t => t.EquipoId).FirstAsync(ct);
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Aprobar, equipo, ct))
        {
            return SinPermiso("No tienes atlas.soporte.aprobar sobre el equipo de este turno.");
        }
        compensacion.FechaCompensada = cambio.FechaCompensada;
        compensacion.Estado = estado.Value;
        compensacion.Nota = nota;
        compensacion.AprobadaPorPersonaId ??= PersonaDeSesion(http.User);
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "compensacion_cambiada", "soporte_compensacion", compensacion.Id,
                new { fecha = compensacion.FechaCompensada, estado = ReglasTurno.Texto(estado.Value) })], ct);
        return Results.Ok((await CompensacionesDtoAsync([compensacion], http.User, permisos, soporte, plataforma, ct))[0]);
    }
}
