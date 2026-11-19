using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Personas;

/// <summary>
/// Trabajo programado de la sincronización de personas (ATLAS-ADR-0030): corre cada día a la
/// <see cref="OpcionesSincronizacionPersonas.Hora"/> de Bogotá (con <see cref="TimeProvider"/>) y cuando un superadmin
/// pide «Sincronizar ahora». Si Graph o la base no responden, reintenta con esperas que se duplican; si se acaban los
/// intentos, queda para el día siguiente (o para el botón). Todas las réplicas lo tienen; el candado de
/// <see cref="SincronizadorPersonas"/> deja correr una, y la franja ya hecha por otra réplica no se repite.
/// </summary>
public sealed partial class TrabajoSincronizacionPersonas(
    IServiceScopeFactory alcances,
    IOptions<OpcionesSincronizacionPersonas> opciones,
    TimeProvider reloj,
    ILogger<TrabajoSincronizacionPersonas> log) : BackgroundService
{
    internal static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    private readonly SemaphoreSlim _ahora = new(0, 1);
    private int _enCurso;
    private Guid? _actor;

    /// <summary>Hay una corrida en curso en esta réplica (programada o pedida).</summary>
    public bool EnCurso => Volatile.Read(ref _enCurso) == 1;

    /// <summary>
    /// Pide una corrida ya, sin esperar a la hora programada. <c>false</c> si ya hay una pendiente o en curso en esta
    /// réplica (no se encolan dos).
    /// </summary>
    /// <param name="actor">Persona que la pidió; queda como actor en la auditoría.</param>
    public bool SolicitarAhora(Guid? actor)
    {
        if (EnCurso || _ahora.CurrentCount > 0)
        {
            return false;
        }
        try
        {
            _actor = actor;
            _ahora.Release();
            return true;
        }
        catch (SemaphoreFullException)
        {
            return false;
        }
    }

    /// <summary>La próxima vez que el reloj de Bogotá marque <paramref name="hora"/> después de <paramref name="ahora"/>.</summary>
    public static DateTimeOffset ProximaCorrida(DateTimeOffset ahora, TimeSpan hora)
    {
        var local = TimeZoneInfo.ConvertTime(ahora, Bogota);
        var hoy = new DateTimeOffset(local.Date + hora, local.Offset);
        return (hoy > ahora ? hoy : hoy.AddDays(1)).ToUniversalTime();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // El directorio es un cliente HTTP con tiempo de vida corto: se resuelve en un alcance, nunca se guarda aquí.
        await using (var alcance = alcances.CreateAsyncScope())
        {
            if (!alcance.ServiceProvider.GetRequiredService<IDirectorioEntra>().Disponible)
            {
                return;
            }
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var programada = opciones.Value.Habilitada;
            var proxima = ProximaCorrida(reloj.GetUtcNow(), opciones.Value.Hora);
            var espera = proxima - reloj.GetUtcNow();
            DisparoSincronizacion disparo;
            try
            {
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var pedida = _ahora.WaitAsync(limite.Token);
                var tareas = programada
                    ? new Task[] { pedida, Task.Delay(espera > TimeSpan.Zero ? espera : TimeSpan.Zero, reloj, limite.Token) }
                    : new Task[] { pedida };
                var primera = await Task.WhenAny(tareas).ConfigureAwait(false);
                await limite.CancelAsync().ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                // Si la señal llegó justo cuando vencía la espera, igual se consume para no correr dos veces.
                disparo = primera == pedida || pedida.Status == TaskStatus.RanToCompletion
                    ? DisparoSincronizacion.Manual
                    : DisparoSincronizacion.Programada;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            Interlocked.Exchange(ref _enCurso, 1);
            try
            {
                var actor = disparo == DisparoSincronizacion.Manual ? _actor : null;
                _actor = null;
                await CorrerConReintentosAsync(disparo, disparo == DisparoSincronizacion.Programada ? proxima : null, stoppingToken, actor)
                    .ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _enCurso, 0);
            }
        }
    }

    /// <summary>Una corrida con sus reintentos; devuelve el estado final, o <c>null</c> si otra réplica la hizo o la tiene.</summary>
    internal async Task<EstadoSincronizacion?> CorrerConReintentosAsync(
        DisparoSincronizacion disparo, DateTimeOffset? franja, CancellationToken ct, Guid? actor = null)
    {
        var config = opciones.Value;
        var espera = config.EsperaReintento;
        for (var intento = 0; ; intento++)
        {
            try
            {
                await using var alcance = alcances.CreateAsyncScope();
                var sincronizador = alcance.ServiceProvider.GetRequiredService<SincronizadorPersonas>();
                // La franja con un margen: la otra réplica pudo arrancar unos segundos antes por la deriva de los relojes.
                return await sincronizador.SincronizarAsync(disparo, actor, franja?.AddMinutes(-10), ct).ConfigureAwait(false);
            }
            catch (SincronizacionEnCursoException)
            {
                // Otra réplica la está corriendo: no hay que reintentar.
                EnOtraReplica(log);
                return null;
            }
            catch (Exception ex) when (EsTransitoria(ex, ct) && intento < config.Reintentos)
            {
                Reintento(log, ex.GetType().Name, intento + 1, config.Reintentos, espera);
                await Task.Delay(espera, reloj, ct).ConfigureAwait(false);
                espera *= 2;
            }
            catch (Exception ex) when (EsTransitoria(ex, ct))
            {
                SinIntentos(log, ex);
                return null;
            }
        }
    }

    /// <summary>Graph, la red, el token de aplicación (MSAL) o la base no respondieron: vale la pena reintentar.</summary>
    internal static bool EsTransitoria(Exception ex, CancellationToken ct) => ex switch
    {
        OperationCanceledException when ct.IsCancellationRequested => false,
        HttpRequestException or TimeoutException or DbException or Microsoft.Identity.Client.MsalException or OperationCanceledException => true,
        InvalidOperationException { InnerException: DbException or HttpRequestException } => true,
        Microsoft.EntityFrameworkCore.DbUpdateException => true,
        _ => false,
    };

    public override void Dispose()
    {
        _ahora.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sincronización de personas: otra réplica la tiene en curso")]
    private static partial void EnOtraReplica(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sincronización de personas: falló ({Error}); intento {Intento} de {Total}, se reintenta en {Espera}")]
    private static partial void Reintento(ILogger log, string error, int intento, int total, TimeSpan espera);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sincronización de personas: se acabaron los reintentos; queda para la próxima corrida")]
    private static partial void SinIntentos(ILogger log, Exception ex);
}
