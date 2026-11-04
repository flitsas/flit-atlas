using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Contratos.Deprecacion;

/// <summary>
/// Pide una corrida de avisos para una versión recién deprecada (HU 4.8): el endpoint no envía en la petición (§5.1: lo
/// que tarda va fuera de ella); el trabajo la atiende en cuanto puede. Con varias réplicas, la atiende la que recibió la
/// petición; si se cae antes, la corrida programada la cubre (la marca de cada envío evita repetirlo).
/// </summary>
internal sealed class DisparadorAvisos
{
    private readonly Channel<Guid> _pedidos = Channel.CreateBounded<Guid>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Pedir(Guid version) => _pedidos.Writer.TryWrite(version);

    public ChannelReader<Guid> Pedidos => _pedidos.Reader;
}

/// <summary>
/// Trabajo de los avisos de deprecación (HU 4.8, ATLAS-ADR-0046): cada <see cref="OpcionesAvisosDeprecacion.Intervalo"/>
/// revisa todas las versiones deprecadas (recordatorios y personas nuevas en una versión ya deprecada) y, entre vueltas,
/// atiende lo que pide <see cref="DisparadorAvisos"/> al deprecar.
/// </summary>
internal sealed partial class TrabajoAvisosDeprecacion(
    IServiceScopeFactory alcances, DisparadorAvisos disparador, IOptions<OpcionesAvisosDeprecacion> opciones, TimeProvider reloj,
    ILogger<TrabajoAvisosDeprecacion> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Value.Habilitados)
        {
            return;
        }
        var siguiente = reloj.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            if (reloj.GetUtcNow() >= siguiente)
            {
                await CorrerAsync(null, stoppingToken).ConfigureAwait(false);
                siguiente = reloj.GetUtcNow() + opciones.Value.Intervalo;
            }
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            espera.CancelAfter(Max(siguiente - reloj.GetUtcNow(), TimeSpan.FromSeconds(1)));
            try
            {
                var version = await disparador.Pedidos.ReadAsync(espera.Token).ConfigureAwait(false);
                await CorrerAsync(version, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Se cumplió el intervalo: la vuelta siguiente revisa todo.
            }
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private async Task CorrerAsync(Guid? version, CancellationToken ct)
    {
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            var resultado = await alcance.ServiceProvider.GetRequiredService<AvisosDeprecacion>().CorrerAsync(version, ct).ConfigureAwait(false);
            if (resultado is null && version is { } pendiente)
            {
                // Otra réplica tenía el candado: lo cubre la vuelta programada.
                Ocupado(log, pendiente);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // La base, el push o el correo no respondieron: se reintenta en la vuelta siguiente.
            Fallo(log, error.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Contratos: otra réplica corre los avisos de deprecación; la versión {Version} queda para la vuelta programada")]
    private static partial void Ocupado(ILogger log, Guid version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Contratos: los avisos de deprecación fallaron ({Error}); se reintenta en la siguiente vuelta")]
    private static partial void Fallo(ILogger log, string error);
}
