using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Recordatorios;

/// <summary>Sección <c>Soporte:Recordatorios</c> (HU 2.11). La hora y la anticipación están en la configuración del soporte.</summary>
public sealed class OpcionesRecordatorios
{
    public const string Seccion = "Soporte:Recordatorios";

    /// <summary>Apaga solo la corrida programada; «Enviar ahora» sigue funcionando.</summary>
    public bool Habilitados { get; set; } = true;

    /// <summary>Cada cuánto mira si ya es la hora del día (Bogotá) y si la corrida de hoy falta.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Trabajo programado de los recordatorios de soporte (HU 2.11): cada <see cref="OpcionesRecordatorios.Intervalo"/>
/// revisa si en Bogotá ya pasó la hora configurada (<see cref="ConfiguracionSoporte.HoraRecordatorios"/>) y si la
/// corrida de hoy falta; también corre cuando alguien pide «Enviar ahora». Con <see cref="TimeProvider"/>. Todas las
/// réplicas lo tienen: el candado de <see cref="RecordatoriosSoporte"/> deja correr una y la corrida del día queda
/// registrada. Si la réplica arranca después de la hora, recupera la del día.
/// </summary>
public sealed partial class TrabajoRecordatorios(
    IServiceScopeFactory alcances, IOptions<OpcionesRecordatorios> opciones, TimeProvider reloj, ILogger<TrabajoRecordatorios> log) : BackgroundService
{
    private readonly SemaphoreSlim _ahora = new(0, 1);
    private Guid? _actor;
    private DateOnly? _hechaHoy;
    private int _enCurso;
    private bool _primera = true;

    /// <summary>Hay una corrida en curso en esta réplica.</summary>
    public bool EnCurso => Volatile.Read(ref _enCurso) == 1;

    /// <summary>Pide una corrida ya. <c>false</c> si ya hay una pedida o en curso en esta réplica.</summary>
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var manual = false;
            try
            {
                manual = await EsperarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            Interlocked.Exchange(ref _enCurso, 1);
            try
            {
                if (manual)
                {
                    var actor = _actor;
                    _actor = null;
                    await CorrerAsync(DisparoRecordatorios.Manual, actor, stoppingToken).ConfigureAwait(false);
                }
                else if (opciones.Value.Habilitados)
                {
                    await CorrerSiEsLaHoraAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // La base no respondió: se intenta en la vuelta siguiente; lo ya enviado está marcado y no se repite.
                Fallo(log, error.GetType().Name);
            }
            finally
            {
                Interlocked.Exchange(ref _enCurso, 0);
            }
        }
    }

    /// <summary>La primera vuelta no espera (recupera la corrida del día tras un reinicio); devuelve si fue pedida a mano.</summary>
    private async Task<bool> EsperarAsync(CancellationToken ct)
    {
        if (_primera)
        {
            _primera = false;
            return false;
        }
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pedida = _ahora.WaitAsync(limite.Token);
        var primera = await Task.WhenAny(pedida, Task.Delay(opciones.Value.Intervalo, reloj, limite.Token)).ConfigureAwait(false);
        await limite.CancelAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // Si la señal llegó justo al vencer la espera, igual se consume para no perderla.
        return primera == pedida || pedida.Status == TaskStatus.RanToCompletion;
    }

    private async Task CorrerSiEsLaHoraAsync(CancellationToken ct)
    {
        var hoy = CalendarioBogota.Hoy(reloj);
        if (_hechaHoy == hoy)
        {
            return;
        }
        TimeOnly hora;
        await using (var alcance = alcances.CreateAsyncScope())
        {
            hora = (await ConfiguracionAsync(alcance.ServiceProvider.GetRequiredService<SoporteDbContext>(), ct).ConfigureAwait(false)).HoraRecordatorios;
        }
        var ahora = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), CalendarioBogota.Zona).DateTime);
        if (ahora < hora)
        {
            return;
        }
        var resultado = await CorrerAsync(DisparoRecordatorios.Programada, null, ct).ConfigureAwait(false);
        // Hasta mañana no se vuelve a consultar, salvo que otra réplica la tenga en curso (si se cae, esta la retoma).
        if (resultado is not null || await HechaHoyAsync(hoy, ct).ConfigureAwait(false))
        {
            _hechaHoy = hoy;
        }
    }

    private async Task<bool> HechaHoyAsync(DateOnly hoy, CancellationToken ct)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var soporte = alcance.ServiceProvider.GetRequiredService<SoporteDbContext>();
        return await soporte.CorridasRecordatorios
            .AnyAsync(c => c.Fecha == hoy && c.Disparo == DisparoRecordatorios.Programada && c.TerminadaEn != null, ct).ConfigureAwait(false);
    }

    private async Task<ResultadoRecordatorios?> CorrerAsync(DisparoRecordatorios disparo, Guid? actor, CancellationToken ct)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var resultado = await alcance.ServiceProvider.GetRequiredService<RecordatoriosSoporte>().CorrerAsync(disparo, actor, ct).ConfigureAwait(false);
        if (resultado is null && disparo == DisparoRecordatorios.Manual)
        {
            EnOtraReplica(log);
        }
        return resultado;
    }

    public override void Dispose()
    {
        _ahora.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Soporte: los recordatorios ya los está corriendo otra réplica")]
    private static partial void EnOtraReplica(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Soporte: la corrida de recordatorios falló ({Error}); se intenta en la vuelta siguiente")]
    private static partial void Fallo(ILogger log, string error);
}
