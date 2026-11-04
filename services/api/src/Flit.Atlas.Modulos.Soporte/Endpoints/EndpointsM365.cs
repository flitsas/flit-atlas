using System.Buffers.Text;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Microsoft365;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

/// <summary>
/// Microsoft 365 del soporte (HU 2.10, ATLAS-ADR-0033): el estado del calendario compartido y de los avisos de Teams, y
/// los feeds .ics personales y de equipo. Los feeds se piden con sesión; su URL lleva un token que se guarda como hash y
/// es lo único que pide <c>GET /ics/{token}.ics</c> (sin sesión: lo leen Outlook, Google Calendar o el calendario del
/// teléfono, que no tienen la cookie).
/// </summary>
internal static class EndpointsM365
{
    public const string RutaIcs = "/ics/";

    /// <summary>Feeds vigentes por persona: uno personal y uno por equipo.</summary>
    public const int MaximoFeeds = 20;

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/microsoft-365", EstadoAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ObtenerEstadoM365Soporte")
            .WithSummary("Estado de Microsoft 365 del soporte")
            .WithDescription(
                "Si el calendario compartido de Outlook y los avisos en el canal de Teams están activos en este ambiente (y, si no, " +
                "por qué), cuántos eventos y avisos esperan sincronizarse y cuándo fue la última sincronización. Sin la " +
                "configuración (local, dev) todo aparece apagado con su motivo. Requiere atlas.soporte.ver.")
            .Produces<EstadoM365Dto>();

        grupo.MapGet("/feeds", ListarFeedsAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("ListarFeedsIcsSoporte")
            .WithSummary("Mis feeds .ics")
            .WithDescription(
                "Los feeds .ics vigentes de la persona en sesión (el personal y los de equipo), sin su URL: el token solo se muestra " +
                "al crearlo. Requiere atlas.soporte.ver.")
            .Produces<List<FeedIcsDto>>();

        grupo.MapPost("/feeds", CrearFeedAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("CrearFeedIcsSoporte")
            .WithSummary("Crear o regenerar un feed .ics")
            .WithDescription(
                "Crea el feed personal (mis turnos) o el de un equipo y devuelve su URL con el token, que no se vuelve a mostrar. Si " +
                "ya había uno igual, lo revoca (regenerar). El feed no trae correos ni teléfonos. Queda en la auditoría. Requiere " +
                "atlas.soporte.ver.")
            .Produces<FeedIcsCreadoDto>(StatusCodes.Status201Created)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        grupo.MapDelete("/feeds/{id:guid}", RevocarFeedAsync)
            .RequierePermiso(PermisosSoporte.Ver)
            .WithName("RevocarFeedIcsSoporte")
            .WithSummary("Revocar un feed .ics")
            .WithDescription("Su URL deja de funcionar de inmediato. Solo los propios. Queda en la auditoría. Requiere atlas.soporte.ver.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorSoporte>(StatusCodes.Status404NotFound);

        // Revisa a la persona dueña del token (empleada, sin organización): alcance del sistema, pedido aquí (ATLAS-ADR-0050).
        grupo.MapGet(RutaIcs + "{token}.ics", FeedAsync)
            .AllowAnonymous()
            .ConAlcanceDelSistemaSinSesion()
            .WithName("ObtenerFeedIcsSoporte")
            .WithSummary("Feed .ics (calendario suscrito)")
            .WithDescription(
                "iCalendar (RFC 5545, zona America/Bogota) con los turnos vigentes del feed, de 90 días atrás a un año adelante. Sin " +
                "sesión: se autentica con el token de la URL, que solo vale mientras no se revoque y la persona que lo creó siga " +
                "activa y con atlas.soporte.ver. Un token desconocido o revocado responde 404.")
            .Produces<string>(StatusCodes.Status200OK, "text/calendar")
            .Produces(StatusCodes.Status404NotFound);
    }

    // --- Estado ---

    private static async Task<IResult> EstadoAsync(
        SoporteDbContext soporte, IAccesoGraph acceso, INotificadorTeams teams, IOptions<OpcionesM365Soporte> opciones, CancellationToken ct)
    {
        var config = opciones.Value;
        string? motivoCalendario = !acceso.Disponible
            ? "Este ambiente no tiene Entra ID configurado: no hay acceso a Microsoft Graph."
            : string.IsNullOrWhiteSpace(config.Buzon)
                ? "Falta el buzón compartido del calendario (Soporte__M365__Buzon) y su permiso en Graph."
                : !config.CalendarioHabilitado ? "La sincronización del calendario está apagada en este ambiente." : null;
        var motivoTeams = teams.Configurado(config.CanalTeams)
            ? null
            : $"Falta la URL del flujo de Workflows del canal de Teams (Plataforma__Teams__Canales__{config.CanalTeams}).";

        var pendientes = await soporte.EventosCalendario.CountAsync(e => e.Pendiente, ct);
        var conErrores = await soporte.EventosCalendario.CountAsync(e => e.Pendiente && e.Intentos > 0, ct);
        var ultima = await soporte.EventosCalendario.MaxAsync(e => (DateTimeOffset?)e.SincronizadoEn, ct);
        var avisos = await soporte.AvisosTeams.CountAsync(a => a.Pendiente, ct);
        return Results.Ok(new EstadoM365Dto(
            new EstadoCalendarioM365Dto(motivoCalendario is null, motivoCalendario, config.NombreCalendario,
                motivoCalendario is null ? pendientes : 0, motivoCalendario is null ? conErrores : 0, ultima),
            new EstadoTeamsM365Dto(motivoTeams is null, motivoTeams, motivoTeams is null ? avisos : 0)));
    }

    // --- Feeds ---

    private static async Task<IResult> ListarFeedsAsync(HttpContext http, SoporteDbContext soporte, CancellationToken ct)
    {
        var yo = PersonaDeSesion(http.User);
        var feeds = await soporte.FeedsIcs.AsNoTracking().Where(f => f.PersonaId == yo && f.RevocadoEn == null)
            .OrderBy(f => f.Tipo).ThenBy(f => f.CreadoEn).ToListAsync(ct);
        var equipos = await NombresDeEquiposAsync(soporte, ct);
        return Results.Ok(feeds.Select(f => Dto(f, equipos)).ToList());
    }

    private static async Task<IResult> CrearFeedAsync(
        [Description("Tipo de feed y, si es de equipo, cuál.")] SolicitudFeedIcs solicitud,
        HttpContext http, SoporteDbContext soporte, PlataformaDbContext plataforma, IConfiguration configuracion, TimeProvider reloj,
        CancellationToken ct)
    {
        var tipo = ReglasTurno.Leer<TipoFeedIcs>(solicitud.Tipo);
        if (tipo is null)
        {
            return Invalido("tipo", $"El tipo va en {ReglasTurno.Valores<TipoFeedIcs>()}.");
        }
        if (tipo == TipoFeedIcs.Equipo && solicitud.EquipoId is null)
        {
            return Invalido("equipoId", "El feed de equipo lleva el equipo.");
        }
        var equipoId = tipo == TipoFeedIcs.Equipo ? solicitud.EquipoId : null;
        if (equipoId is { } e && !await soporte.Equipos.AnyAsync(q => q.Id == e, ct))
        {
            return NoEncontrado("equipo_no_encontrado", "El equipo no existe.");
        }
        var yo = PersonaDeSesion(http.User);
        var ahora = reloj.GetUtcNow();
        var anteriores = await soporte.FeedsIcs.Where(f => f.PersonaId == yo && f.Tipo == tipo && f.EquipoId == equipoId && f.RevocadoEn == null).ToListAsync(ct);
        if (anteriores.Count == 0 && await soporte.FeedsIcs.CountAsync(f => f.PersonaId == yo && f.RevocadoEn == null, ct) >= MaximoFeeds)
        {
            return Invalido("tipo", $"Tienes {MaximoFeeds} feeds vigentes: revoca alguno antes de crear otro.");
        }
        foreach (var anterior in anteriores)
        {
            anterior.RevocadoEn = ahora;
        }
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var feed = new FeedIcs { PersonaId = yo, Tipo = tipo.Value, EquipoId = equipoId, TokenHash = Hash(token) };
        Func<Task>? antes = anteriores.Count == 0 ? null : async () =>
        {
            // Primero se revocan los anteriores (índice único de un feed vigente por persona, tipo y equipo).
            await soporte.SaveChangesAsync(ct);
            soporte.FeedsIcs.Add(feed);
        };
        if (antes is null)
        {
            soporte.FeedsIcs.Add(feed);
        }
        await GuardarAsync(soporte, plataforma,
            [Evento(http, reloj, anteriores.Count == 0 ? "feed_ics_creado" : "feed_ics_regenerado", "soporte_feed_ics", feed.Id,
                new { tipo = ReglasTurno.Texto(feed.Tipo), equipoId, revocados = anteriores.Select(a => a.Id) })], ct, antes);

        var url = UrlFeed(http, configuracion, token);
        var equipos = await NombresDeEquiposAsync(soporte, ct);
        var dto = Dto(feed, equipos);
        return Results.Created($"/api/v1/soporte/feeds/{feed.Id}",
            new FeedIcsCreadoDto(feed.Id, dto.Tipo, dto.Equipo, url, "webcal://" + url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..], feed.CreadoEn));
    }

    private static async Task<IResult> RevocarFeedAsync(
        [Description("Id del feed.")] Guid id,
        HttpContext http, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var yo = PersonaDeSesion(http.User);
        var feed = await soporte.FeedsIcs.FirstOrDefaultAsync(f => f.Id == id && f.PersonaId == yo && f.RevocadoEn == null, ct);
        if (feed is null)
        {
            return NoEncontrado("feed_no_encontrado", "El feed no existe o ya está revocado.");
        }
        feed.RevocadoEn = reloj.GetUtcNow();
        await GuardarAsync(soporte, plataforma, [Evento(http, reloj, "feed_ics_revocado", "soporte_feed_ics", feed.Id, new { tipo = ReglasTurno.Texto(feed.Tipo), feed.EquipoId })], ct);
        return Results.NoContent();
    }

    private static async Task<IResult> FeedAsync(
        [Description("Token del feed (43 caracteres base64url).")] string token,
        HttpContext http, SoporteDbContext soporte, PlataformaDbContext plataforma, CalculadoraPermisos calculadora,
        IOptions<OpcionesM365Soporte> opciones, TimeProvider reloj, CancellationToken ct)
    {
        if (token.Length != 43 || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return Results.NotFound();
        }
        var hash = Hash(token);
        var feed = await soporte.FeedsIcs.AsNoTracking().FirstOrDefaultAsync(f => f.TokenHash == hash && f.RevocadoEn == null, ct);
        if (feed is null || !(await calculadora.CalcularAsync(feed.PersonaId, ct)).Tiene(PermisosSoporte.Ver))
        {
            return Results.NotFound();
        }

        var config = opciones.Value;
        var hoy = CalendarioBogota.Hoy(reloj);
        var desde = hoy.AddDays(-config.DiasFeedAtras);
        var hasta = hoy.AddDays(config.DiasFeedAdelante);
        var consulta = EndpointsTurnos.TurnosVigentes(soporte).AsNoTracking().Where(t => t.Fecha >= desde && t.Fecha <= hasta);
        consulta = feed.Tipo == TipoFeedIcs.Personal ? consulta.Where(t => t.PersonaId == feed.PersonaId) : consulta.Where(t => t.EquipoId == feed.EquipoId);
        var turnos = await consulta.OrderBy(t => t.Fecha).ThenBy(t => t.Id).ToListAsync(ct);
        var equipos = await soporte.Equipos.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var configuracion = await ConfiguracionAsync(soporte, ct);
        var personas = feed.Tipo == TipoFeedIcs.Equipo ? await PersonasAsync(plataforma, turnos.Select(t => t.PersonaId), ct) : [];

        var eventos = turnos.Select(t =>
        {
            var equipo = equipos[t.EquipoId];
            var (inicioEquipo, finEquipo) = HorarioDe(equipo, configuracion);
            var inicio = t.HoraInicio ?? inicioEquipo;
            var fin = t.HoraFin ?? finEquipo;
            var resumen = feed.Tipo == TipoFeedIcs.Personal ? $"Soporte {equipo.Nombre}" : $"Soporte {equipo.Nombre}: {NombreDe(personas, t.PersonaId)}";
            return new EventoIcs(t.Id, t.Fecha, inicio, fin, resumen,
                $"Turno de soporte de los sábados del equipo {equipo.Nombre}, de {Hora(inicio)} a {Hora(fin)} (hora de Colombia). Flit Atlas.",
                t.Estado != EstadoTurno.Programado, t.ActualizadoEn);
        });
        var nombre = feed.Tipo == TipoFeedIcs.Personal
            ? $"{config.NombreCalendario}: mis turnos"
            : $"{config.NombreCalendario}: {(feed.EquipoId is { } id && equipos.TryGetValue(id, out var q) ? q.Nombre : "equipo")}";
        var ics = CalendarioIcs.Generar(nombre, eventos, reloj.GetUtcNow());

        // Último uso, a lo sumo una escritura por hora (los clientes refrescan seguido).
        var hace = reloj.GetUtcNow().AddHours(-1);
        await soporte.FeedsIcs.Where(f => f.Id == feed.Id && (f.UltimoUsoEn == null || f.UltimoUsoEn < hace))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.UltimoUsoEn, reloj.GetUtcNow()), ct);

        http.Response.Headers.CacheControl = "private, max-age=300";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.Text(ics, "text/calendar", Encoding.UTF8);
    }

    /// <summary>SHA-256 del token en hexadecimal en minúscula: lo único que se guarda.</summary>
    internal static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>URL absoluta del feed: con <c>Atlas:UrlPublica</c> si el ambiente la tiene; si no, la de la petición.</summary>
    private static string UrlFeed(HttpContext http, IConfiguration configuracion, string token)
    {
        var raiz = configuracion["Atlas:UrlPublica"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(raiz))
        {
            raiz = $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";
        }
        return $"{raiz}/api/v1/{ModuloSoporte.IdModulo}{RutaIcs}{token}.ics";
    }

    private static FeedIcsDto Dto(FeedIcs f, IReadOnlyDictionary<Guid, string> equipos) =>
        new(f.Id, ReglasTurno.Texto(f.Tipo),
            f.EquipoId is { } e ? new EquipoResumenDto(e, equipos.GetValueOrDefault(e) ?? "Equipo") : null,
            f.CreadoEn, f.UltimoUsoEn);
}
