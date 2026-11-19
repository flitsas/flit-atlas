using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Contratos.Sandbox;

/// <summary>
/// Trabajo periódico del sandbox (HU 4.7, ATLAS-ADR-0045): apaga y borra de Microcks los sandboxes cuya compartición venció,
/// se revocó o ya no permite el sandbox (o cuyo paquete se archivó), reimporta los que cambiaron de revisión, vuelve a
/// importar lo que Microcks perdió al reiniciarse y reintenta los borrados pendientes. Varias réplicas pueden correrlo a la
/// vez: cada paso es idempotente (importar el mismo nombre y versión reemplaza, borrar lo que no existe no hace nada). Sin
/// Microcks configurado, no hace nada.
/// </summary>
internal sealed partial class TrabajoSandbox(
    IServiceScopeFactory alcances, IOptions<OpcionesSandbox> opciones, TimeProvider reloj, ILogger<TrabajoSandbox> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Value.TrabajoHabilitado || !opciones.Value.Disponible)
        {
            return;
        }
        using var temporizador = new PeriodicTimer(opciones.Value.Intervalo, reloj);
        do
        {
            try
            {
                await using var alcance = alcances.CreateAsyncScope();
                await alcance.ServiceProvider.GetRequiredService<ServicioSandbox>().MantenerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // La base o Microcks no respondieron: se intenta en la vuelta siguiente; el proxy revisa la compartición en cada llamada.
                Fallo(log, error.GetType().Name);
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Contratos: la revisión de los sandboxes falló ({Error}); se reintenta en la siguiente vuelta")]
    private static partial void Fallo(ILogger log, string error);
}
