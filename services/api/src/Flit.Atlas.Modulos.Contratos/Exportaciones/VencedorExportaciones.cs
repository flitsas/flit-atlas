using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Almacenamiento;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Contratos.Exportaciones.ServicioExportaciones;

namespace Flit.Atlas.Modulos.Contratos.Exportaciones;

/// <summary>
/// Retención de las exportaciones (HU 4.5, ATLAS-ADR-0042), cada <see cref="OpcionesExportaciones.IntervaloVencimiento"/>:
/// (1) las listas o fallidas cuya retención pasó pierden sus archivos del S3 (todo bajo <c>exportaciones/&lt;id&gt;/</c>,
/// la entrada incluida) y quedan <c>vencida</c>; (2) las pendientes o en proceso más viejas que
/// <see cref="OpcionesExportaciones.TiempoMaximo"/> quedan <c>fallida</c> (<c>tiempo_agotado</c>), auditadas y avisadas.
/// Es seguro con varias réplicas: borrar dos veces el mismo prefijo no hace daño y cada cambio de estado es condicional.
/// </summary>
internal sealed partial class VencedorExportaciones(
    IServiceScopeFactory alcances, IOptions<OpcionesExportaciones> opciones, TimeProvider reloj, ILogger<VencedorExportaciones> log)
    : BackgroundService
{
    private const int Lote = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VencerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Un ciclo fallido (base o S3 caídos) se registra y se reintenta en el siguiente.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                CicloFallido(log, ex);
            }
            try
            {
                await Task.Delay(opciones.Value.IntervaloVencimiento, reloj, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Un ciclo; devuelve cuántas vencieron y cuántas se cortaron por tiempo.</summary>
    internal async Task<(int Vencidas, int Agotadas)> VencerAsync(CancellationToken ct)
    {
        using var alcance = alcances.CreateScope();
        var servicios = alcance.ServiceProvider;
        var contexto = servicios.GetRequiredService<ContratosDbContext>();
        var almacen = servicios.GetRequiredService<IAlmacenObjetos>();
        var ahora = reloj.GetUtcNow();

        var vencidas = 0;
        if (almacen.Disponible)
        {
            var porVencer = await contexto.Exportaciones.AsNoTracking()
                .Where(e => (e.Estado == EstadoExportacion.Lista || e.Estado == EstadoExportacion.Fallida) && e.VenceEn != null && e.VenceEn <= ahora)
                .OrderBy(e => e.VenceEn).Take(Lote).Select(e => new { e.Id, e.Estado })
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var e in porVencer)
            {
                await almacen.BorrarPrefijoAsync(Prefijo(e.Id), ct).ConfigureAwait(false);
                vencidas += await contexto.Exportaciones
                    .Where(x => x.Id == e.Id && x.Estado == e.Estado)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, EstadoExportacion.Vencida).SetProperty(x => x.VencidaEn, ahora), ct)
                    .ConfigureAwait(false);
            }
        }

        var limite = ahora - opciones.Value.TiempoMaximo;
        var atascadas = await contexto.Exportaciones.AsNoTracking()
            .Where(e => (e.Estado == EstadoExportacion.Pendiente || e.Estado == EstadoExportacion.Procesando) && e.SolicitadaEn <= limite)
            .OrderBy(e => e.SolicitadaEn).Take(Lote)
            .ToListAsync(ct).ConfigureAwait(false);
        var plataforma = servicios.GetRequiredService<PlataformaDbContext>();
        var manejador = ActivatorUtilities.CreateInstance<ManejadorResultadosExportacion>(servicios);
        var agotadas = 0;
        foreach (var e in atascadas)
        {
            var estado = e.Estado;
            ManejadorResultadosExportacion.Fallar(e, ahora, "tiempo_agotado", "La exportación tardó demasiado; solicítala de nuevo.");
            // Condicional: si atlas-docs terminó justo ahora u otra réplica ya la cortó, no se toca.
            var cambiadas = await contexto.Exportaciones
                .Where(x => x.Id == e.Id && x.Estado == estado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Estado, e.Estado)
                    .SetProperty(x => x.ErrorCodigo, e.ErrorCodigo)
                    .SetProperty(x => x.Error, e.Error)
                    .SetProperty(x => x.TerminadaEn, e.TerminadaEn)
                    .SetProperty(x => x.VenceEn, e.VenceEn)
                    .SetProperty(x => x.ActualizadoEn, ahora), ct)
                .ConfigureAwait(false);
            if (cambiadas == 0)
            {
                continue;
            }
            agotadas++;
            plataforma.Auditoria.Add(ManejadorResultadosExportacion.AuditoriaFallida(e, ahora, "tiempo_agotado"));
            await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
            await manejador.AvisarAsync(servicios, e.SolicitadaPor, ManejadorResultadosExportacion.AvisoFallida(e), ct).ConfigureAwait(false);
        }
        if (vencidas + agotadas > 0)
        {
            Ciclo(log, vencidas, agotadas);
        }
        return (vencidas, agotadas);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falló el ciclo de retención de exportaciones; se reintenta")]
    private static partial void CicloFallido(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exportaciones: {Vencidas} vencidas y {Agotadas} cortadas por tiempo")]
    private static partial void Ciclo(ILogger log, int vencidas, int agotadas);
}
