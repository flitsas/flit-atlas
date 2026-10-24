using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Equipo;

/// <summary>Configuración de la revisión trimestral (sección <c>Equipo:Revision</c>).</summary>
public sealed class OpcionesRevision
{
    public const string Seccion = "Equipo:Revision";

    /// <summary>Apaga solo el trabajo programado; la revisión y su fecha siguen en la pantalla.</summary>
    public bool Habilitada { get; set; } = true;

    /// <summary>Cuántos días antes de la revisión sale el recordatorio.</summary>
    public int DiasAnticipacion { get; set; } = 7;

    /// <summary>Cada cuánto revisa si toca recordar.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Recordatorio de la revisión trimestral del reparto (§8.2, HU 2.3, ATLAS-ADR-0031): N días antes de la revisión
/// pendiente, una notificación en el centro de notificaciones (bandeja y push, HU 1.6) a quien gestiona el equipo
/// (<c>atlas.equipo.gestionar</c> con alcance global, salvo el superadministrador). El correo y Teams llegan con la HU 2.11.
/// Sale una sola vez aunque haya varias réplicas: la marca se pone con una actualización condicionada.
/// </summary>
public static class RecordatorioRevision
{
    public static readonly TipoNotificacion Tipo = new(
        "equipo.revision_trimestral", "Revisión del reparto de responsables",
        "Aviso unos días antes de la revisión trimestral de responsables principal y secundario.",
        new HashSet<CanalNotificacion> { CanalNotificacion.App, CanalNotificacion.Push });

    /// <summary>Envía el recordatorio si toca; devuelve a cuántas personas se notificó.</summary>
    public static async Task<int> EnviarSiCorrespondeAsync(IServiceProvider servicios, int diasAnticipacion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var equipo = servicios.GetRequiredService<EquipoDbContext>();
        var reloj = servicios.GetRequiredService<TimeProvider>();
        var hoy = CalendarioBogota.Hoy(reloj);
        var limite = hoy.AddDays(Math.Max(0, diasAnticipacion));
        var revision = await equipo.Revisiones.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Estado == EstadoRevision.Pendiente && r.RecordatorioEnviadoEn == null && r.Fecha <= limite, ct)
            .ConfigureAwait(false);
        if (revision is null)
        {
            return 0;
        }
        var ahora = reloj.GetUtcNow();
        // Solo una réplica gana la marca.
        var marcadas = await equipo.Revisiones
            .Where(r => r.Id == revision.Id && r.RecordatorioEnviadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RecordatorioEnviadoEn, ahora).SetProperty(r => r.ActualizadoEn, ahora), ct)
            .ConfigureAwait(false);
        if (marcadas == 0)
        {
            return 0;
        }

        var destinatarios = await DestinatariosAsync(servicios.GetRequiredService<PlataformaDbContext>(),
            servicios.GetRequiredService<CatalogoPermisos>(), ahora, ct).ConfigureAwait(false);
        var notificaciones = servicios.GetRequiredService<ServicioNotificaciones>();
        var cuando = revision.Fecha == hoy ? "hoy" : $"el {Textos.Fecha(revision.Fecha)}";
        foreach (var persona in destinatarios)
        {
            await notificaciones.NotificarAsync(persona, Tipo, "Revisión trimestral de responsables",
                $"La revisión del reparto de responsables principal y secundario es {cuando}. Revisa la carga y el historial antes.",
                "/equipo/responsables?vista=historial", ct).ConfigureAwait(false);
        }
        return destinatarios.Count;
    }

    /// <summary>Empleados activos con <c>atlas.equipo.gestionar</c> global por una asignación vigente (no el superadministrador).</summary>
    internal static async Task<List<Guid>> DestinatariosAsync(PlataformaDbContext plataforma, CatalogoPermisos catalogo, DateTimeOffset ahora, CancellationToken ct)
    {
        var roles = catalogo.Roles
            .Where(r => r.Codigo != RolesAtlas.Superadmin && r.Tipo == TipoRol.Interno && catalogo.PermisosDe(r.Codigo).Contains(PermisosEquipo.Gestionar))
            .Select(r => r.Codigo).ToList();
        return await plataforma.AsignacionesRol.AsNoTracking()
            .Where(a => a.AlcanceTipo == TiposAlcance.Global && a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora))
            .Join(plataforma.Roles.Where(r => roles.Contains(r.Codigo)), a => a.RolId, r => r.Id, (a, r) => a.PersonaId)
            .Join(plataforma.Personas.Where(p => p.Estado == EstadoPersona.Activa && p.Tipo == TipoPersona.Empleado), id => id, p => p.Id, (id, p) => p.Id)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}

/// <summary>Trabajo en segundo plano que revisa cada hora si toca el recordatorio de la revisión trimestral.</summary>
internal sealed partial class TrabajoRecordatorioRevision(
    IServiceScopeFactory alcances, IOptions<OpcionesRevision> opciones, TimeProvider reloj, ILogger<TrabajoRecordatorioRevision> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Value.Habilitada)
        {
            return;
        }
        using var temporizador = new PeriodicTimer(opciones.Value.Intervalo, reloj);
        do
        {
            try
            {
                await using var alcance = alcances.CreateAsyncScope();
                var enviadas = await RecordatorioRevision.EnviarSiCorrespondeAsync(alcance.ServiceProvider, opciones.Value.DiasAnticipacion, stoppingToken)
                    .ConfigureAwait(false);
                if (enviadas > 0)
                {
                    Enviado(log, enviadas);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // La base o el push no respondieron: se intenta en la vuelta siguiente. Si falló después de marcar, no se
                // repite (mejor un aviso de menos que uno duplicado; la revisión sigue visible en la pantalla).
                Fallo(log, error.GetType().Name);
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Equipo: recordatorio de la revisión trimestral enviado a {Personas} personas")]
    private static partial void Enviado(ILogger log, int personas);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Equipo: el recordatorio de la revisión trimestral falló ({Error}); se reintenta en la siguiente vuelta")]
    private static partial void Fallo(ILogger log, string error);
}
