using System.Text.Json;
using System.Threading.Channels;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Arquitectura.Drift;

/// <summary>Conteos de una corrida.</summary>
public sealed record ResultadoDrift(int Abiertos, int Resueltos, int Alertados);

/// <summary>Pide revisar un ambiente al publicar: lo atiende el trabajo, fuera de la petición (§5.1).</summary>
internal sealed class DisparadorDrift
{
    private readonly Channel<string> _pedidos = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Pedir(string ambiente) => _pedidos.Writer.TryWrite(ambiente);

    public ChannelReader<string> Pedidos => _pedidos.Reader;
}

/// <summary>
/// Calcula el drift (plan 01 §11.4, ATLAS-ADR-0056 §6) y guarda sus hallazgos. La regla que manda: <b>sin datos no se resuelve nada</b>.
/// Sin modelo, sin snapshot o sin lectura de Argo CD no se comparan esas capas, y lo que ya estaba abierto sigue abierto. Un nodo a la
/// vez (candado consultivo).
/// </summary>
internal sealed partial class ServicioDrift(
    ArquitecturaDbContext contexto, ILectorArgoCd argo, AlertasDrift alertas, IOptions<OpcionesArquitectura> opciones, TimeProvider reloj, ILogger<ServicioDrift> log)
{
    public static readonly IReadOnlyList<string> Ambientes = ["dev", "qa", "pdn"];

    /// <summary>Llave del candado consultivo de sesión del drift.</summary>
    internal const long Candado = 0x41544C4153_0605;

    /// <summary>Corre todo, o un solo ambiente; <c>null</c> si otra réplica tiene el candado.</summary>
    public async Task<ResultadoDrift?> CorrerAsync(string? soloAmbiente, CancellationToken ct)
    {
        await contexto.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var tomado = await contexto.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({Candado}) AS \"Value\"").SingleAsync(ct).ConfigureAwait(false);
            if (!tomado)
            {
                return null;
            }
            try
            {
                return await CorrerConCandadoAsync(soloAmbiente, ct).ConfigureAwait(false);
            }
            finally
            {
                await contexto.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({Candado})", CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            await contexto.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task<ResultadoDrift> CorrerConCandadoAsync(string? soloAmbiente, CancellationToken ct)
    {
        var (abiertos, resueltos) = (0, 0);
        var lecturas = new Dictionary<string, LecturaArgoCd>(StringComparer.Ordinal);
        foreach (var ambiente in Ambientes.Where(a => soloAmbiente is null || a == soloAmbiente))
        {
            var snapshots = await SnapshotsVigentesAsync(ambiente, ct).ConfigureAwait(false);
            var modelos = await ModelosVigentesAsync(ambiente, ct).ConfigureAwait(false);
            if (snapshots.Count > 0 && modelos.Count > 0)
            {
                var hallazgos = ReglasDrift.ModeloContraGitops(ambiente, await EsperadosAsync(modelos, ct).ConfigureAwait(false), [.. snapshots.Select(s => s.Snapshot)],
                    DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime), snapshots[0].AplicativoId, snapshots[0].Visibilidad);
                var (a, r) = await AplicarAsync(CapaDrift.ModeloGitops, ambiente, null, hallazgos, ct).ConfigureAwait(false);
                (abiertos, resueltos) = (abiertos + a, resueltos + r);
                await GuardarComparacionAsync(ambiente, ct).ConfigureAwait(false);
            }
            foreach (var snapshot in snapshots)
            {
                var cluster = snapshot.Snapshot.Cluster.Id;
                if (!lecturas.TryGetValue(cluster, out var lectura))
                {
                    lectura = await argo.LeerAsync(cluster, ct).ConfigureAwait(false);
                    lecturas[cluster] = lectura;
                    await GuardarLecturaAsync(cluster, lectura, ct).ConfigureAwait(false);
                }
                if (lectura.Resultado != ResultadoLecturaArgo.Correcta)
                {
                    continue;
                }
                var hallazgos = ReglasDrift.GitopsContraArgo(ambiente, snapshot.Snapshot, lectura.Aplicaciones, reloj.GetUtcNow(), opciones.Value.ToleranciaReconciliacion, snapshot.AplicativoId, snapshot.Visibilidad);
                var (a, r) = await AplicarAsync(CapaDrift.GitopsArgo, ambiente, cluster, hallazgos, ct).ConfigureAwait(false);
                (abiertos, resueltos) = (abiertos + a, resueltos + r);
            }
        }
        var alertados = await alertas.EnviarPendientesAsync(ct).ConfigureAwait(false);
        Corrida(log, abiertos, resueltos, alertados);
        return new ResultadoDrift(abiertos, resueltos, alertados);
    }

    internal sealed record SnapshotVigente(SnapshotDespliegue Snapshot, Guid? AplicativoId, VisibilidadArquitectura Visibilidad);

    /// <summary>El último snapshot de cada clúster del ambiente.</summary>
    private async Task<List<SnapshotVigente>> SnapshotsVigentesAsync(string ambiente, CancellationToken ct)
    {
        var deAmbiente = contexto.Publicaciones.AsNoTracking().Where(p => p.Tipo == TipoPublicacion.Despliegue && p.Canal == ambiente);
        var filas = await deAmbiente
            .Where(p => !deAmbiente.Any(q => q.Cluster == p.Cluster && (q.PublicadoEn > p.PublicadoEn || (q.PublicadoEn == p.PublicadoEn && q.Id.CompareTo(p.Id) > 0))))
            .Join(contexto.Proyectos.AsNoTracking(), p => p.ProyectoId, q => q.Id, (p, q) => new { p.Contenido, p.Visibilidad, q.AplicativoId })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. filas.Select(f => new SnapshotVigente(ReglasSnapshot.Parsear(f.Contenido), f.AplicativoId, f.Visibilidad))];
    }

    private sealed record ModeloVigente(Guid PublicacionId, Guid AplicativoId, VisibilidadArquitectura Visibilidad);

    /// <summary>El último modelo de cada proyecto en el canal del ambiente.</summary>
    private async Task<List<ModeloVigente>> ModelosVigentesAsync(string ambiente, CancellationToken ct)
    {
        var modelos = contexto.Publicaciones.AsNoTracking().Where(p => p.Tipo == TipoPublicacion.Modelo && p.Canal == ambiente);
        return await modelos
            .Where(p => !modelos.Any(q => q.ProyectoId == p.ProyectoId && (q.PublicadoEn > p.PublicadoEn || (q.PublicadoEn == p.PublicadoEn && q.Id.CompareTo(p.Id) > 0))))
            .Join(contexto.Proyectos.AsNoTracking(), p => p.ProyectoId, q => q.Id, (p, q) => new ModeloVigente(p.Id, q.AplicativoId, p.Visibilidad))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<ElementoEsperado>> EsperadosAsync(IReadOnlyList<ModeloVigente> modelos, CancellationToken ct)
    {
        var ids = modelos.Select(m => m.PublicacionId).ToList();
        var filas = await contexto.Elementos.AsNoTracking().Where(e => ids.Contains(e.PublicacionId) && e.K8sWorkload != null)
            .Select(e => new { e.PublicacionId, e.ElementoId, e.K8sWorkload, e.Etiquetas, e.Metadatos }).ToListAsync(ct).ConfigureAwait(false);
        var porId = modelos.ToDictionary(m => m.PublicacionId);
        return
        [
            .. filas.Select(f =>
            {
                var modelo = porId[f.PublicacionId];
                var metadatos = JsonSerializer.Deserialize<Dictionary<string, IReadOnlyList<string>>>(f.Metadatos);
                var ambientes = metadatos?.GetValueOrDefault("ambientes") is { Count: > 0 } lista ? string.Join(',', lista) : null;
                return new ElementoEsperado(f.ElementoId, f.K8sWorkload!, ambientes, f.Etiquetas, modelo.AplicativoId, modelo.Visibilidad);
            }),
        ];
    }

    /// <summary>
    /// Guarda el resultado de una comparación que sí se pudo hacer: abre lo nuevo, reabre lo que había vuelto y resuelve lo que ya no
    /// apareció, solo dentro de esta capa, este ambiente y este clúster.
    /// </summary>
    private async Task<(int Abiertos, int Resueltos)> AplicarAsync(CapaDrift capa, string ambiente, string? cluster, IReadOnlyList<HallazgoCalculado> calculados, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var existentes = await contexto.Hallazgos.Where(h => h.Capa == capa && h.Ambiente == ambiente && h.Cluster == cluster).ToListAsync(ct).ConfigureAwait(false);
        var porClave = existentes.ToDictionary(h => h.Clave, StringComparer.Ordinal);
        var actuales = new HashSet<string>(StringComparer.Ordinal);
        var abiertos = 0;
        foreach (var calculado in calculados.DistinctBy(ReglasDrift.Clave))
        {
            var clave = ReglasDrift.Clave(calculado);
            actuales.Add(clave);
            if (!porClave.TryGetValue(clave, out var hallazgo))
            {
                contexto.Hallazgos.Add(new HallazgoDrift
                {
                    Clave = clave, Capa = capa, Ambiente = ambiente, Cluster = cluster, Codigo = calculado.Codigo, ElementoId = calculado.ElementoId, Sujeto = calculado.Sujeto,
                    Descripcion = calculado.Descripcion, Severidad = calculado.Severidad, Visibilidad = calculado.Visibilidad, AplicativoId = calculado.AplicativoId,
                    Estado = EstadoHallazgo.Abierto, DetectadoEn = ahora,
                });
                abiertos++;
                continue;
            }
            hallazgo.ElementoId = calculado.ElementoId;
            hallazgo.Sujeto = calculado.Sujeto;
            hallazgo.Descripcion = calculado.Descripcion;
            hallazgo.Severidad = calculado.Severidad;
            hallazgo.Visibilidad = calculado.Visibilidad;
            hallazgo.AplicativoId = calculado.AplicativoId;
            if (hallazgo.Estado == EstadoHallazgo.Resuelto)
            {
                hallazgo.Estado = EstadoHallazgo.Abierto;
                hallazgo.DetectadoEn = ahora;
                hallazgo.ResueltoEn = null;
                hallazgo.AlertadoEn = null;
                abiertos++;
            }
        }
        var resueltos = 0;
        foreach (var hallazgo in existentes.Where(h => h.Estado == EstadoHallazgo.Abierto && !actuales.Contains(h.Clave)))
        {
            hallazgo.Estado = EstadoHallazgo.Resuelto;
            hallazgo.ResueltoEn = ahora;
            resueltos++;
        }
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return (abiertos, resueltos);
    }

    private async Task GuardarComparacionAsync(string ambiente, CancellationToken ct)
    {
        var fila = await contexto.Comparaciones.FirstOrDefaultAsync(c => c.Ambiente == ambiente, ct).ConfigureAwait(false);
        if (fila is null)
        {
            contexto.Comparaciones.Add(new ComparacionModeloGitops { Ambiente = ambiente, ComparadaEn = reloj.GetUtcNow() });
        }
        else
        {
            fila.ComparadaEn = reloj.GetUtcNow();
        }
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task GuardarLecturaAsync(string cluster, LecturaArgoCd lectura, CancellationToken ct)
    {
        var fila = await contexto.LecturasArgo.FirstOrDefaultAsync(l => l.Cluster == cluster, ct).ConfigureAwait(false);
        if (fila is null)
        {
            contexto.LecturasArgo.Add(new LecturaArgo { Cluster = cluster, Resultado = lectura.Resultado, Motivo = lectura.Motivo, LeidaEn = reloj.GetUtcNow() });
        }
        else
        {
            fila.Resultado = lectura.Resultado;
            fila.Motivo = lectura.Motivo;
            fila.LeidaEn = reloj.GetUtcNow();
        }
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Drift de arquitectura: {Abiertos} abiertos, {Resueltos} resueltos, {Alertados} alertas")]
    private static partial void Corrida(ILogger log, int abiertos, int resueltos, int alertados);
}

/// <summary>Cada <see cref="OpcionesArquitectura.IntervaloDrift"/> revisa todo y, entre vueltas, lo que pide una publicación (plan 01 D-4: <c>BackgroundService</c>).</summary>
internal sealed partial class TrabajoDrift(
    IServiceScopeFactory alcances, DisparadorDrift disparador, IOptions<OpcionesArquitectura> opciones, TimeProvider reloj, ILogger<TrabajoDrift> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Value.DriftHabilitado)
        {
            return;
        }
        var siguiente = reloj.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            if (reloj.GetUtcNow() >= siguiente)
            {
                await CorrerAsync(null, stoppingToken).ConfigureAwait(false);
                siguiente = reloj.GetUtcNow() + opciones.Value.IntervaloDrift;
            }
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var restante = siguiente - reloj.GetUtcNow();
            espera.CancelAfter(restante > TimeSpan.FromSeconds(1) ? restante : TimeSpan.FromSeconds(1));
            try
            {
                var ambiente = await disparador.Pedidos.ReadAsync(espera.Token).ConfigureAwait(false);
                await CorrerAsync(ambiente, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Se cumplió el intervalo: la vuelta siguiente revisa todo.
            }
        }
    }

    private async Task CorrerAsync(string? ambiente, CancellationToken ct)
    {
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            await alcance.ServiceProvider.GetRequiredService<ServicioDrift>().CorrerAsync(ambiente, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // La base o el correo no respondieron: se reintenta en la vuelta siguiente.
            Fallo(log, error.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drift de arquitectura: falló una corrida ({Tipo})")]
    private static partial void Fallo(ILogger log, string tipo);
}
