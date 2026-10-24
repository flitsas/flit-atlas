using System.Threading.Channels;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Correo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>Un correo de la identidad por enviar y a quién atribuir el evento si no sale.</summary>
/// <param name="Motivo">Detalle del evento <c>correo_no_enviado</c> (<c>recuperacion</c>, <c>aviso_recuperacion</c>…).</param>
public sealed record CorreoPendiente(MensajeCorreo Mensaje, DatosEventoSeguridad Datos, string Motivo);

/// <summary>
/// Cola <b>en memoria</b> de los correos que la identidad envía fuera de la petición (F4-SEC-04, ATLAS-ADR-0049): el
/// enlace de recuperación y los avisos de seguridad. Así la respuesta de <c>POST /recuperacion</c> tarda lo mismo exista o
/// no la cuenta. No se usa la outbox de la base a propósito: el enlace de un solo uso nunca se persiste (ATLAS-ADR-0025);
/// si la réplica se cae antes de enviarlo, la persona lo pide otra vez. La atiende <see cref="TrabajoCorreosIdentidad"/>.
/// </summary>
public sealed class ColaCorreosIdentidad
{
    private readonly Channel<CorreoPendiente> _pendientes = Channel.CreateBounded<CorreoPendiente>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private int _enCurso;

    /// <summary>Correos encolados que aún no terminan de enviarse (o de fallar). Lo usan las pruebas para esperar.</summary>
    public int Pendientes => Volatile.Read(ref _enCurso);

    /// <summary><c>false</c> si la cola está llena (un ataque masivo): el correo se descarta y quien llama lo registra.</summary>
    public bool Encolar(CorreoPendiente correo)
    {
        Interlocked.Increment(ref _enCurso);
        if (_pendientes.Writer.TryWrite(correo))
        {
            return true;
        }
        Interlocked.Decrement(ref _enCurso);
        return false;
    }

    internal ChannelReader<CorreoPendiente> Lector => _pendientes.Reader;

    internal void Terminado() => Interlocked.Decrement(ref _enCurso);
}

/// <summary>Envía los correos de <see cref="ColaCorreosIdentidad"/>; un fallo queda como <c>identidad.correo_no_enviado</c>.</summary>
internal sealed partial class TrabajoCorreosIdentidad(
    ColaCorreosIdentidad cola, IServiceScopeFactory alcances, ILogger<TrabajoCorreosIdentidad> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var correo in cola.Lector.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var alcance = alcances.CreateAsyncScope();
                var enviado = await alcance.ServiceProvider.GetRequiredService<CorreosExternos>()
                    .EnviarAsync(correo.Mensaje, stoppingToken).ConfigureAwait(false);
                if (!enviado)
                {
                    await alcance.ServiceProvider.GetRequiredService<RegistroSeguridad>().RegistrarAsync(TiposEventoSeguridad.CorreoNoEnviado,
                        correo.Datos with { Detalle = new { motivo = correo.Motivo } }, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Nunca se escapa: tumbaría el host (BackgroundServiceExceptionBehavior = StopHost).
                Fallo(log, error.GetType().Name);
            }
            finally
            {
                cola.Terminado();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identidad: un correo de la cola no se pudo procesar ({Error})")]
    private static partial void Fallo(ILogger log, string error);
}
