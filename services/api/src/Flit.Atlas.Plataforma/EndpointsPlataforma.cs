using System.Buffers.Text;
using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Plataforma;

/// <summary>
/// Endpoints del núcleo para el shell de la PWA (HU 1.6): banderas de módulo, centro de notificaciones,
/// preferencias y suscripciones Web Push por dispositivo. Todos exigen sesión y solo tocan datos de la persona.
/// </summary>
public static class EndpointsPlataforma
{
    /// <summary>Mismos nombres de claim que <c>ClaimsAtlas</c> de Identidad (lo verifica una prueba).</summary>
    public const string ClaimPersona = "persona_id";
    public const string ClaimOrganizacion = "organizacion_id";

    public const int LimiteBandeja = 100;

    [Description("Notificación del centro de notificaciones.")]
    public sealed record NotificacionDto(
        Guid Id, string Tipo, string Titulo, string Cuerpo, string? Enlace, DateTimeOffset CreadaEn, DateTimeOffset? LeidaEn);

    [Description("Bandeja de la persona: total sin leer y las más recientes primero.")]
    public sealed record BandejaDto(int NoLeidas, IReadOnlyList<NotificacionDto> Elementos);

    [Description("Estado de un canal para un tipo de notificación.")]
    public sealed record PreferenciaCanalDto(
        [property: Description("app, push, correo o teams.")] string Canal, bool Activa, bool Disponible, bool Editable);

    [Description("Tipo de notificación con sus canales.")]
    public sealed record PreferenciaTipoDto(string Tipo, string Nombre, string Descripcion, IReadOnlyList<PreferenciaCanalDto> Canales);

    [Description("Cambio de una preferencia: activar o apagar un canal para un tipo.")]
    public sealed record CambioPreferencia(string Tipo, [property: Description("push (app no se puede apagar).")] string Canal, bool Activa);

    [Description("Llave pública VAPID para suscribir el dispositivo; habilitado es falso si el ambiente no tiene llaves.")]
    public sealed record ClavePushDto(bool Habilitado, string? ClavePublica);

    [Description("Suscripción Web Push de un dispositivo (PushSubscription del navegador).")]
    public sealed record SuscripcionDto(
        [property: Description("URL https del servicio de push del navegador.")] string Endpoint,
        [property: Description("Llave pública del navegador, base64url (65 bytes).")] string P256dh,
        [property: Description("Secreto de autenticación, base64url (16 bytes).")] string Auth,
        [property: Description("Resumen del dispositivo, p. ej. «Chrome en Android».")] string? Dispositivo);

    [Description("Suscripción que se da de baja.")]
    public sealed record BajaSuscripcionDto(string Endpoint);

    [Description("Resultado de la notificación de prueba.")]
    public sealed record PruebaPushDto([property: Description("Dispositivos a los que llegó el push.")] int Dispositivos);

