using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Flit.Atlas.Plataforma.Mensajeria;

/// <summary>
/// Una cola de atlas-api que recibe eventos de otro servicio de Atlas (ATLAS-ADR-0018): p. ej. los resultados de
/// <c>atlas-docs</c> en <c>flit.atlas.docs</c>. Se registra con <see cref="ExtensionesConsumo.AgregarManejadorEventos{T}"/>.
/// El manejador aplica la inbox (<see cref="MensajeriaModelBuilderExtensions.RegistrarEnInboxAsync"/>) en la misma
/// transacción que su efecto, o es idempotente por su estado.
/// </summary>
public interface IManejadorEventos
{
    /// <summary><c>atlas.api.&lt;proposito&gt;</c>; las colas de reintentos y de errores toman este nombre como prefijo.</summary>
    string Cola { get; }

    /// <summary>Exchange topic del productor (<c>flit.&lt;productor&gt;</c>).</summary>
    string Exchange { get; }

    /// <summary>Los tipos que la cola recibe: cada uno es una llave de ruteo enlazada.</summary>
    IReadOnlyList<TipoEvento> Tipos { get; }

    /// <summary>Procesa un evento. Si lanza, el mensaje se reintenta (hasta <see cref="OpcionesConsumo.MaximoIntentos"/>).</summary>
    Task ManejarAsync(SobreEvento sobre, IServiceProvider servicios, CancellationToken ct);
}

/// <summary>Consumo de colas de atlas-api (sección <c>Plataforma:Mensajeria:Consumo</c>).</summary>
public sealed class OpcionesConsumo
{
    public const string Seccion = "Plataforma:Mensajeria:Consumo";

