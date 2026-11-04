using System.Diagnostics.Metrics;
using System.Globalization;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Microsoft365;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;

/// <summary>Cuánto hizo un ciclo.</summary>
public readonly record struct CicloM365(int Eventos, int Avisos, int Fallos, bool Omitido = false);

/// <summary>
/// Un ciclo de la sincronización con Microsoft 365 (HU 2.10, ATLAS-ADR-0033). Lleva el calendario compartido al estado
/// de cada turno pendiente y publica los avisos de Teams en cola. Es idempotente: el evento se calcula siempre desde el
/// turno actual (crear si falta, actualizar si existe, cancelar si ya no debe estar) y la creación lleva un
/// <c>transactionId</c>, así que repetir un ciclo no duplica nada. Un candado consultivo de PostgreSQL deja correr una
/// réplica a la vez; la columna <c>version</c> evita cerrar como hecho un turno que cambió mientras se llamaba a Graph.
/// </summary>
public sealed partial class SincronizadorM365(
    SoporteDbContext soporte,
    PlataformaDbContext plataforma,
    IAccesoGraph acceso,
    CalendarioGraph calendario,
    INotificadorTeams teams,
    IOptions<OpcionesM365Soporte> opciones,
    TimeProvider reloj,
    ILogger<SincronizadorM365> log)
{
    /// <summary>Llave del candado consultivo (<c>pg_try_advisory_lock</c>) de esta sincronización.</summary>
    internal const long Candado = 0x41544C4153_0033;

    internal static readonly Meter Medidor = new("Flit.Atlas.Soporte");

    private static readonly Counter<int> Operaciones = Medidor.CreateCounter<int>(
        "atlas.soporte.m365.operaciones", description: "Eventos del calendario y avisos de Teams sincronizados o fallidos.");

    /// <summary>El calendario compartido se sincroniza (hay buzón, está habilitado y hay Entra).</summary>
    public bool CalendarioActivo => opciones.Value.CalendarioConfigurado && acceso.Disponible;

    public async Task<CicloM365> SincronizarAsync(CancellationToken ct)
    {
        var config = opciones.Value;
        var calendarioActivo = CalendarioActivo;
        if (!calendarioActivo && !teams.Configurado(config.CanalTeams))
        {
            return new CicloM365(0, 0, 0, Omitido: true);
        }
        await soporte.Database.OpenConnectionAsync(ct);
        try
        {
            var tomado = await soporte.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({Candado}) AS \"Value\"").SingleAsync(ct);
            if (!tomado)
            {
                return new CicloM365(0, 0, 0, Omitido: true);
            }
            try
            {
                var (eventos, fallosEventos) = calendarioActivo ? await CalendarioAsync(config, ct) : (0, 0);
                var (avisos, fallosAvisos) = await AvisosAsync(config, ct);
                return new CicloM365(eventos, avisos, fallosEventos + fallosAvisos);
            }
            finally
            {
                await soporte.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({Candado})", CancellationToken.None);
            }
        }
        finally
        {
            await soporte.Database.CloseConnectionAsync();
        }
    }

    private async Task<(int Hechos, int Fallos)> CalendarioAsync(OpcionesM365Soporte config, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var lote = await soporte.EventosCalendario.AsNoTracking()
            .Where(e => e.Pendiente && e.ProximoIntentoEn <= ahora)
            .OrderBy(e => e.ProximoIntentoEn).ThenBy(e => e.Id)
            .Take(config.TamanoLote)
            .ToListAsync(ct);
        int hechos = 0, fallos = 0;
        foreach (var fila in lote)
        {
            try
            {
                var (eventoId, persona) = await SincronizarEventoAsync(config, fila, ct);
                var cerradas = await soporte.EventosCalendario.Where(e => e.Id == fila.Id && e.Version == fila.Version)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.Pendiente, false)
                        .SetProperty(e => e.EventoGraphId, eventoId)
                        .SetProperty(e => e.PersonaSincronizadaId, persona)
                        .SetProperty(e => e.Intentos, 0)
                        .SetProperty(e => e.UltimoError, (string?)null)
                        .SetProperty(e => e.SincronizadoEn, reloj.GetUtcNow())
                        .SetProperty(e => e.ActualizadoEn, reloj.GetUtcNow()), ct);
                if (cerradas == 0)
                {
                    // El turno cambió mientras se llamaba a Graph: se guarda el evento y queda pendiente para el siguiente ciclo.
                    await soporte.EventosCalendario.Where(e => e.Id == fila.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(e => e.EventoGraphId, eventoId)
                            .SetProperty(e => e.PersonaSincronizadaId, persona), ct);
                }
                hechos++;
                Operaciones.Add(1, new KeyValuePair<string, object?>("tipo", "evento"), new KeyValuePair<string, object?>("resultado", "ok"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Graph, la red o el token (MSAL) fallan: queda el error en la fila y se reintenta.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                fallos++;
                var intentos = fila.Intentos + 1;
                var espera = config.EsperaTras(intentos, (ex as ErrorGraphException)?.ReintentarEn);
                var error = Recortar(ex is ErrorGraphException ? ex.Message : ex.GetType().Name);
                await soporte.EventosCalendario.Where(e => e.Id == fila.Id && e.Version == fila.Version)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.Intentos, intentos)
                        .SetProperty(e => e.ProximoIntentoEn, reloj.GetUtcNow() + espera)
                        .SetProperty(e => e.UltimoError, error), CancellationToken.None);
                FalloEvento(log, fila.TurnoId, intentos, error);
                Operaciones.Add(1, new KeyValuePair<string, object?>("tipo", "evento"), new KeyValuePair<string, object?>("resultado", "fallo"));
            }
        }
        return (hechos, fallos);
    }

    /// <summary>Lleva el evento de un turno a lo que debe ser y devuelve el id y la persona con que quedó.</summary>
    private async Task<(string? EventoId, Guid? Persona)> SincronizarEventoAsync(OpcionesM365Soporte config, EventoCalendario fila, CancellationToken ct)
    {
        var buzon = config.Buzon!.Trim();
        // Un turno ya pasado no se toca (p. ej. cuando queda «aplicado» después del sábado): su evento queda como historia
        // y, si nunca tuvo, no se crea.
        var fecha = await soporte.Turnos.AsNoTracking().Where(t => t.Id == fila.TurnoId).Select(t => (DateOnly?)t.Fecha).FirstOrDefaultAsync(ct);
        if (fecha is { } dia && dia < CalendarioBogota.Hoy(reloj))
        {
            return (fila.EventoGraphId, fila.PersonaSincronizadaId);
        }
        var deseado = await DeseadoAsync(fila.TurnoId, ct);
        if (deseado is null)
        {
            if (fila.EventoGraphId is { } cancelar)
            {
                await calendario.CancelarAsync(buzon, cancelar, "El turno de soporte se canceló o cambió de persona en Flit Atlas.", ct);
            }
            return (null, null);
        }
        if (fila.EventoGraphId is { } existente)
        {
            if (fila.PersonaSincronizadaId == deseado.PersonaId)
            {
                if (await calendario.ActualizarAsync(buzon, existente, deseado, ct))
                {
                    return (existente, deseado.PersonaId);
                }
            }
            else
            {
                // Otra persona en el mismo turno: la anterior recibe la cancelación y la nueva, la invitación.
                await calendario.CancelarAsync(buzon, existente, "El turno de soporte pasó a otra persona en Flit Atlas.", ct);
            }
        }
        var id = await calendario.CrearAsync(buzon, deseado, $"atlas-soporte-{fila.TurnoId:N}-{fila.Version}", ct);
        return (id, deseado.PersonaId);
    }

    /// <summary>
    /// El evento que debe tener el turno, o <c>null</c> si no debe tener ninguno: turno borrado, reemplazado o cancelado,
    /// ya pasado (el historial importado no llena los calendarios) o de una persona sin buzón (inactiva o creada por el
    /// importador).
    /// </summary>
    internal async Task<EventoDeseado?> DeseadoAsync(Guid turnoId, CancellationToken ct)
    {
        var turno = await soporte.Turnos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == turnoId, ct);
        if (turno is null || !ReglasTurno.Vigentes.Contains(turno.Estado) || turno.Fecha < CalendarioBogota.Hoy(reloj))
        {
            return null;
        }
        var persona = await plataforma.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == turno.PersonaId, ct);
        if (persona is not { Tipo: TipoPersona.Empleado, Estado: EstadoPersona.Activa } || string.IsNullOrWhiteSpace(persona.Correo)
            || persona.Correo.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var equipo = await soporte.Equipos.AsNoTracking().FirstAsync(e => e.Id == turno.EquipoId, ct);
        var configuracion = await soporte.Configuraciones.AsNoTracking().FirstOrDefaultAsync(ct) ?? new ConfiguracionSoporte();
        var (inicioEquipo, finEquipo) = equipo.HoraInicio is { } i && equipo.HoraFin is { } f
            ? (i, f) : (configuracion.HoraInicioPorDefecto, configuracion.HoraFinPorDefecto);
        var inicio = turno.HoraInicio ?? inicioEquipo;
        var fin = turno.HoraFin ?? finEquipo;
        var horario = $"{inicio.ToString("HH:mm", CultureInfo.InvariantCulture)} a {fin.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        return new EventoDeseado(
            turno.Id,
            $"Soporte de los sábados: {equipo.Nombre}",
            $"Turno de soporte de los sábados del equipo {equipo.Nombre}, de {horario} (hora de Colombia). " +
            "Lo publica Flit Atlas: los cambios de turno se piden en Atlas y este evento se actualiza solo.",
            turno.Fecha, inicio, fin, persona.Id, persona.Correo, persona.Nombre);
    }

    private async Task<(int Hechos, int Fallos)> AvisosAsync(OpcionesM365Soporte config, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var lote = await soporte.AvisosTeams
            .Where(a => a.Pendiente && a.ProximoIntentoEn <= ahora)
            .OrderBy(a => a.ProximoIntentoEn).ThenBy(a => a.Id)
            .Take(config.TamanoLote)
            .ToListAsync(ct);
        int hechos = 0, fallos = 0;
        foreach (var aviso in lote)
        {
            if (!teams.Configurado(aviso.Canal) || ahora - aviso.CreadoEn > config.VigenciaAviso)
            {
                // Sin canal (se quitó la URL) o viejo: ya no se publica.
                aviso.Pendiente = false;
                aviso.UltimoError = teams.Configurado(aviso.Canal) ? "vencido" : "canal_sin_configurar";
            }
            else
            {
                try
                {
                    await teams.EnviarAsync(aviso.Canal, ColaAvisosTeams.Leer(aviso.Contenido), ct);
                    aviso.Pendiente = false;
                    aviso.EnviadoEn = reloj.GetUtcNow();
                    aviso.UltimoError = null;
                    hechos++;
                    Operaciones.Add(1, new KeyValuePair<string, object?>("tipo", "aviso_teams"), new KeyValuePair<string, object?>("resultado", "ok"));
                }
                catch (AvisoTeamsNoEnviadoException ex)
                {
                    fallos++;
                    aviso.Intentos++;
                    aviso.ProximoIntentoEn = reloj.GetUtcNow() + config.EsperaTras(aviso.Intentos, ex.ReintentarEn);
                    aviso.UltimoError = Recortar(ex.Message);
                    FalloAviso(log, aviso.Id, aviso.Intentos, aviso.UltimoError);
                    Operaciones.Add(1, new KeyValuePair<string, object?>("tipo", "aviso_teams"), new KeyValuePair<string, object?>("resultado", "fallo"));
                }
            }
            await soporte.SaveChangesAsync(CancellationToken.None);
        }
        return (hechos, fallos);
    }

    private static string Recortar(string texto) => texto.Length <= SoporteDbContext.LargoError ? texto : texto[..SoporteDbContext.LargoError];

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo sincronizar el evento de Outlook del turno {TurnoId} (intento {Intentos}): {Error}")]
    private static partial void FalloEvento(ILogger log, Guid turnoId, int intentos, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo publicar el aviso de Teams {AvisoId} (intento {Intentos}): {Error}")]
    private static partial void FalloAviso(ILogger log, Guid avisoId, int intentos, string error);
}

