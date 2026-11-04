using System.ComponentModel;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;
using Flit.Atlas.Modulos.Soporte.Recordatorios;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

[Description("Canales de los recordatorios que tiene este ambiente.")]
public sealed record CanalesRecordatorioDto(
    [property: Description("Hay llaves VAPID: push puede llegar a los dispositivos suscritos.")] bool Push,
    [property: Description("Hay proveedor de correo (SMTP en qa).")] bool Correo,
    [property: Description("El canal de Teams del soporte está configurado (HU 2.10): el aviso del sábado al equipo sale por ahí. No hay aviso personal por Teams.")] bool Teams);

[Description("Una corrida del trabajo de recordatorios.")]
public sealed record CorridaRecordatoriosDto(
    [property: Description("Día de Bogotá.")] DateOnly Fecha,
    [property: Description("programada o manual.")] string Disparo,
    [property: Description("Cuándo empezó.")] DateTimeOffset IniciadaEn,
    [property: Description("Cuándo terminó; vacío si se cortó o sigue en curso.")] DateTimeOffset? TerminadaEn,
    [property: Description("Envíos que salieron (un recordatorio por canal).")] int Enviados,
    [property: Description("Envíos que fallaron; se reintentan en la corrida siguiente.")] int Fallidos,
    [property: Description("Canales que no se intentaron: preferencia apagada, sin dispositivos o canal sin configurar.")] int Omitidos);

[Description("Estado de los recordatorios de turno (HU 2.11).")]
public sealed record EstadoRecordatoriosDto(
    [property: Description("Si la corrida diaria está encendida en este ambiente (Soporte__Recordatorios__Habilitados).")] bool Programados,
    [property: Description("Hora (HH:mm, Bogotá) de la corrida diaria.")] string Hora,
    [property: Description("Días de anticipación del recordatorio a la persona; vacío, solo el del mismo sábado.")] int? DiasAnticipacion,
    [property: Description("Canales que tiene el ambiente.")] CanalesRecordatorioDto Canales,
    [property: Description("Si esta réplica tiene una corrida pedida o en curso.")] bool EnCurso,
    [property: Description("Las últimas corridas, de la más reciente a la más vieja.")] IReadOnlyList<CorridaRecordatoriosDto> Corridas);

/// <summary>
/// Recordatorios de turno (HU 2.11): el estado del trabajo diario y «Enviar ahora». Los recordatorios los manda
/// <see cref="TrabajoRecordatorios"/>; las preferencias de cada persona van en el centro de notificaciones
/// (<c>/api/v1/plataforma/notificaciones/preferencias</c>, tipo <c>soporte.recordatorio_turno</c>).
/// </summary>
internal static class EndpointsRecordatorios
{
    public const int CorridasListadas = 10;

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/recordatorios", ObtenerAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("ObtenerRecordatoriosSoporte")
            .WithSummary("Estado de los recordatorios de turno")
            .WithDescription(
                $"Hora y anticipación de los recordatorios, canales que tiene el ambiente y las últimas {CorridasListadas} corridas con sus " +
                "conteos (sin nombres). Requiere atlas.soporte.gestionar.")
            .Produces<EstadoRecordatoriosDto>();

        grupo.MapPost("/recordatorios/ejecucion", EjecutarAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithName("EjecutarRecordatoriosSoporte")
            .WithSummary("Enviar los recordatorios ahora")
            .WithDescription(
                "Pide una corrida ya, sin esperar a la hora del día: manda lo que falte de hoy (lo ya enviado no se repite). Responde " +
                "202 y corre en segundo plano; 409 si ya hay una pedida o en curso. Requiere atlas.soporte.gestionar con alcance global.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ObtenerAsync(
        SoporteDbContext soporte, TrabajoRecordatorios trabajo, IOptions<OpcionesRecordatorios> opciones, LlavesVapid llaves,
        IEnviadorCorreo correo, ColaAvisosTeams avisosTeams, CancellationToken ct)
    {
        var config = await ConfiguracionAsync(soporte, ct);
        var corridas = await soporte.CorridasRecordatorios.AsNoTracking()
            .OrderByDescending(c => c.CreadoEn).Take(CorridasListadas)
            .ToListAsync(ct);
        return Results.Ok(new EstadoRecordatoriosDto(
            opciones.Value.Habilitados,
            Hora(config.HoraRecordatorios)!,
            config.DiasAnticipacionRecordatorio,
            new CanalesRecordatorioDto(
                llaves.Habilitado,
                CatalogoNotificaciones.Disponible(RecordatoriosSoporte.Tipo, CanalNotificacion.Correo, correo),
                avisosTeams.Activa),
            trabajo.EnCurso,
            [.. corridas.Select(c => new CorridaRecordatoriosDto(
                c.Fecha, ReglasTurno.Texto(c.Disparo), c.CreadoEn, c.TerminadaEn, c.Enviados, c.Fallidos, c.Omitidos))]));
    }

    private static async Task<IResult> EjecutarAsync(HttpContext http, IPermisosDeSesion permisos, TrabajoRecordatorios trabajo, CancellationToken ct)
    {
        if (!await PuedeGlobalAsync(permisos, http.User, PermisosSoporte.Gestionar, ct))
        {
            return SinPermiso("Enviar los recordatorios exige atlas.soporte.gestionar sin alcance a un solo equipo.");
        }
        return trabajo.SolicitarAhora(PersonaDeSesion(http.User))
            ? Results.Accepted("/api/v1/soporte/recordatorios")
            : Conflicto("recordatorios_en_curso", "Ya hay una corrida de recordatorios pedida o en curso.");
    }
}
