using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Flit.Atlas.Plataforma.Mensajeria;

/// <summary>Publica un mensaje de la outbox en el broker y espera su confirmación.</summary>
public interface ICanalEventos
{
    Task PublicarAsync(MensajeOutbox mensaje, CancellationToken ct);
}

/// <summary>
/// Canal de RabbitMQ con confirmación del broker (<i>publisher confirms</i>): <see cref="PublicarAsync"/> termina
/// solo cuando el broker aceptó el mensaje y falla si lo rechaza. Declara el exchange <c>flit.atlas</c> (topic, durable).
/// La conexión se pide al primer envío: si el broker no está al arrancar, atlas-api arranca igual y la outbox reintenta.
/// </summary>
internal sealed class CanalRabbitMq(IServiceProvider servicios) : ICanalEventos, IAsyncDisposable
{
    private readonly SemaphoreSlim _cerrojo = new(1, 1);
    private IChannel? _canal;

    public async Task PublicarAsync(MensajeOutbox mensaje, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var canal = await ObtenerCanalAsync(mensaje.Exchange, ct).ConfigureAwait(false);
            var propiedades = new BasicProperties
            {
                MessageId = mensaje.Id.ToString(),
                Type = mensaje.Tipo,
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(mensaje.OcurridoEn.ToUnixTimeSeconds()),
            };
            try
            {
                await canal.BasicPublishAsync(
                    mensaje.Exchange, mensaje.Tipo, mandatory: false, propiedades,
                    Encoding.UTF8.GetBytes(mensaje.Sobre), ct).ConfigureAwait(false);
            }
            catch
            {
                // Un canal con error queda inservible: se descarta y el siguiente intento abre otro.
                await DescartarCanalAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    private async Task<IChannel> ObtenerCanalAsync(string exchange, CancellationToken ct)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        await DescartarCanalAsync().ConfigureAwait(false);
        var conexion = servicios.GetRequiredService<IConnection>();
        var canal = await conexion.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct).ConfigureAwait(false);
        await canal.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct)
            .ConfigureAwait(false);
        _canal = canal;
        return canal;
    }

    private async Task DescartarCanalAsync()
    {
        if (_canal is null)
        {
            return;
        }
        try
        {
            await _canal.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Cerrar un canal roto puede fallar; da igual, se abre otro.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        _canal = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DescartarCanalAsync().ConfigureAwait(false);
        _cerrojo.Dispose();
    }
}
