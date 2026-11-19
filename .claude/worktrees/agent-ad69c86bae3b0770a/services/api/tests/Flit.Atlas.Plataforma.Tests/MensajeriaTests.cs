using System.Collections.Concurrent;
using System.Text.Json;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace Flit.Atlas.Plataforma.Tests;

public class MensajeriaTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record OrganizacionCreada(Guid OrganizacionId, string Nombre);

    private static PublicadorOutbox Publicador(BaseDePrueba bd, ICanalEventos canal, OpcionesMensajeria? opciones = null)
    {
        var servicios = new ServiceCollection()
            .AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, bd.Servicio))
            .BuildServiceProvider();
        return new PublicadorOutbox(
            servicios.GetRequiredService<IServiceScopeFactory>(), canal, opciones ?? new OpcionesMensajeria(),
            TimeProvider.System, NullLogger<PublicadorOutbox>.Instance);
    }

    private static async Task<SobreEvento> CrearOrganizacionConEventoAsync(BaseDePrueba bd, string nombre)
    {
        await using var contexto = bd.Contexto();
        var organizacion = new Organizacion { Nombre = nombre, Tipo = TipoOrganizacion.Cliente };
        contexto.Organizaciones.Add(organizacion);
        var sobre = contexto.EncolarEvento(
            "atlas.plataforma.organizacion_creada", 1, new OrganizacionCreada(organizacion.Id, nombre), organizacion.Id);
        await contexto.SaveChangesAsync(Ct);
        return sobre;
    }

    [Fact]
    public async Task El_evento_y_el_cambio_se_guardan_o_se_descartan_juntos()
    {
        var bd = await contenedores.CrearBaseAsync();

        await using (var contexto = bd.Contexto())
        {
            await using var transaccion = await contexto.Database.BeginTransactionAsync(Ct);
            contexto.Organizaciones.Add(new Organizacion { Nombre = "Descartada", Tipo = TipoOrganizacion.Cliente });
            contexto.EncolarEvento("atlas.plataforma.organizacion_creada", 1, new { nombre = "Descartada" });
            await contexto.SaveChangesAsync(Ct);
            await transaccion.RollbackAsync(Ct);
        }
        var sobre = await CrearOrganizacionConEventoAsync(bd, "Organismo Demo");

        await using var lectura = bd.Contexto();
        Assert.Equal(["Organismo Demo"], await lectura.Organizaciones.Select(o => o.Nombre).ToListAsync(Ct));
        var pendiente = Assert.Single(await lectura.Outbox.ToListAsync(Ct));
        Assert.Equal(sobre.EventId, pendiente.Id);
        Assert.Equal("flit.atlas", pendiente.Exchange);
        Assert.Null(pendiente.PublicadoEn);
    }

    [Fact]
    public async Task Publica_el_sobre_de_FLIT_en_flit_atlas_y_lo_sella_al_confirmar_el_broker()
    {
        var bd = await contenedores.CrearBaseAsync();
        var sobre = await CrearOrganizacionConEventoAsync(bd, "Organismo Demo");

        var fabrica = new ConnectionFactory { Uri = new Uri(contenedores.RabbitMq.GetConnectionString()) };
        await using var conexion = await fabrica.CreateConnectionAsync(Ct);
        await using var canalPrueba = await conexion.CreateChannelAsync(cancellationToken: Ct);
        await canalPrueba.ExchangeDeclareAsync(SobreEvento.Exchange, ExchangeType.Topic, durable: true, cancellationToken: Ct);
        var cola = (await canalPrueba.QueueDeclareAsync(cancellationToken: Ct)).QueueName;
        await canalPrueba.QueueBindAsync(cola, SobreEvento.Exchange, "atlas.plataforma.#", cancellationToken: Ct);

        var servicios = new ServiceCollection().AddSingleton(conexion).BuildServiceProvider();
        await using var canal = new CanalRabbitMq(servicios);
        var ciclo = await Publicador(bd, canal).PublicarPendientesAsync(Ct);

        Assert.Equal(1, ciclo.Publicados);
        Assert.False(ciclo.Fallo);
        var recibido = await canalPrueba.BasicGetAsync(cola, autoAck: true, Ct);
        Assert.NotNull(recibido);
        Assert.Equal("atlas.plataforma.organizacion_creada", recibido.RoutingKey);
        Assert.Equal(sobre.EventId.ToString(), recibido.BasicProperties.MessageId);

        using var json = JsonDocument.Parse(recibido.Body);
        var raiz = json.RootElement;
        Assert.Equal(
            ["eventId", "type", "version", "occurredAt", "tenantId", "producer", "correlationId", "data"],
            raiz.EnumerateObject().Select(p => p.Name));
        Assert.Equal("atlas", raiz.GetProperty("producer").GetString());
        Assert.Equal(sobre.TenantId, raiz.GetProperty("tenantId").GetGuid());
        Assert.Equal("Organismo Demo", raiz.GetProperty("data").GetProperty("nombre").GetString());

        await using var lectura = bd.Contexto();
        Assert.NotNull((await lectura.Outbox.SingleAsync(Ct)).PublicadoEn);
    }

    [Fact]
    public async Task Con_el_broker_caido_el_evento_queda_pendiente_y_no_se_salta_el_orden()
    {
        var bd = await contenedores.CrearBaseAsync();
        var primero = await CrearOrganizacionConEventoAsync(bd, "Primera");
        await CrearOrganizacionConEventoAsync(bd, "Segunda");
        var canal = new CanalFalso { Fallar = true };

        var ciclo = await Publicador(bd, canal).PublicarPendientesAsync(Ct);

        Assert.True(ciclo.Fallo);
        Assert.Equal([primero.EventId], canal.Intentados);
        await using var lectura = bd.Contexto();
        var mensajes = await lectura.Outbox.OrderBy(m => m.OcurridoEn).ToListAsync(Ct);
        Assert.All(mensajes, m => Assert.Null(m.PublicadoEn));
        Assert.Equal([1, 0], mensajes.Select(m => m.Intentos));
        Assert.Contains("broker caído", mensajes[0].UltimoError, StringComparison.Ordinal);

        canal.Fallar = false;
        var reintento = await Publicador(bd, canal).PublicarPendientesAsync(Ct);
        Assert.Equal(2, reintento.Publicados);
    }

    [Fact]
    public async Task Dos_replicas_no_publican_el_mismo_evento()
    {
        var bd = await contenedores.CrearBaseAsync();
        for (var i = 0; i < 20; i++)
        {
            await CrearOrganizacionConEventoAsync(bd, $"Organización {i}");
        }
        var canal = new CanalFalso { Demora = TimeSpan.FromMilliseconds(20) };
        var opciones = new OpcionesMensajeria { TamanoLote = 5 };

        async Task Vaciar(PublicadorOutbox p)
        {
            while ((await p.PublicarPendientesAsync(Ct)).Publicados > 0)
            {
            }
        }
        await Task.WhenAll(Vaciar(Publicador(bd, canal, opciones)), Vaciar(Publicador(bd, canal, opciones)));

        Assert.Equal(20, canal.Intentados.Count);
        Assert.Equal(20, canal.Intentados.Distinct().Count());
    }

    [Fact]
    public async Task El_inbox_detecta_un_evento_repetido()
    {
        var bd = await contenedores.CrearBaseAsync();
        var sobre = new SobreEvento(Guid.CreateVersion7(), "atlas.docs.exportacion_solicitada", 1, DateTimeOffset.UtcNow,
            SobreEvento.SinOrganizacion, "atlas", "", JsonSerializer.SerializeToElement(new { }));

        await using (var contexto = bd.Contexto())
        {
            Assert.True(await contexto.RegistrarEnInboxAsync(sobre, "atlas.docs.exportar", Ct));
            await contexto.SaveChangesAsync(Ct);
        }

        await using var otro = bd.Contexto();
        Assert.False(await otro.RegistrarEnInboxAsync(sobre, "atlas.docs.exportar", Ct));
        Assert.True(await otro.RegistrarEnInboxAsync(sobre, "atlas.ia.indexar", Ct));
    }

    [Fact]
    public async Task Borra_los_eventos_publicados_despues_de_la_retencion()
    {
        var bd = await contenedores.CrearBaseAsync();
        await CrearOrganizacionConEventoAsync(bd, "Vieja");
        await CrearOrganizacionConEventoAsync(bd, "Pendiente");
        await using (var contexto = bd.Contexto())
        {
            var vieja = await contexto.Outbox.OrderBy(m => m.OcurridoEn).FirstAsync(Ct);
            vieja.PublicadoEn = DateTimeOffset.UtcNow.AddDays(-8);
            await contexto.SaveChangesAsync(Ct);
        }

        var borrados = await Publicador(bd, new CanalFalso()).LimpiarSiTocaAsync(Ct);

        Assert.Equal(1, borrados);
        await using var lectura = bd.Contexto();
        Assert.Null((await lectura.Outbox.SingleAsync(Ct)).PublicadoEn);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(10, 60)]
    public void La_espera_se_duplica_con_cada_fallo_hasta_el_tope(int fallos, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos),
            PublicadorOutbox.Espera(fallos, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)));

    private sealed class CanalFalso : ICanalEventos
    {
        public bool Fallar { get; set; }

        public TimeSpan Demora { get; init; }

        public ConcurrentQueue<Guid> IntentadosCola { get; } = new();

        public List<Guid> Intentados => [.. IntentadosCola];

        public async Task PublicarAsync(MensajeOutbox mensaje, CancellationToken ct)
        {
            IntentadosCola.Enqueue(mensaje.Id);
            if (Demora > TimeSpan.Zero)
            {
                await Task.Delay(Demora, ct);
            }
            if (Fallar)
            {
                throw new InvalidOperationException("broker caído");
            }
        }
    }
}
