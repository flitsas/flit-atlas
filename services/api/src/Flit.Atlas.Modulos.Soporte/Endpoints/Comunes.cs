using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

/// <summary>Piezas que comparten los endpoints de soporte: sesión, horas, personas, errores y auditoría.</summary>
internal static class Comunes
{
    public const string FormatoHora = "HH:mm";

    public static Guid PersonaDeSesion(ClaimsPrincipal usuario) =>
        Guid.Parse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona)!);

    public static Guid? OrganizacionDeSesion(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var id) ? id : null;

    public static string? Hora(TimeOnly? hora) => hora?.ToString(FormatoHora, CultureInfo.InvariantCulture);

    /// <summary>Lee una hora HH:mm; vacío es «sin hora». Devuelve falso si el texto no es una hora válida.</summary>
    public static bool LeerHora(string? texto, out TimeOnly? hora)
    {
        hora = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }
        if (TimeOnly.TryParseExact(texto.Trim(), FormatoHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor))
        {
            hora = valor;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Valida un horario (inicio y fin HH:mm, los dos o ninguno, fin posterior al inicio) y deja los errores con los
    /// nombres de campo que da <paramref name="campoInicio"/> y <paramref name="campoFin"/>.
    /// </summary>
    public static (TimeOnly? Inicio, TimeOnly? Fin) LeerHorario(
        string? inicio, string? fin, string campoInicio, string campoFin, Dictionary<string, string[]> errores)
    {
        if (!LeerHora(inicio, out var horaInicio))
        {
            errores[campoInicio] = ["La hora de inicio va en formato HH:mm (por ejemplo, 08:00)."];
        }
        if (!LeerHora(fin, out var horaFin))
        {
            errores[campoFin] = ["La hora de fin va en formato HH:mm (por ejemplo, 12:00)."];
        }
        if (errores.ContainsKey(campoInicio) || errores.ContainsKey(campoFin))
        {
            return (null, null);
        }
        if (horaInicio.HasValue != horaFin.HasValue)
        {
            errores[horaInicio.HasValue ? campoFin : campoInicio] = ["El horario lleva hora de inicio y de fin, o ninguna."];
        }
        else if (horaInicio >= horaFin)
        {
            errores[campoFin] = ["La hora de fin debe ser posterior a la de inicio."];
        }
        return (horaInicio, horaFin);
    }

    /// <summary>Recorta y deja vacío en lugar de espacios; <c>null</c> si excede el largo (el llamador lo informa).</summary>
    public static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    public static IResult Conflicto(string codigo, string mensaje) => Results.Conflict(new ErrorSoporte(codigo, mensaje));

    public static IResult NoEncontrado(string codigo, string mensaje) => Results.NotFound(new ErrorSoporte(codigo, mensaje));

    public static IResult Invalido(string campo, string mensaje) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [mensaje] });

    /// <summary>La configuración (una sola fila); si no existe, una en memoria con los valores por defecto (08:00–12:00, sin turno en festivos).</summary>
    public static async Task<ConfiguracionSoporte> ConfiguracionAsync(SoporteDbContext soporte, CancellationToken ct, bool seguimiento = false)
    {
        var consulta = seguimiento ? soporte.Configuraciones : soporte.Configuraciones.AsNoTracking();
        return await consulta.FirstOrDefaultAsync(ct) ?? new ConfiguracionSoporte();
    }

    /// <summary>Horario que reciben los turnos nuevos de un equipo: el propio o el global.</summary>
    public static (TimeOnly Inicio, TimeOnly Fin) HorarioDe(EquipoSoporte equipo, ConfiguracionSoporte configuracion) =>
        equipo.HoraInicio is { } inicio && equipo.HoraFin is { } fin ? (inicio, fin) : (configuracion.HoraInicioPorDefecto, configuracion.HoraFinPorDefecto);

    /// <summary>Tipo de alcance de las asignaciones de rol limitadas a un equipo (ATLAS-ADR-0023): <c>equipo_soporte:&lt;id&gt;</c>.</summary>
    public const string AlcanceEquipo = "equipo_soporte";

    public static AlcanceAsignacion Alcance(Guid equipoId) => new(AlcanceEquipo, equipoId.ToString("D"));

    /// <summary>¿Tiene el permiso sobre el equipo (asignación global o con alcance a ese equipo)?</summary>
    public static async Task<bool> PuedeEnEquipoAsync(IPermisosDeSesion permisos, ClaimsPrincipal usuario, string permiso, Guid equipoId, CancellationToken ct) =>
        (await permisos.ObtenerAsync(usuario, ct)).TieneEn(permiso, Alcance(equipoId));

    /// <summary>¿Tiene el permiso con alcance global? Lo exigen la configuración, los equipos nuevos, ausencias y festivos.</summary>
    public static async Task<bool> PuedeGlobalAsync(IPermisosDeSesion permisos, ClaimsPrincipal usuario, string permiso, CancellationToken ct) =>
        (await permisos.ObtenerAsync(usuario, ct)).TieneEn(permiso, AlcanceAsignacion.Global);

    public static IResult SinPermiso(string mensaje) =>
        Results.Json(new ErrorSoporte("sin_permiso_en_equipo", mensaje), statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Nombres de los equipos por id.</summary>
    public static Task<Dictionary<Guid, string>> NombresDeEquiposAsync(SoporteDbContext soporte, CancellationToken ct) =>
        soporte.Equipos.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Nombre, ct);

    /// <summary>Datos mínimos de personas por id, con el alcance de datos de la petición.</summary>
    public static async Task<Dictionary<Guid, Persona>> PersonasAsync(PlataformaDbContext plataforma, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        if (lista.Count == 0)
        {
            return [];
        }
        return await plataforma.Personas.AsNoTracking().Where(p => lista.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    public static string NombreDe(IReadOnlyDictionary<Guid, Persona> personas, Guid id) =>
        personas.TryGetValue(id, out var persona) ? persona.Nombre : "Persona no disponible";

    public static PersonaSoporteDto PersonaDto(IReadOnlyDictionary<Guid, Persona> personas, Guid id, bool verContacto) =>
        personas.TryGetValue(id, out var p)
            ? new PersonaSoporteDto(p.Id, p.Nombre, p.Correo, p.Cargo, verContacto ? p.Telefono : null)
            : new PersonaSoporteDto(id, "Persona no disponible", null, null, null);

    public static TurnoDto TurnoDto(
        Turno t, IReadOnlyDictionary<Guid, Persona> personas, bool verContacto, IReadOnlyDictionary<DateOnly, string> festivos,
        IReadOnlyDictionary<Guid, string> equipos) =>
        new(t.Id, new EquipoResumenDto(t.EquipoId, equipos.GetValueOrDefault(t.EquipoId) ?? "Equipo"), t.Fecha,
            PersonaDto(personas, t.PersonaId, verContacto), Hora(t.HoraInicio), Hora(t.HoraFin),
            ReglasTurno.Texto(t.Estado), ReglasTurno.Texto(t.Origen), t.TurnoReemplazadoId,
            festivos.TryGetValue(t.Fecha, out var festivo) ? festivo : null, t.Observacion, t.RequiereRevision);

    /// <summary>Festivos de ley y adicionales entre dos fechas (incluidas), por fecha.</summary>
    public static async Task<List<FestivoDto>> FestivosAsync(SoporteDbContext soporte, DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var deLey = FestivosColombia.Entre(desde, hasta)
            .Select(f => new FestivoDto(f.Fecha, f.Nombre, ReglasTurno.Texto(f.Tipo), f.Trasladado, null));
        var adicionales = (await soporte.FestivosAdicionales.AsNoTracking().Where(f => f.Fecha >= desde && f.Fecha <= hasta).ToListAsync(ct))
            .Select(f => new FestivoDto(f.Fecha, f.Nombre, "adicional", false, f.Id));
        return [.. deLey.Concat(adicionales).OrderBy(f => f.Fecha).ThenBy(f => f.Tipo == "adicional")];
    }

    /// <summary>El primer nombre de festivo por fecha (si una fecha tiene un festivo de ley y uno adicional, el de ley).</summary>
    public static Dictionary<DateOnly, string> PorFecha(IEnumerable<FestivoDto> festivos) =>
        festivos.GroupBy(f => f.Fecha).ToDictionary(g => g.Key, g => g.First().Nombre);

    /// <summary>Evento de auditoría de una acción de soporte hecha por la persona de la sesión.</summary>
    public static EventoAuditoria Evento(
        HttpContext http, TimeProvider reloj, string accion, string recursoTipo, Guid? recursoId, object? datos = null) => new()
    {
        OcurridoEn = reloj.GetUtcNow(),
        ActorTipo = TipoActor.Persona,
        ActorId = PersonaDeSesion(http.User).ToString(),
        OrganizacionId = OrganizacionDeSesion(http.User),
        Accion = $"{ModuloSoporte.IdModulo}.{accion}",
        RecursoTipo = recursoTipo,
        RecursoId = recursoId?.ToString(),
        Ip = http.Connection.RemoteIpAddress,
        // Solo ids, fechas y estados: nunca observaciones ni motivos de ausencia (pueden traer datos personales).
        Datos = datos is null ? null : JsonSerializer.Serialize(datos),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>
    /// Guarda los cambios de soporte, los eventos de auditoría y las notificaciones en la app (centro de notificaciones,
    /// HU 1.6) en <b>una sola transacción</b>: el contexto de la plataforma usa la conexión y la transacción del de
    /// soporte (misma base y mismo alcance de datos). <paramref name="antes"/> corre ya dentro de la transacción (p. ej.
    /// para crear personas en la plataforma antes que los turnos que las usan).
    /// </summary>
    public static async Task GuardarAsync(
        SoporteDbContext soporte, PlataformaDbContext plataforma, IEnumerable<EventoAuditoria> eventos, CancellationToken ct,
        Func<Task>? antes = null, IEnumerable<Notificacion>? notificaciones = null)
    {
        await using var transaccion = await soporte.Database.BeginTransactionAsync(ct);
        plataforma.Database.SetDbConnection(soporte.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        try
        {
            if (antes is not null)
            {
                await antes();
            }
            await soporte.SaveChangesAsync(ct);
            plataforma.Auditoria.AddRange(eventos);
            if (notificaciones is not null)
            {
                plataforma.Notificaciones.AddRange(notificaciones);
            }
            await plataforma.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        finally
        {
            await plataforma.Database.UseTransactionAsync(null, ct);
        }
        // Ya confirmado: el trabajo de Microsoft 365 puede ver los eventos y avisos pendientes (ATLAS-ADR-0033).
        await transaccion.DisposeAsync();
        soporte.DespertarSiConfirmado();
    }

    /// <summary>¿La excepción es la del índice único de un sábado con turno vigente?</summary>
    public static bool EsTurnoRepetido(DbUpdateException error) =>
        error.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == "ux_soporte_turno_vigente";
}
