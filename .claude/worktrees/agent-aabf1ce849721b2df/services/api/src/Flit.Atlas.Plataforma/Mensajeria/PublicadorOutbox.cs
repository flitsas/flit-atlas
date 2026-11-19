using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma.Mensajeria;

public sealed class OpcionesMensajeria
{
    /// <summary>Espera entre ciclos cuando no hay pendientes.</summary>
    public TimeSpan IntervaloSondeo { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Tope de la espera con el broker o la base caídos.</summary>
    public TimeSpan EsperaMaxima { get; set; } = TimeSpan.FromMinutes(1);

    public int TamanoLote { get; set; } = 100;

    /// <summary>Los eventos publicados se borran pasado este tiempo.</summary>
    public TimeSpan Retencion { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan IntervaloLimpieza { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Saca los eventos pendientes de <c>plataforma_outbox</c> y los publica (ATLAS-ADR-0018). Reclama en orden con
/// <c>FOR NO KEY UPDATE SKIP LOCKED</c>, así que es seguro con varias réplicas de atlas-api. Sella
/// <c>publicado_en</c> solo cuando el broker confirmó. Si el broker no responde, el evento queda pendiente (sube
/// <c>intentos</c>) y el lote se corta para conservar el orden. Con fallos seguidos, la espera entre ciclos se duplica
/// hasta <see cref="OpcionesMensajeria.EsperaMaxima"/>.
/// </summary>
public sealed partial class PublicadorOutbox(
    IServiceScopeFactory alcances,
    ICanalEventos canal,
    OpcionesMensajeria opciones,
    TimeProvider reloj,
    ILogger<PublicadorOutbox> log) : BackgroundService
{
    private DateTimeOffset _ultimaLimpieza = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fallosSeguidos = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            Ciclo ciclo;
            try
            {
                ciclo = await PublicarPendientesAsync(stoppingToken).ConfigureAwait(false);
                await LimpiarSiTocaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Cualquier fallo del ciclo se registra y se reintenta; el servicio no debe morir.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorDeCiclo(log, ex);
                ciclo = new Ciclo(0, Fallo: true);
            }

            fallosSeguidos = ciclo.Fallo ? fallosSeguidos + 1 : 0;
            if (!ciclo.Fallo && ciclo.Publicados >= opciones.TamanoLote)
            {
                continue; // Lote lleno: puede haber más.
            }

            try
            {
                await Task.Delay(Espera(fallosSeguidos, opciones.IntervaloSondeo, opciones.EsperaMaxima), reloj, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal readonly record struct Ciclo(int Publicados, bool Fallo);

    /// <summary>Un ciclo: reclama un lote, lo publica en orden y sella lo publicado.</summary>
    internal async Task<Ciclo> PublicarPendientesAsync(CancellationToken ct)
    {
        using var alcance = alcances.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var lote = await contexto.Outbox
            .FromSqlInterpolated($"""
                SELECT * FROM atlas.plataforma_outbox
                WHERE publicado_en IS NULL
                ORDER BY ocurrido_en, id
                LIMIT {opciones.TamanoLote}
                FOR NO KEY UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct).ConfigureAwait(false);

        var publicados = 0;
        var fallo = false;
        foreach (var mensaje in lote)
        {
            try
            {
                await canal.PublicarAsync(mensaje, ct).ConfigureAwait(false);
                mensaje.PublicadoEn = reloj.GetUtcNow();
                mensaje.UltimoError = null;
                publicados++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // El fallo se guarda en el mensaje y el lote se corta.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                mensaje.Intentos++;
                mensaje.UltimoError = Truncar($"{ex.GetType().Name}: {ex.Message}", 2000);
                FalloAlPublicar(log, mensaje.Id, mensaje.Intentos, ex);
                fallo = true;
                break;
            }
        }

        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);
        return new Ciclo(publicados, fallo);
    }

    internal async Task<int> LimpiarSiTocaAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        if (ahora - _ultimaLimpieza < opciones.IntervaloLimpieza)
        {
            return 0;
        }

        using var alcance = alcances.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var limite = ahora - opciones.Retencion;
        var borrados = await contexto.Outbox
            .Where(m => m.PublicadoEn != null && m.PublicadoEn < limite)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        _ultimaLimpieza = ahora;
        return borrados;
    }

    /// <summary>Intervalo normal sin fallos; con fallos seguidos: intervalo × 2^(fallos−1), con tope.</summary>
    internal static TimeSpan Espera(int fallosSeguidos, TimeSpan intervalo, TimeSpan maxima)
    {
        if (fallosSeguidos <= 0)
        {
            return intervalo;
        }
        var espera = intervalo * Math.Pow(2, Math.Min(fallosSeguidos - 1, 30));
        return espera > maxima ? maxima : espera;
    }

    private static string Truncar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo publicar el evento {EventoId} (intento {Intentos}); queda pendiente")]
    private static partial void FalloAlPublicar(ILogger log, Guid eventoId, int intentos, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló un ciclo de la outbox; se reintenta")]
    private static partial void ErrorDeCiclo(ILogger log, Exception ex);
}
