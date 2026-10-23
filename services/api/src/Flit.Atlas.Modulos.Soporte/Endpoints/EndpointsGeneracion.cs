using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

/// <summary>
/// Generación automática por equipo (HU 2.8): vista previa con <c>atlas.soporte.gestionar</c> y publicación con
/// <c>atlas.soporte.aprobar</c>, global o con alcance al equipo (aprueba quien tenga el permiso, sin aprobadores fijos).
/// La propuesta sale de <see cref="GeneradorRotacion"/>, que es determinista: la confirmación la recalcula y la compara
/// con la huella de la vista previa, así nada se publica distinto de lo que se vio.
/// <para>
/// Solo se regeneran los turnos <c>generado</c> + <c>programado</c> del equipo en la ventana; los aplicados,
/// confirmados, importados y manuales no cambian y cuentan para la equidad. Una persona con turno ese sábado en otro
/// equipo no se asigna (cuenta como no disponible).
/// </para>
/// </summary>
internal static class EndpointsGeneracion
{
    public const int MesesMaximos = 12;

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/generacion/vista-previa", PrevisualizarAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("PrevisualizarGeneracionSoporte")
            .WithSummary("Vista previa de la generación de un equipo")
            .WithDescription(
                $"Propone los turnos del equipo para los próximos meses (1 a {MesesMaximos}) en orden rotativo entre sus " +
                "participantes activos, con el número de personas por sábado del equipo, buscando el mismo número de turnos " +
                "por persona, saltando ausencias y turnos en otros equipos y aplicando la regla de festivos de Colombia. No " +
                "guarda nada. 409 si el equipo no tiene participantes activos. Requiere atlas.soporte.gestionar global o con " +
                "alcance al equipo.")
            .Produces<VistaPreviaDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        grupo.MapPost("/generacion/confirmar", ConfirmarAsync)
            .RequierePermiso(PermisosSoporte.Aprobar)
            .WithName("ConfirmarGeneracionSoporte")
            .WithSummary("Publicar la generación de un equipo")
            .WithDescription(
                "Publica la propuesta de la vista previa con el mismo equipo, la misma ventana y su huella: si algo cambió desde " +
                "entonces (turnos, participantes, ausencias, configuración o el día), responde 409 y hay que volver a generar " +
                "la vista previa. Los turnos aplicados no cambian. Queda en la auditoría. Requiere atlas.soporte.aprobar global " +
                "o con alcance al equipo.")
            .Produces<ResultadoGeneracionDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    /// <summary>Todo lo que hace falta para confirmar: la propuesta, los turnos generados que cambia y su huella.</summary>
    private sealed record Plan(
        EquipoSoporte Equipo, DateOnly Desde, DateOnly Hasta, ResultadoGeneracion Resultado, ConfiguracionSoporte Configuracion,
        IReadOnlyList<Turno> GeneradosAntes, IReadOnlyList<string> Avisos, string Huella, Dictionary<Guid, Persona> Personas);

