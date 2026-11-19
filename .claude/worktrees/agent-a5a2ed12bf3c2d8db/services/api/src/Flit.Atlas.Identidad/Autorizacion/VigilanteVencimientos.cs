using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Autorizacion;

/// <summary>Cada cuánto se procesan los vencimientos (sección <c>Identidad:Vencimientos</c>).</summary>
public sealed class OpcionesVencimientos
{
    public const string Seccion = "Identidad:Vencimientos";

    public TimeSpan Intervalo { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Registra los vencimientos de asignaciones de roles (HU 1.4, ATLAS-ADR-0023): cada <see cref="Intervalo"/> toma las
/// asignaciones vencidas que aún no procesó, deja <c>plataforma.asignacion_vencida</c> en la auditoría e invalida las
/// sesiones de esas personas (<see cref="MotivosInvalidacion.Vencimiento"/>), para que la PWA cierre la sesión y borre lo
/// que guardó sin conexión. El corte del acceso <b>no</b> depende de este proceso: los permisos se calculan en cada
/// petición y una asignación vencida ya no cuenta desde el primer segundo después de su vencimiento.
/// Varias réplicas pueden correrlo a la vez: cada fila la toma una sola (<c>FOR UPDATE SKIP LOCKED</c>).
/// </summary>
public sealed partial class VigilanteVencimientos(
    IServiceScopeFactory alcances,
    TimeProvider reloj,
    IOptions<OpcionesVencimientos> opciones,
    ILogger<VigilanteVencimientos> log) : BackgroundService
{
    public TimeSpan Intervalo => opciones.Value.Intervalo;

    private const int Lote = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(Intervalo, reloj);
        while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcesarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Si la base no responde se reintenta en el siguiente ciclo; el acceso ya se cortó en cada petición.
                NoSeProceso(log, ex);
            }
        }
    }

    /// <summary>Procesa las asignaciones vencidas pendientes; devuelve cuántas.</summary>
    public async Task<int> ProcesarAsync(CancellationToken ct = default)
    {
        var total = 0;
        int procesadas;
        do
        {
            procesadas = await ProcesarLoteAsync(ct).ConfigureAwait(false);
            total += procesadas;
        }
        while (procesadas == Lote);
        return total;
    }

    private async Task<int> ProcesarLoteAsync(CancellationToken ct)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var plataforma = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var invalidador = alcance.ServiceProvider.GetRequiredService<IInvalidadorSesiones>();
        var ahora = reloj.GetUtcNow();

        List<AsignacionRol> vencidas;
        await using (var transaccion = await plataforma.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            vencidas = await plataforma.AsignacionesRol
                .FromSql($"""
                    SELECT * FROM atlas.plataforma_asignacion_rol
                    WHERE vigente_hasta IS NOT NULL AND vigente_hasta <= {ahora} AND vencimiento_procesado_en IS NULL
                    ORDER BY vigente_hasta
                    LIMIT {Lote}
                    FOR UPDATE SKIP LOCKED
                    """)
                .IgnoreQueryFilters()
                .ToListAsync(ct).ConfigureAwait(false);
            if (vencidas.Count == 0)
            {
                return 0;
            }
            var roles = await plataforma.Roles.Where(r => vencidas.Select(v => v.RolId).Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Codigo, ct).ConfigureAwait(false);
            foreach (var asignacion in vencidas)
            {
                asignacion.VencimientoProcesadoEn = ahora;
                plataforma.Auditoria.Add(AdministracionRoles.CrearEvento(ahora, AccionesAuditoriaRoles.AsignacionVencida,
                    "asignacion_rol", asignacion.Id, asignacion.OrganizacionId, null,
                    AdministracionRoles.DatosAsignacion(asignacion, roles.GetValueOrDefault(asignacion.RolId, "?"))));
            }
            await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
        }

        // Después de guardar (ATLAS-ADR-0022).
        foreach (var persona in vencidas.Select(v => v.PersonaId).Distinct())
        {
            await invalidador.InvalidarSesionesAsync(persona, MotivosInvalidacion.Vencimiento, null, ct).ConfigureAwait(false);
        }
        Procesadas(log, vencidas.Count);
        return vencidas.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Vencimientos de roles procesados: {Cantidad}")]
    private static partial void Procesadas(ILogger log, int cantidad);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudieron procesar los vencimientos de roles; se reintenta en el siguiente ciclo")]
    private static partial void NoSeProceso(ILogger log, Exception ex);
}