    /// <summary>Apagado, atlas-api no abre colas (las pruebas que no usan el broker).</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Espera de un mensaje fallido en <c>&lt;cola&gt;.reintentos</c> antes de volver a la cola.</summary>
    public TimeSpan EsperaReintento { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Intentos antes de dejar el mensaje en <c>&lt;cola&gt;.errores</c>.</summary>
    public int MaximoIntentos { get; set; } = 5;

    /// <summary>Espera entre intentos de conexión con el broker caído.</summary>
    public TimeSpan EsperaConexion { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Consume las colas de los <see cref="IManejadorEventos"/> registrados (ATLAS-ADR-0018): declara la topología (el
/// exchange del productor, la cola con su exchange y cola de reintentos con TTL y la cola de errores), recibe de a un
/// mensaje por cola (prefetch 1) y confirma solo después del efecto. Un fallo manda el mensaje a reintentos
/// (<c>nack</c> sin reencolar, por la DLX); al llegar al tope de intentos, lo copia a errores y lo confirma. Un sobre
/// ilegible o de un tipo que la cola no espera va directo a errores. Si el broker no está, reintenta la conexión sin
/// tumbar atlas-api; la conexión de RabbitMQ.Client se recupera sola con sus consumidores.
/// </summary>
internal sealed partial class ConsumidorEventos(
    IServiceProvider servicios,
    IEnumerable<IManejadorEventos> manejadores,
    IOptions<OpcionesConsumo> opciones,
    TimeProvider reloj,
    ILogger<ConsumidorEventos> log) : BackgroundService
{
    private readonly List<IChannel> _canales = [];
    private readonly Lock _cerrojo = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lista = manejadores.ToList();
        if (!opciones.Value.Habilitado || lista.Count == 0)
        {
            return;
        }
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            var cerrado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var conexion = servicios.GetRequiredService<IConnection>();
                foreach (var manejador in lista)
                {
                    var canal = await AbrirAsync(conexion, manejador, stoppingToken).ConfigureAwait(false);
                    canal.ChannelShutdownAsync += (_, _) =>
                    {
                        cerrado.TrySetResult();
                        return Task.CompletedTask;
                    };
                }
                await cerrado.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
                CanalCerrado(log);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // El broker caído es transitorio: se registra y se reintenta.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                SinBroker(log, ex);
            }
            await CerrarCanalesAsync().ConfigureAwait(false);
            try
            {
                await Task.Delay(opciones.Value.EsperaConexion, reloj, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await CerrarCanalesAsync().ConfigureAwait(false);
    }

    private async Task<IChannel> AbrirAsync(IConnection conexion, IManejadorEventos manejador, CancellationToken ct)
    {
        var canal = await conexion.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);
        lock (_cerrojo)
        {
            _canales.Add(canal);
        }
        await DeclararAsync(canal, manejador, opciones.Value, ct).ConfigureAwait(false);
        await canal.BasicQosAsync(0, 1, false, ct).ConfigureAwait(false);
        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) => ProcesarAsync(canal, manejador, entrega, ct);
        await canal.BasicConsumeAsync(manejador.Cola, autoAck: false, consumidor, ct).ConfigureAwait(false);
        return canal;
    }

    /// <summary>Exchange del productor, cola con DLX a reintentos, reintentos con TTL de vuelta a la cola, y errores.</summary>
    internal static async Task DeclararAsync(IChannel canal, IManejadorEventos manejador, OpcionesConsumo opciones, CancellationToken ct)
    {
        var reintentos = ColaReintentos(manejador.Cola);
        await canal.ExchangeDeclareAsync(manejador.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct)
            .ConfigureAwait(false);
        await canal.ExchangeDeclareAsync(reintentos, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct)
            .ConfigureAwait(false);
        await canal.QueueDeclareAsync(manejador.Cola, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = reintentos }, cancellationToken: ct).ConfigureAwait(false);
        foreach (var tipo in manejador.Tipos.Select(t => t.Nombre).Distinct(StringComparer.Ordinal))
        {
            await canal.QueueBindAsync(manejador.Cola, manejador.Exchange, tipo, cancellationToken: ct).ConfigureAwait(false);
        }
        await canal.QueueDeclareAsync(reintentos, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = (long)opciones.EsperaReintento.TotalMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = manejador.Cola,
            }, cancellationToken: ct).ConfigureAwait(false);
        await canal.QueueBindAsync(reintentos, reintentos, string.Empty, cancellationToken: ct).ConfigureAwait(false);
        await canal.QueueDeclareAsync(ColaErrores(manejador.Cola), durable: true, exclusive: false, autoDelete: false, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public static string ColaReintentos(string cola) => cola + ".reintentos";

    public static string ColaErrores(string cola) => cola + ".errores";

    private async Task ProcesarAsync(IChannel canal, IManejadorEventos manejador, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        SobreEvento? sobre;
        try
        {
            sobre = SobreEvento.DesdeJson(entrega.Body.Span);
        }
        catch (JsonException)
        {
            sobre = null;
        }
        if (sobre is null || !manejador.Tipos.Any(t => t.Nombre == sobre.Type && t.Version == sobre.Version))
        {
            MensajeDescartado(log, manejador.Cola, entrega.BasicProperties.Type ?? "(sin tipo)");
            await AErroresAsync(canal, manejador, entrega).ConfigureAwait(false);
            return;
        }

        try
        {
            await using (var alcance = servicios.CreateAsyncScope())
            {
                await manejador.ManejarAsync(sobre, alcance.ServiceProvider, ct).ConfigureAwait(false);
            }
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Al apagar: el mensaje vuelve a la cola sin contar como intento.
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Cualquier fallo del manejador se reintenta; tras el tope, el mensaje queda en errores.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var intentos = Muertes(entrega.BasicProperties.Headers, manejador.Cola) + 1;
            FalloAlManejar(log, sobre.EventId, sobre.Type, intentos, ex);
            if (intentos >= opciones.Value.MaximoIntentos)
            {
                await AErroresAsync(canal, manejador, entrega).ConfigureAwait(false);
            }
            else
            {
                await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Copia el mensaje (cuerpo y propiedades) a la cola de errores y lo confirma en la original.</summary>
    private static async Task AErroresAsync(IChannel canal, IManejadorEventos manejador, BasicDeliverEventArgs entrega)
    {
        var propiedades = new BasicProperties
        {
            MessageId = entrega.BasicProperties.MessageId,
            Type = entrega.BasicProperties.Type,
            ContentType = entrega.BasicProperties.ContentType,
            ContentEncoding = entrega.BasicProperties.ContentEncoding,
            Timestamp = entrega.BasicProperties.Timestamp,
            Headers = entrega.BasicProperties.Headers,
            DeliveryMode = DeliveryModes.Persistent,
        };
        await canal.BasicPublishAsync(string.Empty, ColaErrores(manejador.Cola), mandatory: false, propiedades, entrega.Body, CancellationToken.None)
            .ConfigureAwait(false);
        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Cuántas veces la cola rechazó este mensaje, según la cabecera <c>x-death</c> que agrega RabbitMQ.</summary>
    internal static long Muertes(IDictionary<string, object?>? cabeceras, string cola)
    {
        if (cabeceras is null || !cabeceras.TryGetValue("x-death", out var valor) || valor is not IEnumerable<object> muertes)
        {
            return 0;
        }
        foreach (var muerte in muertes.OfType<IDictionary<string, object?>>())
        {
            var nombre = muerte.TryGetValue("queue", out var q) ? q switch
            {
                byte[] b => Encoding.UTF8.GetString(b),
                string s => s,
                _ => null,
            } : null;
            if (nombre == cola && muerte.TryGetValue("count", out var c) && c is not null)
            {
                return Convert.ToInt64(c, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        return 0;
    }

    private async Task CerrarCanalesAsync()
    {
        IChannel[] canales;
        // Lo llaman el ciclo y StopAsync a la vez: cada uno cierra lo que alcanzó a sacar de la lista.
        lock (_cerrojo)
        {
            canales = [.. _canales];
            _canales.Clear();
        }
        foreach (var canal in canales)
        {
            try
            {
                await canal.DisposeAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Cerrar un canal roto puede fallar; da igual.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo abrir el consumo de eventos; se reintenta")]
    private static partial void SinBroker(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Se cerró un canal de consumo; se vuelve a abrir")]
    private static partial void CanalCerrado(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mensaje ilegible o de un tipo inesperado en {Cola} ({Tipo}): pasa a errores")]
    private static partial void MensajeDescartado(ILogger log, string cola, string tipo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falló el evento {EventoId} ({Tipo}), intento {Intentos}")]
    private static partial void FalloAlManejar(ILogger log, Guid eventoId, string tipo, long intentos, Exception ex);
}

public static class ExtensionesConsumo
{
    /// <summary>Registra una cola de atlas-api. El consumidor la abre al arrancar (si el consumo está habilitado).</summary>
    public static IServiceCollection AgregarManejadorEventos<TManejador>(this IServiceCollection servicios)
        where TManejador : class, IManejadorEventos
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddEnumerable(ServiceDescriptor.Singleton<IManejadorEventos, TManejador>());
        return servicios;
    }

    internal static IServiceCollection AgregarConsumo(this IServiceCollection servicios)
    {
        servicios.AddOptions<OpcionesConsumo>().BindConfiguration(OpcionesConsumo.Seccion);
        servicios.AddHostedService<ConsumidorEventos>();
        return servicios;
    }
}
