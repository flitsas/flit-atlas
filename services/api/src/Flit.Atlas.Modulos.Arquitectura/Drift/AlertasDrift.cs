using System.Globalization;
using System.Text.Json;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Modulos.Arquitectura.Drift;

/// <summary>
/// Alertas de drift (HU 6.5, ATLAS-ADR-0056 §6) con el centro de notificaciones que ya existe: bandeja, push y correo, según las
/// preferencias de cada persona. Le llegan al líder técnico y a los responsables vigentes del aplicativo del hallazgo o de su ancestro
/// más cercano con responsables; solo a empleados activos (§7.3: los externos nunca ven drift). El texto dice cuántos hallazgos hay y en
/// qué ambiente, sin workloads, clústeres ni hosts: un responsable puede no tener <c>ver_despliegue</c>. Cada hallazgo alerta una vez.
/// </summary>
internal sealed partial class AlertasDrift(
    ArquitecturaDbContext contexto,
    PlataformaDbContext plataforma,
    ILectorAplicativos lector,
    ILectorResponsables responsables,
    ServicioNotificaciones notificaciones,
    IEnviadorCorreo correo,
    IConfiguration configuracion,
    TimeProvider reloj,
    ILogger<AlertasDrift> log)
{
    public const string RecursoAuditoria = "arquitectura.drift";
    public const string AccionAlerta = "arquitectura.alerta_drift_enviada";

    private static readonly HashSet<CanalNotificacion> Canales = [CanalNotificacion.App, CanalNotificacion.Push, CanalNotificacion.Correo];

    public static readonly TipoNotificacion Tipo = new(
        "arquitectura.alerta_drift", "Alertas de drift de arquitectura",
        "Cuando lo declarado en el modelo, en gitops y en Argo CD deja de coincidir en un aplicativo del que respondes.",
        Canales, Canales);

    /// <summary>Alerta los hallazgos abiertos sin alerta. Devuelve a cuántas personas se les avisó.</summary>
    public async Task<int> EnviarPendientesAsync(CancellationToken ct)
    {
        var pendientes = await contexto.Hallazgos.Where(h => h.Estado == EstadoHallazgo.Abierto && h.AlertadoEn == null).ToListAsync(ct).ConfigureAwait(false);
        if (pendientes.Count == 0)
        {
            return 0;
        }
        var lideres = await LideresAsync(ct).ConfigureAwait(false);
        var porAplicativo = await ResponsablesPorAplicativoAsync(pendientes, ct).ConfigureAwait(false);
        var ids = lideres.Concat(porAplicativo.Values.SelectMany(p => p)).Distinct().ToList();
        var personas = await plataforma.Personas.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa)
            .ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);

        var avisos = 0;
        foreach (var grupo in pendientes.GroupBy(h => h.Ambiente, StringComparer.Ordinal))
        {
            // De cada persona, solo lo suyo; el líder técnico, todo.
            var porPersona = new Dictionary<Guid, List<HallazgoDrift>>();
            foreach (var hallazgo in grupo)
            {
                var destinos = lideres.Concat(hallazgo.AplicativoId is { } a && porAplicativo.TryGetValue(a, out var r) ? r : []).Where(personas.ContainsKey).Distinct();
                foreach (var persona in destinos)
                {
                    (porPersona.TryGetValue(persona, out var lista) ? lista : porPersona[persona] = []).Add(hallazgo);
                }
            }
            foreach (var (id, hallazgos) in porPersona)
            {
                if (await AvisarAsync(personas[id], grupo.Key, hallazgos, ct).ConfigureAwait(false))
                {
                    avisos++;
                }
            }
        }
        var ahora = reloj.GetUtcNow();
        foreach (var hallazgo in pendientes)
        {
            hallazgo.AlertadoEn = ahora;
        }
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return avisos;
    }

    private async Task<List<Guid>> LideresAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        return await (from asignacion in plataforma.AsignacionesRol.AsNoTracking()
                      join rol in plataforma.Roles.AsNoTracking() on asignacion.RolId equals rol.Id
                      where rol.Codigo == RolesAtlas.LiderTecnico && asignacion.AlcanceTipo == TiposAlcance.Global
                          && asignacion.VigenteDesde <= ahora && (asignacion.VigenteHasta == null || asignacion.VigenteHasta > ahora)
                      select asignacion.PersonaId).Distinct().ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Por aplicativo, sus responsables o los de su ancestro más cercano que los tenga.</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<Guid>>> ResponsablesPorAplicativoAsync(IReadOnlyList<HallazgoDrift> hallazgos, CancellationToken ct)
    {
        var pedidos = hallazgos.Select(h => h.AplicativoId).OfType<Guid>().Distinct().ToList();
        if (pedidos.Count == 0)
        {
            return [];
        }
        var aplicativos = (await lector.ListarAsync(ct).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var cadenas = pedidos.ToDictionary(a => a, a => Cadena(a, aplicativos));
        var leidos = await responsables.DeAplicativosAsync([.. cadenas.Values.SelectMany(c => c).Distinct()], ct).ConfigureAwait(false);
        return cadenas.ToDictionary(c => c.Key, c => c.Value.Select(a => leidos.GetValueOrDefault(a)).FirstOrDefault(r => r is { Count: > 0 }) ?? []);
    }

    private static List<Guid> Cadena(Guid aplicativo, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        var cadena = new List<Guid> { aplicativo };
        var actual = aplicativos.GetValueOrDefault(aplicativo);
        while (actual?.PadreId is { } padre && !cadena.Contains(padre))
        {
            cadena.Add(padre);
            actual = aplicativos.GetValueOrDefault(padre);
        }
        return cadena;
    }

    /// <summary>Una falla de un canal no frena a los demás ni a las otras personas: queda en el log, solo con su tipo.</summary>
    private async Task<bool> AvisarAsync(Persona persona, string ambiente, List<HallazgoDrift> hallazgos, CancellationToken ct)
    {
        var alta = hallazgos.Any(h => h.Severidad == SeveridadDrift.Alta);
        var cuantos = hallazgos.Count.ToString(CultureInfo.InvariantCulture);
        var titulo = alta ? $"Drift de severidad alta en {ambiente}" : $"Drift de arquitectura en {ambiente}";
        var cuerpo = hallazgos.Count == 1
            ? $"Atlas encontró 1 hallazgo nuevo de drift en {ambiente}. Revísalo en el panel de drift."
            : $"Atlas encontró {cuantos} hallazgos nuevos de drift en {ambiente}. Revísalos en el panel de drift.";
        var enlace = $"/arquitectura/drift?ambiente={ambiente}";
        var salio = false;
        try
        {
            await notificaciones.NotificarAsync(persona.Id, Tipo, titulo, cuerpo, enlace, ct).ConfigureAwait(false);
            salio = true;
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            Fallo(log, "app_push", error.GetType().Name);
        }
        if (CatalogoNotificaciones.Disponible(Tipo, CanalNotificacion.Correo, correo)
            && await notificaciones.CanalActivoAsync(persona.Id, Tipo, CanalNotificacion.Correo, ct).ConfigureAwait(false))
        {
            try
            {
                await correo.EnviarAsync(Correo(persona, titulo, cuerpo, enlace), ct).ConfigureAwait(false);
                salio = true;
            }
            catch (CorreoNoEnviadoException error)
            {
                Fallo(log, "correo", error.GetType().Name);
            }
        }
        if (salio)
        {
            plataforma.Auditoria.Add(new EventoAuditoria
            {
                OcurridoEn = reloj.GetUtcNow(),
                ActorTipo = TipoActor.Sistema,
                ActorId = "arquitectura.alertas_drift",
                OrganizacionId = persona.OrganizacionId,
                Accion = AccionAlerta,
                RecursoTipo = RecursoAuditoria,
                RecursoId = ambiente,
                Datos = JsonSerializer.Serialize(new { persona = persona.Id, hallazgos = hallazgos.Select(h => h.Id).ToList() }, JsonSerializerOptions.Web),
            });
            await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return salio;
    }

    private MensajeCorreo Correo(Persona persona, string titulo, string cuerpo, string enlace)
    {
        var url = configuracion["Atlas:UrlPublica"];
        var baseUrl = string.IsNullOrWhiteSpace(url) ? "https://localhost:4032" : url.TrimEnd('/');
        var nombre = persona.Nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? persona.Nombre;
        var (html, texto) = PlantillaCorreo.Armar(titulo, [$"Hola, {nombre}.", cuerpo], "Ver el panel de drift", $"{baseUrl}{enlace}",
            ["Recibes este correo porque tienes activas las alertas de drift por correo. Puedes apagarlas en Atlas, en Ajustes, sección Notificaciones."]);
        return new MensajeCorreo(persona.Correo, persona.Nombre, titulo, html, texto);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerta de drift: el canal {Canal} falló ({Tipo})")]
    private static partial void Fallo(ILogger log, string canal, string tipo);
}