    private static async Task<(Plan? Plan, IResult? Error)> PlanificarAsync(
        Guid equipoId, int meses, DateOnly? desde, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var equipo = await soporte.Equipos.AsNoTracking().FirstOrDefaultAsync(e => e.Id == equipoId, ct);
        if (equipo is null)
        {
            return (null, Invalido("equipoId", "El equipo no existe."));
        }
        if (meses is < 1 or > MesesMaximos)
        {
            return (null, Invalido("meses", $"La ventana va de 1 a {MesesMaximos} meses."));
        }
        var hoy = CalendarioBogota.Hoy(reloj);
        var inicio = desde ?? hoy.AddDays(1);
        if (inicio <= hoy)
        {
            return (null, Invalido("desde", "La generación empieza después de hoy: los turnos pasados no se generan."));
        }
        var fin = inicio.AddMonths(meses);
        var configuracion = await ConfiguracionAsync(soporte, ct);

        var participantes = await soporte.Participantes.AsNoTracking().Where(p => p.EquipoId == equipo.Id && p.Activo)
            .OrderBy(p => p.Orden).ThenBy(p => p.CreadoEn).ToListAsync(ct);
        var turnos = await EndpointsTurnos.TurnosVigentes(soporte).Where(t => t.Fecha >= inicio && t.Fecha < fin).ToListAsync(ct);
        var delEquipo = turnos.Where(t => t.EquipoId == equipo.Id).ToList();
        var personas = await PersonasAsync(plataforma, participantes.Select(p => p.PersonaId).Concat(delEquipo.Select(t => t.PersonaId)), ct);
        var avisos = new List<string>();
        var activos = new List<Guid>();
        foreach (var p in participantes)
        {
            if (personas.TryGetValue(p.PersonaId, out var persona) && persona.Estado == EstadoPersona.Activa)
            {
                activos.Add(p.PersonaId);
            }
            else
            {
                avisos.Add($"{NombreDe(personas, p.PersonaId)} no está activa en Atlas: no entra en la generación.");
            }
        }
        if (activos.Count == 0)
        {
            return (null, Conflicto("sin_participantes", $"El equipo {equipo.Nombre} no tiene participantes activos."));
        }

        var generadosAntes = delEquipo.Where(EsRegenerable).OrderBy(t => t.Fecha).ThenBy(t => t.Id).ToList();
        var fijos = delEquipo.Where(t => !EsRegenerable(t)).Select(t => new TurnoFijo(t.Fecha, t.PersonaId)).ToList();
        var ultima = await EndpointsTurnos.TurnosVigentes(soporte).Where(t => t.EquipoId == equipo.Id && t.Fecha < inicio)
            .OrderByDescending(t => t.Fecha).ThenByDescending(t => t.CreadoEn).Select(t => (Guid?)t.PersonaId).FirstOrDefaultAsync(ct);
        var ausencias = await soporte.Ausencias.AsNoTracking().Where(a => a.Hasta >= inicio && a.Desde < fin)
            .Select(a => new RangoAusencia(a.PersonaId, a.Desde, a.Hasta)).ToListAsync(ct);
        // Quien tiene turno ese sábado en otro equipo no está disponible para este.
        ausencias.AddRange(turnos.Where(t => t.EquipoId != equipo.Id).Select(t => new RangoAusencia(t.PersonaId, t.Fecha, t.Fecha)));
        var festivos = PorFecha(await FestivosAsync(soporte, inicio, fin.AddDays(-1), ct));

        var resultado = GeneradorRotacion.Generar(new EntradaGeneracion(
            inicio, fin, activos, fijos, ausencias, festivos, configuracion.ReglaFestivos, ultima, equipo.PersonasMinimo, equipo.PersonasMaximo));

        foreach (var s in resultado.Sabados)
        {
            if (s.Faltan > 0)
            {
                avisos.Add($"El sábado {s.Fecha:dd/MM/yyyy} faltan {s.Faltan} {(s.Faltan == 1 ? "persona" : "personas")} para el mínimo del equipo: complétalo a mano.");
            }
            if (s.AusenteEnFijo)
            {
                avisos.Add($"El sábado {s.Fecha:dd/MM/yyyy} hay un turno ya publicado de alguien con una ausencia ese día.");
            }
        }
        if (resultado.Desbalance > 1)
        {
            avisos.Add($"Quedan {resultado.Desbalance} turnos de diferencia entre personas por las ausencias y los turnos que no cambian.");
        }
        var huella = Huella(equipo, inicio, fin, configuracion, resultado, generadosAntes);
        return (new Plan(equipo, inicio, fin.AddDays(-1), resultado, configuracion, generadosAntes, avisos, huella, personas), null);
    }

    /// <summary>Un turno que la generación puede volver a proponer: lo creó ella y nadie lo ha confirmado.</summary>
    private static bool EsRegenerable(Turno t) => t.Origen == OrigenTurno.Generado && t.Estado == EstadoTurno.Programado;