/// <summary>
/// Trabajo en segundo plano de Microsoft 365 del soporte (ATLAS-ADR-0033): cada <see cref="OpcionesM365Soporte.Intervalo"/>
/// (15 s por defecto, con el <see cref="TimeProvider"/> de la aplicación) o en cuanto una acción de esta réplica deja algo
/// pendiente, corre <see cref="SincronizadorM365"/>. Todas las réplicas lo tienen; el candado deja correr una. Si la
/// base falla, lo registra y sigue en el próximo intervalo.
/// </summary>
public sealed partial class TrabajoM365Soporte(
    IServiceScopeFactory alcances,
    DespertadorM365 despertador,
    IOptions<OpcionesM365Soporte> opciones,
    TimeProvider reloj,
    ILogger<TrabajoM365Soporte> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CicloAsync(stoppingToken).ConfigureAwait(false);
                await despertador.EsperarAsync(opciones.Value.Intervalo, reloj, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<CicloM365> CicloAsync(CancellationToken ct)
    {
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            return await alcance.ServiceProvider.GetRequiredService<SincronizadorM365>().SincronizarAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Un fallo de la base no debe matar el trabajo: se reintenta en el próximo intervalo.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorDeCiclo(log, ex);
            return new CicloM365(0, 0, 1);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló un ciclo de la sincronización con Microsoft 365 del soporte; se reintenta")]
    private static partial void ErrorDeCiclo(ILogger log, Exception ex);
}
