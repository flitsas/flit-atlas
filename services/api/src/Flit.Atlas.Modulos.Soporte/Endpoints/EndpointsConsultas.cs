using System.ComponentModel;
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

/// <summary>Ausencias, festivos, indicadores y el canal para externos.</summary>
internal static class EndpointsConsultas
{
    /// <summary>Una ausencia dura a lo sumo un año (una más larga es un retiro: se desactiva al participante).</summary>
    public const int DiasMaximosAusencia = 366;

    private const string SoloGlobal = "Esta acción exige atlas.soporte.gestionar sin alcance a un solo equipo.";

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/ausencias", ListarAusenciasAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("ListarAusenciasSoporte")
            .WithSummary("Ausencias de la rotación")
            .WithDescription(
                "Vacaciones, incapacidades y otras ausencias que terminan desde la fecha dada (por defecto, hoy). El motivo es " +
                "un dato sensible: solo lo ve quien gestiona el soporte. Requiere atlas.soporte.gestionar.")
            .Produces<List<AusenciaDto>>();

        grupo.MapPost("/ausencias", CrearAusenciaAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CrearAusenciaSoporte")
            .WithSummary("Registrar una ausencia")
            .WithDescription(
                $"Ausencia de una persona de la rotación entre dos fechas (incluidas, máximo {DiasMaximosAusencia} días). La " +
                "generación no la asigna esos días; los turnos ya creados no cambian solos. Vale en todos los equipos de la persona. Requiere atlas.soporte.gestionar global o sobre un equipo de la persona.")
            .Produces<AusenciaDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        grupo.MapDelete("/ausencias/{id:guid}", EliminarAusenciaAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("EliminarAusenciaSoporte")
            .WithSummary("Quitar una ausencia")
            .WithDescription("Borra una ausencia registrada por error o cancelada. Requiere atlas.soporte.gestionar global o sobre un equipo de la persona.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound);

        grupo.MapGet("/festivos", ListarFestivosAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ListarFestivosSoporte")
            .WithSummary("Festivos del año")
            .WithDescription(
                "Los festivos de Colombia del año, calculados en Atlas con la Ley 51 de 1983 y, desde 2026, la Ley 2578 (Pascua por algoritmo, fijos, " +
                "trasladables al lunes y relativos a Pascua), más los días adicionales registrados. Sin año, el actual. " +
                "Requiere atlas.soporte.ver.")
            .Produces<List<FestivoDto>>()
            .ProducesValidationProblem();

        grupo.MapPost("/festivos", CrearFestivoAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("CrearFestivoSoporte")
            .WithSummary("Agregar un día no laborable")
            .WithDescription(
                "Día adicional (p. ej. un día cívico decretado) que la generación trata como festivo. 409 si ya existe un día " +
                "adicional en esa fecha. Requiere atlas.soporte.gestionar con alcance global.")
            .Produces<FestivoDto>(StatusCodes.Status201Created)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapDelete("/festivos/{id:guid}", EliminarFestivoAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("EliminarFestivoSoporte")
            .WithSummary("Quitar un día no laborable")
            .WithDescription("Borra un día adicional; los festivos de ley no se pueden quitar. Requiere atlas.soporte.gestionar con alcance global.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound);

        grupo.MapGet("/indicadores", IndicadoresAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ObtenerIndicadoresSoporte")
            .WithSummary("Indicadores del soporte")
            .WithDescription(
                "Turnos por persona, reemplazos, compensaciones pendientes y cumplimiento (aplicados sobre los turnos ya " +
                "pasados) entre dos fechas; sin fechas, el año en curso. Requiere atlas.soporte.ver.")
            .Produces<IndicadoresDto>()
            .ProducesValidationProblem();

        grupo.MapGet("/canal", CanalAsync)
            .RequierePermiso(PermisosSoporte.VerCanal)
            .WithName("ObtenerCanalSoporte")
            .WithSummary("Canal de soporte del sábado")
            .WithDescription(
                "Lo único del soporte que ve un externo: el canal configurado y el horario del próximo sábado, " +
                "sin nombres ni datos de personas. Sin canal configurado, configurado es falso. Requiere atlas.soporte.ver_canal.")
            .Produces<CanalSoporteDto>();
    }

    // --- Ausencias ---

    private static async Task<IResult> ListarAusenciasAsync(
        [Description("Incluye las ausencias que terminan desde este día (aaaa-mm-dd); vacío, hoy.")] DateOnly? desde,
        SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var dia = desde ?? CalendarioBogota.Hoy(reloj);
        var ausencias = await soporte.Ausencias.AsNoTracking().Where(a => a.Hasta >= dia).OrderBy(a => a.Desde).ToListAsync(ct);
        var personas = await PersonasAsync(plataforma, ausencias.Select(a => a.PersonaId), ct);
        return Results.Ok(ausencias.Select(a => Dto(a, NombreDe(personas, a.PersonaId))).ToList());
    }

    private static AusenciaDto Dto(Ausencia a, string nombre) => new(a.Id, a.PersonaId, nombre, a.Desde, a.Hasta, ReglasTurno.Texto(a.Tipo));

    /// <summary>¿Gestiona (global o con alcance) al menos un equipo en el que participa la persona?</summary>
    private static async Task<bool> GestionaAlgunEquipoDeAsync(HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, Guid personaId, CancellationToken ct)
    {
        var efectivos = await permisos.ObtenerAsync(http.User, ct);
        var equipos = await soporte.Participantes.Where(p => p.PersonaId == personaId).Select(p => p.EquipoId).ToListAsync(ct);
        return efectivos.TieneEn(PermisosSoporte.Gestionar, AlcanceAsignacion.Global)
            || equipos.Any(e => efectivos.TieneEn(PermisosSoporte.Gestionar, Alcance(e)));
    }

    private static async Task<IResult> CrearAusenciaAsync(
        [Description("Persona, fechas y motivo de la ausencia.")] NuevaAusencia nueva,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        var tipo = ReglasTurno.Leer<TipoAusencia>(nueva.Tipo);
        if (tipo is null)
        {
            errores["tipo"] = [$"El motivo es uno de: {ReglasTurno.Valores<TipoAusencia>()}."];
        }
        if (nueva.Desde == default || nueva.Hasta == default)
        {
            errores["desde"] = ["La ausencia lleva fecha de inicio y de fin."];
        }
        else if (nueva.Hasta < nueva.Desde)
        {
            errores["hasta"] = ["La fecha final no puede ser anterior a la inicial."];
        }
        else if (nueva.Hasta.DayNumber - nueva.Desde.DayNumber >= DiasMaximosAusencia)
        {
            errores["hasta"] = [$"Una ausencia dura máximo {DiasMaximosAusencia} días; para un retiro, desactiva al participante."];
        }
        if (!await soporte.Participantes.AnyAsync(p => p.PersonaId == nueva.PersonaId, ct))
        {
            errores["personaId"] = ["La persona debe estar en la rotación de algún equipo."];
        }
        else if (!await GestionaAlgunEquipoDeAsync(http, permisos, soporte, nueva.PersonaId, ct))
        {
            return SinPermiso("No gestionas ningún equipo de esta persona.");
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var ausencia = new Ausencia
        {
            PersonaId = nueva.PersonaId,
            Desde = nueva.Desde,
            Hasta = nueva.Hasta,
            Tipo = tipo!.Value,
            RegistradaPorPersonaId = PersonaDeSesion(http.User),
        };
        soporte.Ausencias.Add(ausencia);
        // La auditoría no guarda el motivo (una incapacidad es un dato de salud): solo persona y fechas.
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "ausencia_registrada", "soporte_ausencia", ausencia.Id, new { personaId = ausencia.PersonaId, desde = ausencia.Desde, hasta = ausencia.Hasta })], ct);
        var personas = await PersonasAsync(plataforma, [ausencia.PersonaId], ct);
        return Results.Created($"/api/v1/soporte/ausencias/{ausencia.Id}", Dto(ausencia, NombreDe(personas, ausencia.PersonaId)));
    }

    private static async Task<IResult> EliminarAusenciaAsync(
        [Description("Id de la ausencia.")] Guid id,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var ausencia = await soporte.Ausencias.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ausencia is null)
        {
            return NoEncontrado("ausencia_no_encontrada", "La ausencia no existe.");
        }
        if (!await GestionaAlgunEquipoDeAsync(http, permisos, soporte, ausencia.PersonaId, ct))
        {
            return SinPermiso("No gestionas ningún equipo de esta persona.");
        }
        soporte.Ausencias.Remove(ausencia);
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "ausencia_eliminada", "soporte_ausencia", ausencia.Id, new { personaId = ausencia.PersonaId })], ct);
        return Results.NoContent();
    }

    // --- Festivos ---

    private static async Task<IResult> ListarFestivosAsync(
        [Description("Año (1984 a 9999); vacío, el actual.")] int? anio,
        SoporteDbContext soporte, TimeProvider reloj, CancellationToken ct)
    {
        var valor = anio ?? CalendarioBogota.Hoy(reloj).Year;
        if (valor is < FestivosColombia.AnioMinimo or > FestivosColombia.AnioMaximo)
        {
            return Invalido("anio", $"El año va de {FestivosColombia.AnioMinimo} (Ley 51 de 1983) a {FestivosColombia.AnioMaximo}.");
        }
        return Results.Ok(await FestivosAsync(soporte, new DateOnly(valor, 1, 1), new DateOnly(valor, 12, 31), ct));
    }

    private static async Task<IResult> CrearFestivoAsync(
        [Description("Fecha y nombre del día no laborable.")] NuevoFestivo nuevo,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await PuedeGlobalAsync(permisos, http.User, PermisosSoporte.Gestionar, ct))
        {
            return SinPermiso(SoloGlobal);
        }
        var nombre = Limpiar(nuevo.Nombre);
        var errores = new Dictionary<string, string[]>();
        if (nuevo.Fecha.Year is < FestivosColombia.AnioMinimo or > FestivosColombia.AnioMaximo)
        {
            errores["fecha"] = ["La fecha no es válida."];
        }
        if (nombre is null or { Length: > SoporteDbContext.LargoNombreFestivo })
        {
            errores["nombre"] = [$"El nombre es obligatorio (hasta {SoporteDbContext.LargoNombreFestivo} caracteres)."];
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        if (await soporte.FestivosAdicionales.AnyAsync(f => f.Fecha == nuevo.Fecha, ct))
        {
            return Conflicto("festivo_existente", "Ya hay un día adicional en esa fecha.");
        }
        var festivo = new FestivoAdicional { Fecha = nuevo.Fecha, Nombre = nombre! };
        soporte.FestivosAdicionales.Add(festivo);
        try
        {
            await GuardarAsync(soporte, plataforma,
                [Evento(http, reloj, "festivo_agregado", "soporte_festivo", festivo.Id, new { fecha = festivo.Fecha })], ct);
        }
        catch (DbUpdateException)
        {
            return Conflicto("festivo_existente", "Ya hay un día adicional en esa fecha.");
        }
        return Results.Created($"/api/v1/soporte/festivos/{festivo.Id}", new FestivoDto(festivo.Fecha, festivo.Nombre, "adicional", false, festivo.Id));
    }

    private static async Task<IResult> EliminarFestivoAsync(
        [Description("Id del día adicional.")] Guid id,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await PuedeGlobalAsync(permisos, http.User, PermisosSoporte.Gestionar, ct))
        {
            return SinPermiso(SoloGlobal);
        }
        var festivo = await soporte.FestivosAdicionales.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (festivo is null)
        {
            return NoEncontrado("festivo_no_encontrado", "El día adicional no existe.");
        }
        soporte.FestivosAdicionales.Remove(festivo);
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "festivo_eliminado", "soporte_festivo", festivo.Id, new { fecha = festivo.Fecha })], ct);
        return Results.NoContent();
    }

    // --- Indicadores ---

    private static async Task<IResult> IndicadoresAsync(
        [Description("Primer día (aaaa-mm-dd); vacío, el 1 de enero del año en curso.")] DateOnly? desde,
        [Description("Último día, incluido (aaaa-mm-dd); vacío, el 31 de diciembre del año en curso.")] DateOnly? hasta,
        [Description("Id del equipo; vacío, todos.")] Guid? equipo,
        SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var hoy = CalendarioBogota.Hoy(reloj);
        var inicio = desde ?? new DateOnly(hoy.Year, 1, 1);
        var fin = hasta ?? new DateOnly(hoy.Year, 12, 31);
        if (fin < inicio || fin.DayNumber - inicio.DayNumber > 3 * 366)
        {
            return Invalido("hasta", "El rango va de una fecha a otra posterior, máximo tres años.");
        }
        var consulta = soporte.Turnos.AsNoTracking().Where(t => t.Fecha >= inicio && t.Fecha <= fin);
        if (equipo is { } e)
        {
            consulta = consulta.Where(t => t.EquipoId == e);
        }
        var turnos = await consulta.ToListAsync(ct);
        var pendientes = await soporte.Compensaciones.AsNoTracking().Where(c => c.Estado == EstadoCompensacion.Pendiente)
            .GroupBy(c => c.PersonaId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Total, ct);
        var personas = await PersonasAsync(plataforma, turnos.Select(t => t.PersonaId).Concat(pendientes.Keys), ct);

        // Con equipo, solo sus personas; sin él, también quien solo tenga compensaciones pendientes.
        var filas = turnos.Select(t => t.PersonaId).Concat(equipo is null ? pendientes.Keys : []).Distinct().Select(id =>
        {
            var suyos = turnos.Where(t => t.PersonaId == id).ToList();
            var vigentes = suyos.Count(t => ReglasTurno.Vigentes.Contains(t.Estado));
            var aplicados = suyos.Count(t => t.Estado == EstadoTurno.Aplicado);
            var pasados = suyos.Count(t => t.Fecha < hoy && ReglasTurno.Vigentes.Contains(t.Estado));
            return new IndicadorPersonaDto(
                id, NombreDe(personas, id), vigentes, aplicados,
                suyos.Count(t => t.Estado == EstadoTurno.Reemplazado),
                suyos.Count(t => t.TurnoReemplazadoId.HasValue),
                suyos.Count(t => t.Estado == EstadoTurno.Cancelado),
                pendientes.GetValueOrDefault(id),
                pasados == 0 ? null : Math.Round((double)suyos.Count(t => t.Fecha < hoy && t.Estado == EstadoTurno.Aplicado) / pasados, 4));
        }).OrderByDescending(f => f.Turnos).ThenBy(f => f.Nombre, StringComparer.CurrentCulture).ToList();

        var pasadosTotal = turnos.Count(t => t.Fecha < hoy && ReglasTurno.Vigentes.Contains(t.Estado));
        return Results.Ok(new IndicadoresDto(inicio, fin, equipo, filas,
            turnos.Count(t => ReglasTurno.Vigentes.Contains(t.Estado)),
            turnos.Count(t => t.TurnoReemplazadoId.HasValue),
            pendientes.Values.Sum(),
            pasadosTotal == 0 ? null : Math.Round((double)turnos.Count(t => t.Fecha < hoy && t.Estado == EstadoTurno.Aplicado) / pasadosTotal, 4)));
    }

    // --- Canal para externos ---

    private static async Task<IResult> CanalAsync(SoporteDbContext soporte, TimeProvider reloj, CancellationToken ct)
    {
        var sabado = CalendarioBogota.SabadoDesde(CalendarioBogota.Hoy(reloj));
        var configuracion = await ConfiguracionAsync(soporte, ct);
        if (configuracion.CanalExterno is null)
        {
            // Sin canal configurado no se dice nada más: ni siquiera si hay soporte.
            return Results.Ok(new CanalSoporteDto(sabado, false, null, null, null, false));
        }
        // Sin nombres ni equipos: solo si hay soporte ese sábado y desde y hasta qué hora (el horario más amplio).
        var horarios = await EndpointsTurnos.TurnosVigentes(soporte).Where(t => t.Fecha == sabado)
            .Select(t => new { t.HoraInicio, t.HoraFin }).ToListAsync(ct);
        var inicio = horarios.Where(h => h.HoraInicio.HasValue).Select(h => h.HoraInicio).Min() ?? configuracion.HoraInicioPorDefecto;
        var fin = horarios.Where(h => h.HoraFin.HasValue).Select(h => h.HoraFin).Max() ?? configuracion.HoraFinPorDefecto;
        return Results.Ok(new CanalSoporteDto(sabado, true, configuracion.CanalExterno, Hora(inicio), Hora(fin), horarios.Count > 0));
    }
}