    /// <summary>
    /// SHA-256 del equipo, la ventana, lo que afecta la propuesta, la propuesta y los turnos generados que cambiaría. No es
    /// un secreto: solo detecta que algo cambió entre la vista previa y la confirmación.
    /// </summary>
    private static string Huella(EquipoSoporte e, DateOnly desde, DateOnly hasta, ConfiguracionSoporte c, ResultadoGeneracion r, IEnumerable<Turno> generados)
    {
        var texto = new StringBuilder();
        var (inicio, fin) = HorarioDe(e, c);
        texto.Append(CultureInfo.InvariantCulture, $"{e.Id}|{e.PersonasMinimo}|{e.PersonasMaximo}|{desde:O}|{hasta:O}|{c.ReglaFestivos}|{Hora(inicio)}|{Hora(fin)}\n");
        foreach (var s in r.Sabados)
        {
            texto.Append(CultureInfo.InvariantCulture, $"{s.Fecha:O}|{s.Decision}|{string.Join(',', s.Fijas)}|{string.Join(',', s.Nuevas)}\n");
        }
        foreach (var t in generados)
        {
            texto.Append(CultureInfo.InvariantCulture, $"g|{t.Id}|{t.Fecha:O}|{t.PersonaId}\n");
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())));
    }

    /// <summary>
    /// Lo que hace confirmar: los generados programados de un sábado se conservan si su persona sigue propuesta y se
    /// quitan si no; las personas propuestas sin turno reciben uno nuevo.
    /// </summary>
    private static (List<(DateOnly Fecha, Guid Persona)> Crear, List<Turno> Quitar) Cambios(Plan p)
    {
        var crear = new List<(DateOnly, Guid)>();
        var quitar = new List<Turno>();
        var porFecha = p.GeneradosAntes.GroupBy(t => t.Fecha).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var s in p.Resultado.Sabados)
        {
            var antes = porFecha.GetValueOrDefault(s.Fecha) ?? [];
            quitar.AddRange(antes.Where(t => !s.Nuevas.Contains(t.PersonaId)));
            crear.AddRange(s.Nuevas.Where(n => antes.All(t => t.PersonaId != n)).Select(n => (s.Fecha, n)));
        }
        return (crear, quitar);
    }

    private static async Task<IResult> PrevisualizarAsync(
        [Description("Equipo y ventana de la generación.")] SolicitudGeneracion solicitud,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (solicitud.EquipoId != Guid.Empty && !await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, solicitud.EquipoId, ct))
        {
            return SinPermiso("No tienes atlas.soporte.gestionar sobre este equipo.");
        }
        var (plan, error) = await PlanificarAsync(solicitud.EquipoId, solicitud.Meses, solicitud.Desde, soporte, plataforma, reloj, ct);
        if (error is not null)
        {
            return error;
        }
        var p = plan!;
        var (crear, quitar) = Cambios(p);
        var porFecha = p.GeneradosAntes.GroupBy(t => t.Fecha).ToDictionary(g => g.Key, g => g.Select(t => t.PersonaId).ToHashSet());
        var sabados = p.Resultado.Sabados.Select(s =>
        {
            var antes = porFecha.GetValueOrDefault(s.Fecha) ?? [];
            return new SabadoPreviaDto(
                s.Fecha, ReglasTurno.Texto(s.Decision),
                [.. s.Fijas.Select(f => new PersonaPreviaDto(f, NombreDe(p.Personas, f), false)),
                 .. s.Nuevas.Select(n => new PersonaPreviaDto(n, NombreDe(p.Personas, n), true))],
                s.Festivo, !antes.SetEquals(s.Nuevas), s.AusenteEnFijo, s.Faltan);
        }).ToList();
        var conteo = p.Resultado.TurnosPorPersona
            .Select(kv => new ConteoPersonaDto(kv.Key, NombreDe(p.Personas, kv.Key), kv.Value))
            .OrderByDescending(c => c.Turnos).ThenBy(c => c.Nombre, StringComparer.CurrentCulture).ToList();
        return Results.Ok(new VistaPreviaDto(new EquipoResumenDto(p.Equipo.Id, p.Equipo.Nombre), p.Desde, p.Hasta, p.Huella, sabados, conteo,
            p.Resultado.Desbalance, crear.Count, quitar.Count, p.Avisos));
    }

    private static async Task<IResult> ConfirmarAsync(
        [Description("Equipo, ventana y huella de la vista previa que se publica.")] ConfirmacionGeneracion confirmacion,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(confirmacion.Huella))
        {
            return Invalido("huella", "Envía la huella de la vista previa que confirmas.");
        }
        if (confirmacion.EquipoId != Guid.Empty && !await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Aprobar, confirmacion.EquipoId, ct))
        {
            return SinPermiso("No tienes atlas.soporte.aprobar sobre este equipo.");
        }
        var (plan, error) = await PlanificarAsync(confirmacion.EquipoId, confirmacion.Meses, confirmacion.Desde, soporte, plataforma, reloj, ct);
        if (error is not null)
        {
            return error;
        }
        var p = plan!;
        if (!string.Equals(p.Huella, confirmacion.Huella.Trim(), StringComparison.Ordinal))
        {
            return Conflicto("vista_previa_vencida", "Algo cambió desde la vista previa: vuelve a generarla y revísala antes de publicar.");
        }

        var generacion = Guid.CreateVersion7();
        var persona = PersonaDeSesion(http.User);
        var (horaInicio, horaFin) = HorarioDe(p.Equipo, p.Configuracion);
        var (crear, quitar) = Cambios(p);
        var idsQuitar = quitar.Select(t => t.Id).ToList();
        soporte.Turnos.RemoveRange(await soporte.Turnos.Where(t => idsQuitar.Contains(t.Id)).ToListAsync(ct));
        soporte.Turnos.AddRange(crear.Select(c => new Turno
        {
            EquipoId = p.Equipo.Id,
            Fecha = c.Fecha,
            PersonaId = c.Persona,
            Origen = OrigenTurno.Generado,
            HoraInicio = horaInicio,
            HoraFin = horaFin,
            GeneracionId = generacion,
            CreadoPorPersonaId = persona,
        }));
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, "generacion_confirmada", "soporte_generacion", generacion,
                new { equipoId = p.Equipo.Id, desde = p.Desde, hasta = p.Hasta, creados = crear.Count, eliminados = quitar.Count, huella = p.Huella })], ct);
        return Results.Ok(new ResultadoGeneracionDto(generacion, crear.Count, quitar.Count));
    }
}
