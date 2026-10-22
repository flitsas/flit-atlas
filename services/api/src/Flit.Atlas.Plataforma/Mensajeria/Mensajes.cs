using System.Diagnostics;
using System.Text.Json;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Mensajeria;

/// <summary>
/// Un evento por publicar, guardado en la misma transacción que el cambio que lo origina (ATLAS-ADR-0018).
/// El publicador lo saca de aquí, lo manda al broker y sella <see cref="PublicadoEn"/> al recibir la confirmación.
/// </summary>
public sealed class MensajeOutbox
{
    /// <summary>= <see cref="SobreEvento.EventId"/>.</summary>
    public Guid Id { get; init; }

    public required string Exchange { get; init; }

    /// <summary>= <see cref="SobreEvento.Type"/>: el exchange es topic y la llave de ruteo es el tipo.</summary>
    public required string Tipo { get; init; }

    /// <summary>El sobre completo en JSON, tal como viaja.</summary>
    public required string Sobre { get; init; }

    public required DateTimeOffset OcurridoEn { get; init; }

    public DateTimeOffset? PublicadoEn { get; set; }

    /// <summary>Intentos fallidos. Sin tope: un broker caído es transitorio y el evento no se pierde.</summary>
    public int Intentos { get; set; }

    public string? UltimoError { get; set; }
}

/// <summary>Un evento ya procesado por un consumidor: llave <c>(evento_id, consumidor)</c> (ATLAS-ADR-0018).</summary>
public sealed class MensajeInbox
{
    public required Guid EventoId { get; init; }

    /// <summary>Nombre de la cola del consumidor (<c>atlas.&lt;consumidor&gt;.&lt;proposito&gt;</c>).</summary>
    public required string Consumidor { get; init; }

    public required string Tipo { get; init; }

    public required DateTimeOffset ProcesadoEn { get; init; }
}

public static class MensajeriaModelBuilderExtensions
{
    public const string TablaOutbox = "plataforma_outbox";
    public const string TablaInbox = "plataforma_inbox";

    /// <summary>
    /// Mapea <c>plataforma_outbox</c> y <c>plataforma_inbox</c>. Un módulo con su propio <c>DbContext</c> las mapea con
    /// <paramref name="excluirDeMigraciones"/> en <c>true</c> para escribir eventos en su transacción sin crear las tablas.
    /// </summary>
    public static ModelBuilder AgregarMensajeria(this ModelBuilder modelo, bool excluirDeMigraciones)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        modelo.Entity<MensajeOutbox>(e =>
        {
            e.ToTable(TablaOutbox, t => t.ExcludeFromMigrations(excluirDeMigraciones));
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedNever();
            e.Property(m => m.Exchange).HasMaxLength(100);
            e.Property(m => m.Tipo).HasMaxLength(200);
            e.Property(m => m.UltimoError).HasMaxLength(2000);
            e.HasIndex(m => new { m.OcurridoEn, m.Id })
                .HasDatabaseName("ix_plataforma_outbox_pendientes")
                .HasFilter("publicado_en IS NULL");
        });

        modelo.Entity<MensajeInbox>(e =>
        {
            e.ToTable(TablaInbox, t => t.ExcludeFromMigrations(excluirDeMigraciones));
            e.HasKey(m => new { m.EventoId, m.Consumidor });
            e.Property(m => m.Consumidor).HasMaxLength(200);
            e.Property(m => m.Tipo).HasMaxLength(200);
        });

        return modelo;
    }

    /// <summary>
    /// Agrega un evento a la outbox. Se publica cuando el <c>SaveChanges</c> del llamador confirma la transacción.
    /// </summary>
    /// <param name="tipo"><c>atlas.&lt;modulo&gt;.&lt;evento&gt;</c>.</param>
    /// <param name="organizacionId">Organización externa dueña del evento; <c>null</c> si es interno.</param>
    public static SobreEvento EncolarEvento<TDatos>(
        this ContextoAtlas contexto, string tipo, int version, TDatos datos, Guid? organizacionId = null)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);

        var sobre = new SobreEvento(
            EventId: Guid.CreateVersion7(),
            Type: tipo,
            Version: version,
            OccurredAt: contexto.Reloj.GetUtcNow(),
            TenantId: organizacionId ?? SobreEvento.SinOrganizacion,
            Producer: SobreEvento.Productor,
            CorrelationId: Activity.Current?.TraceId.ToString() ?? string.Empty,
            Data: JsonSerializer.SerializeToElement(datos, SobreEvento.OpcionesJson));

        contexto.Set<MensajeOutbox>().Add(new MensajeOutbox
        {
            Id = sobre.EventId,
            Exchange = SobreEvento.Exchange,
            Tipo = sobre.Type,
            Sobre = sobre.AJson(),
            OcurridoEn = sobre.OccurredAt,
        });
        return sobre;
    }

    /// <summary>
    /// Registra que <paramref name="consumidor"/> procesó el evento. Devuelve <c>false</c> si ya estaba: el llamador
    /// confirma el mensaje sin repetir el efecto. Se guarda con el <c>SaveChanges</c> del efecto, en la misma transacción.
    /// </summary>
    public static async Task<bool> RegistrarEnInboxAsync(
        this ContextoAtlas contexto, SobreEvento sobre, string consumidor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(sobre);

        var inbox = contexto.Set<MensajeInbox>();
        if (await inbox.AnyAsync(m => m.EventoId == sobre.EventId && m.Consumidor == consumidor, ct).ConfigureAwait(false))
        {
            return false;
        }

        inbox.Add(new MensajeInbox
        {
            EventoId = sobre.EventId,
            Consumidor = consumidor,
            Tipo = sobre.Type,
            ProcesadoEn = contexto.Reloj.GetUtcNow(),
        });
        return true;
    }
}
