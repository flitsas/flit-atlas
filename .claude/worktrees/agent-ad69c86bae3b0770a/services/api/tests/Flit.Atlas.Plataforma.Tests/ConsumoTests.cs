using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Flit.Atlas.Plataforma.Almacenamiento;
using Flit.Atlas.Plataforma.Mensajeria;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>
/// Colas que atlas-api consume de otros servicios (HU 4.5, ATLAS-ADR-0018), contra un RabbitMQ real: el manejador recibe
/// el sobre de su tipo y el mensaje se confirma; un fallo vuelve por la cola de reintentos y, al llegar al tope, queda en
/// la de errores; un sobre ilegible va directo a errores.
/// </summary>
public class ConsumoTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TipoEvento Prueba = new("atlas.pruebas.algo_listo", 1);

    private sealed class ManejadorDePrueba(string cola, int fallarVeces) : IManejadorEventos
    {
        public string Cola => cola;

        public string Exchange => "flit.atlas.pruebas";

        public IReadOnlyList<TipoEvento> Tipos => [Prueba];

        public ConcurrentQueue<SobreEvento> Recibidos { get; } = new();

        public int Intentos;

        public Task ManejarAsync(SobreEvento sobre, IServiceProvider servicios, CancellationToken ct)
        {
            if (Interlocked.Increment(ref Intentos) <= fallarVeces)
            {
                throw new InvalidOperationException("falla de prueba");
            }
            Recibidos.Enqueue(sobre);
            return Task.CompletedTask;
        }
    }

    private async Task<(ConsumidorEventos Consumidor, IConnection Conexion)> IniciarAsync(ManejadorDePrueba manejador, int maximoIntentos)
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(contenedores.RabbitMq.GetConnectionString()) };
        var conexion = await fabrica.CreateConnectionAsync(Ct);
        var servicios = new ServiceCollection().AddSingleton(conexion).BuildServiceProvider();
        var opciones = Options.Create(new OpcionesConsumo
        {
            EsperaReintento = TimeSpan.FromMilliseconds(300), MaximoIntentos = maximoIntentos, EsperaConexion = TimeSpan.FromMilliseconds(200),
        });
        var consumidor = new ConsumidorEventos(servicios, [manejador], opciones, TimeProvider.System, NullLogger<ConsumidorEventos>.Instance);
        await consumidor.StartAsync(Ct);
        // La topología queda declarada cuando la cola existe.
        return await EsperarColaAsync(consumidor, conexion, manejador);
    }

    private static async Task<(ConsumidorEventos, IConnection)> EsperarColaAsync(ConsumidorEventos consumidor, IConnection conexion, ManejadorDePrueba manejador)
    {
        for (var i = 0; i < 50; i++)
        {
            await using var canal = await conexion.CreateChannelAsync(cancellationToken: Ct);
            try
            {
                await canal.QueueDeclarePassiveAsync(manejador.Cola, Ct);
                return (consumidor, conexion);
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                await Task.Delay(100, Ct);
            }
        }
        throw new TimeoutException("El consumidor no declaró su cola.");
    }

    private static async Task PublicarAsync(IConnection conexion, string exchange, string tipo, byte[] cuerpo)
    {
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: Ct);
        await canal.BasicPublishAsync(exchange, tipo, mandatory: false, new BasicProperties { Type = tipo, ContentType = "application/json" }, cuerpo, Ct);
    }

    private static byte[] Sobre(string tipo) => Encoding.UTF8.GetBytes(new SobreEvento(Guid.CreateVersion7(), tipo, 1, DateTimeOffset.UtcNow,
        SobreEvento.SinOrganizacion, "atlas-pruebas", string.Empty, JsonSerializer.SerializeToElement(new { nombre = "Organismo Demo" })).AJson());

    private static async Task<uint> MensajesAsync(IConnection conexion, string cola)
    {
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: Ct);
        return (await canal.QueueDeclarePassiveAsync(cola, Ct)).MessageCount;
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicion)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condicion())
            {
                return;
            }
            await Task.Delay(100, Ct);
        }
        Assert.Fail("La condición no se cumplió a tiempo.");
    }

    [Fact]
    public async Task Un_fallo_se_reintenta_por_la_cola_de_reintentos_y_luego_se_procesa()
    {
        var manejador = new ManejadorDePrueba($"atlas.api.pruebas_{Guid.NewGuid():N}", fallarVeces: 2);
        var (consumidor, conexion) = await IniciarAsync(manejador, maximoIntentos: 5);
        try
        {
            await PublicarAsync(conexion, manejador.Exchange, Prueba.Nombre, Sobre(Prueba.Nombre));

            await EsperarAsync(() => Task.FromResult(!manejador.Recibidos.IsEmpty));

            Assert.Equal(3, manejador.Intentos);
            Assert.Equal("Organismo Demo", Assert.Single(manejador.Recibidos).Data.GetProperty("nombre").GetString());
            Assert.Equal(0u, await MensajesAsync(conexion, ConsumidorEventos.ColaErrores(manejador.Cola)));
        }
        finally
        {
            await consumidor.StopAsync(Ct);
            await conexion.DisposeAsync();
        }
    }

    [Fact]
    public async Task Al_llegar_al_tope_o_si_es_ilegible_el_mensaje_queda_en_errores()
    {
        var manejador = new ManejadorDePrueba($"atlas.api.pruebas_{Guid.NewGuid():N}", fallarVeces: int.MaxValue);
        var (consumidor, conexion) = await IniciarAsync(manejador, maximoIntentos: 2);
        try
        {
            await PublicarAsync(conexion, manejador.Exchange, Prueba.Nombre, Sobre(Prueba.Nombre));
            await PublicarAsync(conexion, manejador.Exchange, Prueba.Nombre, "no es un sobre"u8.ToArray());

            await EsperarAsync(async () => await MensajesAsync(conexion, ConsumidorEventos.ColaErrores(manejador.Cola)) == 2);

            Assert.Equal(2, manejador.Intentos);
            Assert.Empty(manejador.Recibidos);
            Assert.Equal(0u, await MensajesAsync(conexion, manejador.Cola));
        }
        finally
        {
            await consumidor.StopAsync(Ct);
            await conexion.DisposeAsync();
        }
    }

    [Fact]
    public void Las_muertes_se_leen_de_la_cabecera_x_death_de_la_cola()
    {
        var cabeceras = new Dictionary<string, object?>
        {
            ["x-death"] = new List<object>
            {
                new Dictionary<string, object?> { ["queue"] = Encoding.UTF8.GetBytes("otra"), ["count"] = 9L },
                new Dictionary<string, object?> { ["queue"] = Encoding.UTF8.GetBytes("atlas.api.demo"), ["count"] = 3L },
            },
        };

        Assert.Equal(3, ConsumidorEventos.Muertes(cabeceras, "atlas.api.demo"));
        Assert.Equal(0, ConsumidorEventos.Muertes(null, "atlas.api.demo"));
        Assert.Equal(0, ConsumidorEventos.Muertes(new Dictionary<string, object?>(), "atlas.api.demo"));
    }

    [Theory]
    [InlineData("integracion-ict-r3.zip", "attachment; filename=\"integracion-ict-r3.zip\"; filename*=UTF-8''integracion-ict-r3.zip")]
    [InlineData("Guía \"ICT\";.pdf", "attachment; filename=\"Guia _ICT__.pdf\"; filename*=UTF-8''Gu%C3%ADa%20%22ICT%22%3B.pdf")]
    public void El_nombre_de_descarga_va_como_adjunto_en_ASCII_y_en_UTF8(string nombre, string esperado) =>
        Assert.Equal(esperado, AlmacenS3.Adjunto(nombre));
}