    public static IEndpointRouteBuilder MapPlataforma(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        // Valida la configuración de Web Push al arrancar (nombra las variables que falten).
        _ = rutas.ServiceProvider.GetRequiredService<LlavesVapid>();

        var plataforma = rutas.MapGroup("/api/v1/plataforma").WithTags("Plataforma").RequireAuthorization();

        plataforma.MapGet("/banderas", ObtenerBanderasAsync)
            .WithName("ObtenerBanderasDeModulo")
            .WithSummary("Banderas de módulo")
            .WithDescription(
                "Módulos activos para la organización de la persona, como «modulo.<id>» → activo (ATLAS-ADR-0016). Un " +
                "módulo que no aparece usa el valor por defecto de su manifiesto en la PWA.");

        var notificaciones = plataforma.MapGroup("/notificaciones");
        notificaciones.MapGet("", ListarNotificacionesAsync)
            .WithName("ListarNotificaciones")
            .WithSummary("Bandeja de notificaciones")
            .WithDescription($"Notificaciones de la persona, de la más reciente a la más vieja (máximo {LimiteBandeja}), y cuántas no ha leído.");
        notificaciones.MapPost("/{id:guid}/leida", MarcarLeidaAsync)
            .WithName("MarcarNotificacionLeida")
            .WithSummary("Marcar una notificación como leída")
            .WithDescription("Marca como leída una notificación de la persona. 404 si no existe o es de otra persona.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
        notificaciones.MapPost("/leidas", MarcarTodasLeidasAsync)
            .WithName("MarcarNotificacionesLeidas")
            .WithSummary("Marcar todas como leídas")
            .WithDescription("Marca como leídas todas las notificaciones pendientes de la persona.")
            .Produces(StatusCodes.Status204NoContent);
        notificaciones.MapGet("/preferencias", ListarPreferenciasAsync)
            .WithName("ListarPreferenciasDeNotificacion")
            .WithSummary("Preferencias de notificación")
            .WithDescription(
                "Cada tipo de notificación (del núcleo y de los módulos) con el estado de sus canales (app, push, correo y Teams). " +
                "Un canal no disponible es uno que el tipo no usa o que el ambiente no tiene configurado.");
        notificaciones.MapPut("/preferencias", GuardarPreferenciasAsync)
            .WithName("GuardarPreferenciasDeNotificacion")
            .WithSummary("Guardar preferencias de notificación")
            .WithDescription("Activa o apaga canales por tipo. El canal app no se puede apagar; un canal no disponible para el tipo o el ambiente responde 400.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        var push = plataforma.MapGroup("/push");
        push.MapGet("/clave", (LlavesVapid llaves) => new ClavePushDto(llaves.Habilitado, llaves.ClavePublica))
            .WithName("ObtenerClavePush")
            .WithSummary("Llave pública VAPID")
            .WithDescription("Llave con la que el navegador suscribe el dispositivo a Web Push.");
        push.MapPost("/suscripciones", RegistrarSuscripcionAsync)
            .WithName("RegistrarSuscripcionPush")
            .WithSummary("Registrar el dispositivo para push")
            .WithDescription(
                "Crea o actualiza la suscripción del dispositivo y la deja a nombre de la persona en sesión. Solo se aceptan " +
                "servicios de push conocidos (FCM, Mozilla, Apple y Windows).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
        push.MapPost("/suscripciones/baja", DarDeBajaAsync)
            .WithName("DarDeBajaSuscripcionPush")
            .WithSummary("Quitar el dispositivo de push")
            .WithDescription("Borra la suscripción del dispositivo; la PWA la llama al desactivar push y al cerrar sesión.")
            .Produces(StatusCodes.Status204NoContent);
        push.MapPost("/prueba", EnviarPruebaAsync)
            .WithName("EnviarPushDePrueba")
            .WithSummary("Enviar una notificación de prueba")
            .WithDescription("Crea una notificación de prueba en la bandeja y la envía por push a los dispositivos de la persona.");

        return rutas;
    }

    private static Guid Persona(ClaimsPrincipal usuario) => Guid.Parse(usuario.FindFirstValue(ClaimPersona)!);

    private static async Task<Dictionary<string, bool>> ObtenerBanderasAsync(
        ClaimsPrincipal usuario, PlataformaDbContext contexto, CancellationToken ct)
    {
        var organizacion = Guid.TryParse(usuario.FindFirstValue(ClaimOrganizacion), out var id) ? id : Guid.Empty;
        var modulos = await contexto.Modulos.AsNoTracking().ToListAsync(ct);
        return modulos.ToDictionary(
            m => $"modulo.{m.Id}",
            m => m.Activo && (m.OrganizacionesHabilitadas.Count == 0 || m.OrganizacionesHabilitadas.Contains(organizacion)));
    }

    private static async Task<BandejaDto> ListarNotificacionesAsync(
        ClaimsPrincipal usuario, PlataformaDbContext contexto, CancellationToken ct,
        [Description("Cuántas notificaciones devolver (1 a 100, por defecto 30).")] int? limite = null)
    {
        var persona = Persona(usuario);
        var tope = Math.Clamp(limite ?? 30, 1, LimiteBandeja);
        var elementos = await contexto.Notificaciones.AsNoTracking()
            .Where(n => n.PersonaId == persona)
            .OrderByDescending(n => n.CreadoEn)
            .Take(tope)
            .Select(n => new NotificacionDto(n.Id, n.Tipo, n.Titulo, n.Cuerpo, n.Enlace, n.CreadoEn, n.LeidaEn))
            .ToListAsync(ct);
        var noLeidas = await contexto.Notificaciones.CountAsync(n => n.PersonaId == persona && n.LeidaEn == null, ct);
        return new BandejaDto(noLeidas, elementos);
    }

    private static async Task<IResult> MarcarLeidaAsync(
        Guid id, ClaimsPrincipal usuario, PlataformaDbContext contexto, TimeProvider reloj, CancellationToken ct)
    {
        var persona = Persona(usuario);
        var notificacion = await contexto.Notificaciones.FirstOrDefaultAsync(n => n.Id == id && n.PersonaId == persona, ct);
        if (notificacion is null)
        {
            return Results.NotFound();
        }
        notificacion.LeidaEn ??= reloj.GetUtcNow();
        await contexto.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MarcarTodasLeidasAsync(
        ClaimsPrincipal usuario, PlataformaDbContext contexto, TimeProvider reloj, CancellationToken ct)
    {
        var persona = Persona(usuario);
        var ahora = reloj.GetUtcNow();
        await contexto.Notificaciones
            .Where(n => n.PersonaId == persona && n.LeidaEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LeidaEn, ahora).SetProperty(n => n.ActualizadoEn, ahora), ct);
        return Results.NoContent();
    }

    private static string NombreCanal(CanalNotificacion canal) => canal.ToString().ToLowerInvariant();

    private static async Task<List<PreferenciaTipoDto>> ListarPreferenciasAsync(
        ClaimsPrincipal usuario, PlataformaDbContext contexto, CatalogoNotificaciones catalogo, IEnviadorCorreo correo, INotificadorTeams teams,
        CancellationToken ct)
    {
        var persona = Persona(usuario);
        var guardadas = await contexto.PreferenciasNotificacion.AsNoTracking().Where(p => p.PersonaId == persona).ToListAsync(ct);
        return
        [
            .. catalogo.Todos.Select(tipo => new PreferenciaTipoDto(tipo.Codigo, tipo.Nombre, tipo.Descripcion,
            [
                .. Enum.GetValues<CanalNotificacion>().Select(canal =>
                {
                    var disponible = CatalogoNotificaciones.Disponible(tipo, canal, correo, teams);
                    var guardada = guardadas.FirstOrDefault(p => p.Tipo == tipo.Codigo && p.Canal == canal);
                    var activa = canal == CanalNotificacion.App || (disponible && (guardada?.Activa ?? tipo.PorDefecto.Contains(canal)));
                    return new PreferenciaCanalDto(NombreCanal(canal), activa, disponible, disponible && canal != CanalNotificacion.App);
                }),
            ])),
        ];
    }

    private static async Task<IResult> GuardarPreferenciasAsync(
        [Description("Cambios de preferencia a aplicar.")] List<CambioPreferencia> cambios, ClaimsPrincipal usuario, PlataformaDbContext contexto,
        CatalogoNotificaciones catalogo, IEnviadorCorreo correo, INotificadorTeams teams, TimeProvider reloj, CancellationToken ct)
    {
        var persona = Persona(usuario);
        var errores = new Dictionary<string, string[]>();
        var validos = new List<(TipoNotificacion Tipo, CanalNotificacion Canal, bool Activa)>();
        foreach (var (cambio, i) in cambios.Select((c, i) => (c, i)))
        {
            var tipo = catalogo.Buscar(cambio.Tipo);
            var canal = Enum.GetValues<CanalNotificacion>().Cast<CanalNotificacion?>().FirstOrDefault(c => NombreCanal(c!.Value) == cambio.Canal);
            if (tipo is null)
            {
                errores[$"[{i}].tipo"] = [$"Tipo de notificación desconocido: «{cambio.Tipo}»."];
            }
            else if (canal is null || canal == CanalNotificacion.App || !CatalogoNotificaciones.Disponible(tipo, canal.Value, correo, teams))
            {
                errores[$"[{i}].canal"] = [$"El canal «{cambio.Canal}» no se puede cambiar."];
            }
            else
            {
                validos.Add((tipo, canal.Value, cambio.Activa));
            }
        }
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }

        var ahora = reloj.GetUtcNow();
        foreach (var (tipo, canal, activa) in validos)
        {
            var existente = await contexto.PreferenciasNotificacion
                .FirstOrDefaultAsync(p => p.PersonaId == persona && p.Tipo == tipo.Codigo && p.Canal == canal, ct);
            if (existente is null)
            {
                contexto.PreferenciasNotificacion.Add(new PreferenciaNotificacion
                {
                    PersonaId = persona,
                    Tipo = tipo.Codigo,
                    Canal = canal,
                    Activa = activa,
                    ActualizadoEn = ahora,
                });
            }
            else
            {
                existente.Activa = activa;
                existente.ActualizadoEn = ahora;
            }
        }
        await contexto.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RegistrarSuscripcionAsync(
        [Description("Suscripción que entrega el navegador (PushSubscription.toJSON) y el resumen del dispositivo.")] SuscripcionDto suscripcion, ClaimsPrincipal usuario, PlataformaDbContext contexto, OpcionesPush opciones,
        TimeProvider reloj, CancellationToken ct)
    {
        var errores = ValidarSuscripcion(suscripcion, opciones);
        if (errores.Count > 0)
        {
            return Results.ValidationProblem(errores);
        }
        var persona = Persona(usuario);
        var dispositivo = suscripcion.Dispositivo?.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 200)] : null;
        var existente = await contexto.SuscripcionesPush.FirstOrDefaultAsync(s => s.Endpoint == suscripcion.Endpoint, ct);
        if (existente is null)
        {
            contexto.SuscripcionesPush.Add(new SuscripcionPush
            {
                PersonaId = persona,
                Endpoint = suscripcion.Endpoint,
                P256dh = suscripcion.P256dh,
                Auth = suscripcion.Auth,
                Dispositivo = dispositivo,
                UltimoUsoEn = reloj.GetUtcNow(),
            });
        }
        else
        {
            // El mismo navegador con otra persona: la suscripción pasa a quien tiene la sesión ahora.
            existente.PersonaId = persona;
            existente.P256dh = suscripcion.P256dh;
            existente.Auth = suscripcion.Auth;
            existente.Dispositivo = dispositivo;
            existente.FallosConsecutivos = 0;
        }
        try
        {
            await contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos pestañas registrando el mismo endpoint a la vez: la otra ya lo guardó.
            return Results.NoContent();
        }
        return Results.NoContent();
    }

    internal static Dictionary<string, string[]> ValidarSuscripcion(SuscripcionDto suscripcion, OpcionesPush opciones)
    {
        var errores = new Dictionary<string, string[]>();
        if (!Uri.TryCreate(suscripcion.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
            || suscripcion.Endpoint.Length > 2000 || !opciones.HostPermitido(endpoint.Host))
        {
            // Atlas hace POST a esta URL: solo servicios de push conocidos, nunca una dirección interna (SSRF).
            errores["endpoint"] = ["El endpoint debe ser una URL https de un servicio de push conocido."];
        }
        if (!LargoBase64Url(suscripcion.P256dh, 65))
        {
            errores["p256dh"] = ["La llave p256dh debe ser base64url de 65 bytes."];
        }
        if (!LargoBase64Url(suscripcion.Auth, 16))
        {
            errores["auth"] = ["El secreto auth debe ser base64url de 16 bytes."];
        }
        return errores;
    }

    private static bool LargoBase64Url(string? valor, int bytes)
    {
        if (string.IsNullOrEmpty(valor) || valor.Length > 200)
        {
            return false;
        }
        try
        {
            return Base64Url.DecodeFromChars(valor.TrimEnd('=')).Length == bytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<IResult> DarDeBajaAsync(
        [Description("Endpoint de la suscripción del dispositivo.")] BajaSuscripcionDto baja, ClaimsPrincipal usuario, PlataformaDbContext contexto, CancellationToken ct)
    {
        var persona = Persona(usuario);
        await contexto.SuscripcionesPush
            .Where(s => s.Endpoint == baja.Endpoint && s.PersonaId == persona)
            .ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static async Task<PruebaPushDto> EnviarPruebaAsync(
        ClaimsPrincipal usuario, ServicioNotificaciones servicio, TimeProvider reloj, CancellationToken ct)
    {
        var hora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), ZonaBogota).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var (_, dispositivos) = await servicio.NotificarAsync(
            Persona(usuario), TiposNotificacion.Prueba, "Notificación de prueba",
            $"Si ves esto, las notificaciones de Atlas llegan a tu dispositivo ({hora}).", "/notificaciones", ct);
        return new PruebaPushDto(dispositivos);
    }

    private static readonly TimeZoneInfo ZonaBogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
}
