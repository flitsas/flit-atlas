using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>Límites de las rutas de identidad por ventana fija (sección <c>Identidad:LimiteTasa</c>).</summary>
public sealed class OpcionesLimiteTasa
{
    public const string Seccion = "Identidad:LimiteTasa";

    public TimeSpan Ventana { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Peticiones de ingreso (botones de entrar y retorno de Entra) por IP y ventana.</summary>
    public int IngresoPorIp { get; set; } = 20;

    /// <summary>Peticiones a cualquier ruta de identidad por IP y ventana (una oficina comparte IP: es holgado).</summary>
    public int PorIp { get; set; } = 120;

    /// <summary>Peticiones a cualquier ruta de identidad por usuario autenticado y ventana.</summary>
    public int PorUsuario { get; set; } = 60;

    /// <summary>Peticiones a <c>/connect/token</c> y <c>/connect/authorize</c> por IP y ventana (HU 1.3c).</summary>
    public int TokensPorIp { get; set; } = 30;

    /// <summary>
    /// Peticiones por IP y ventana a las rutas públicas de las cuentas externas (aceptar invitación, ingreso local,
    /// recuperación): más estricto que el ingreso de empleados (§12, HU 1.5). Se suma a los demás límites.
    /// </summary>
    public int ExternosPorIp { get; set; } = 10;

    /// <summary>
    /// Intentos de ingreso local (contraseña y segundo factor) por cuenta atacada y <see cref="VentanaPorCuenta"/>, sumando
    /// todas las IP y réplicas (contador en Redis, ATLAS-ADR-0049): un ataque repartido entre muchas IP también se frena.
    /// Cuenta por correo, exista o no la cuenta, para que el 429 no revele nada.
    /// </summary>
    public int IngresoPorCuenta { get; set; } = 20;

    public TimeSpan VentanaPorCuenta { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Reportes de errores de la PWA por persona y ventana (<c>POST /api/v1/plataforma/errores-cliente</c>, con sesión):
    /// una pantalla de error envía un solo reporte, así que un tope bajo evita que una sesión llene el log.
    /// </summary>
    public int ErroresClientePorPersona { get; set; } = 10;

    /// <summary>
    /// Reportes de errores de la PWA por IP y ventana, además del tope por persona (también cuenta los intentos sin
    /// sesión, que responden 401). Una oficina comparte IP: es más holgado.
    /// </summary>
    public int ErroresClientePorIp { get; set; } = 60;

    // --- Resto de la API (HU 4.10, ATLAS-ADR-0048): se suman a los de identidad ---

    /// <summary>
    /// Peticiones a <c>/api/v1/**</c> y <c>/hubs/**</c> por empleado (persona con la sesión de la PWA) y ventana. La PWA
    /// pide entre 5 y 10 recursos por pantalla: 600 por minuto deja navegar rápido y corta un script que recorra la API.
    /// </summary>
    public int PorEmpleado { get; set; } = 600;

    /// <summary>Lo mismo por persona externa: más estricto (prompt maestro §12).</summary>
    public int PorExterno { get; set; } = 240;

    /// <summary>
    /// Peticiones a <c>/api/v1/portal/**</c> de todas las personas externas de una misma organización y ventana: una
    /// organización con muchas cuentas no suma más que esto.
    /// </summary>
    public int PortalPorOrganizacion { get; set; } = 600;

    /// <summary>Solicitudes de exportación (<c>POST …/exportaciones</c>, interna o del portal) por persona y ventana.</summary>
    public int ExportacionesPorPersona { get; set; } = 5;

    /// <summary>
    /// Solicitudes de exportación del portal por organización externa y ventana. Además, cada organización tiene un tope
    /// de exportaciones en curso (<c>Contratos:Exportaciones:MaximoEnCursoPorOrganizacion</c>).
    /// </summary>
    public int ExportacionesPorOrganizacion { get; set; } = 10;

    /// <summary>
    /// Peticiones a <c>/api/v1/**</c> y <c>/hubs/**</c> sin la sesión de la PWA (rutas públicas, tokens Bearer de
    /// OpenIddict y de GitHub Actions) por IP y ventana, fuera de las rutas de identidad, que tienen las suyas.
    /// </summary>
    public int AnonimoPorIp { get; set; } = 300;

    /// <summary>
    /// Peticiones al proxy del sandbox (<c>/api/v1/contratos/sandbox/**</c>, HU 4.7) por llave de sandbox (una por
    /// compartición, en <c>Authorization: Bearer</c>) y ventana: más estricto que el portal, porque no lleva sesión.
    /// </summary>
    public int SandboxPorLlave { get; set; } = 60;

    /// <summary>Peticiones al proxy del sandbox por IP y ventana, con o sin llave (corta a quien prueba llaves).</summary>
    public int SandboxPorIp { get; set; } = 120;

    /// <summary>Informes de violaciones de la CSP por IP y ventana (<c>POST /api/v1/plataforma/csp-reportes</c>, sin sesión).</summary>
    public int ReportesCspPorIp { get; set; } = 30;

    /// <summary>Informes de violaciones de la CSP de todas las IP juntas, por réplica y ventana: el log no crece sin tope.</summary>
    public int ReportesCspTotal { get; set; } = 300;
}

/// <summary>Metadato de los endpoints con límite de tasa (lo lee el contrato OpenAPI para declarar el 429).</summary>
public sealed class MetadatoLimiteTasa;

/// <summary>
/// Limitación de tasa de Atlas con <c>Microsoft.AspNetCore.RateLimiting</c>. Rutas de identidad (ATLAS-ADR-0022): por IP
/// (la del cliente, resuelta por <c>UseForwardedHeaders</c> solo desde los proxies confiables de
/// <c>Atlas:Proxy:RedesConfiables</c>) y por usuario. Resto de la API (HU 4.10, ATLAS-ADR-0048): por persona (más estricto
/// para externos), por organización externa en el portal y al exportar, y por IP sin sesión. Responde 429 con
/// <c>Retry-After</c> y problem+json, y registra <c>identidad.limite_tasa</c>, una vez por cliente y ventana, para que un
/// ataque no inunde la auditoría. Los contadores viven en memoria de cada réplica.
/// </summary>
public static partial class LimitacionTasa
{
    public const string Codigo = "limite_de_tasa";

    internal static void Agregar(IServiceCollection servicios)
    {
        servicios.AddOptions<OpcionesLimiteTasa>().BindConfiguration(OpcionesLimiteTasa.Seccion);
        servicios.AddSingleton<AvisosLimiteTasa>();
        servicios.AddRateLimiter(_ => { });
        servicios.AddOptions<RateLimiterOptions>().Configure<IOptions<OpcionesLimiteTasa>>((o, limites) =>
        {
            var l = limites.Value;
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Particion(l.IngresoPorIp, l.Ventana, http => EsIngreso(http.Request.Path) ? "ingreso:" + Ip(http) : null),
                Particion(l.TokensPorIp, l.Ventana, http => EsEmisionTokens(http.Request.Path) ? "tokens:" + Ip(http) : null),
                Particion(l.ExternosPorIp, l.Ventana, http => Externos.EndpointsCuentasExternas.EsRutaDeIngreso(http.Request.Path) ? "externos:" + Ip(http) : null),
                Particion(l.ErroresClientePorIp, l.Ventana, http => EsErroresCliente(http.Request.Path) ? "errores:" + Ip(http) : null),
                Particion(l.ErroresClientePorPersona, l.Ventana, http => EsErroresCliente(http.Request.Path) && Persona(http) is { } p ? "errores-persona:" + p : null),
                Particion(l.PorIp, l.Ventana, http => EsIdentidad(http.Request.Path) ? "ip:" + Ip(http) : null),
                Particion(l.PorUsuario, l.Ventana, http => EsIdentidad(http.Request.Path) && Usuario(http) is { } sub ? "usuario:" + sub : null),
                Particion(l.PorEmpleado, l.Ventana, http => EsApi(http.Request.Path) && !EsExterno(http) && Persona(http) is { } p ? "empleado:" + p : null),
                Particion(l.PorExterno, l.Ventana, http => EsApi(http.Request.Path) && EsExterno(http) && Persona(http) is { } p ? "externo:" + p : null),
                Particion(l.PortalPorOrganizacion, l.Ventana, http =>
                    EsPortal(http.Request.Path) && EsExterno(http) && Organizacion(http) is { } org ? "portal:" + org : null),
                Particion(l.ExportacionesPorPersona, l.Ventana, http => EsSolicitudExportacion(http) && Persona(http) is { } p ? "exportar:" + p : null),
                Particion(l.ExportacionesPorOrganizacion, l.Ventana, http =>
                    EsSolicitudExportacion(http) && EsExterno(http) && Organizacion(http) is { } org ? "exportar-org:" + org : null),
                Particion(l.AnonimoPorIp, l.Ventana, http =>
                    EsApi(http.Request.Path) && !EsIdentidad(http.Request.Path) && Persona(http) is null ? "anonimo:" + Ip(http) : null),
                Particion(l.SandboxPorLlave, l.Ventana, http => EsSandbox(http.Request.Path) && LlaveSandbox(http) is { } llave ? "sandbox:" + llave : null),
                Particion(l.SandboxPorIp, l.Ventana, http => EsSandbox(http.Request.Path) ? "sandbox-ip:" + Ip(http) : null),
                Particion(l.ReportesCspPorIp, l.Ventana, http => EsReporteCsp(http.Request.Path) ? "csp:" + Ip(http) : null),
                Particion(l.ReportesCspTotal, l.Ventana, http => EsReporteCsp(http.Request.Path) ? "csp" : null));
            o.OnRejected = AlRechazarAsync;
        });
    }

    /// <summary>
    /// Va <b>antes</b> de <c>UseAuthentication</c> para cubrir también el retorno de Entra (lo atiende el middleware de
    /// autenticación). Para limitar por usuario, por persona o por organización, lee aquí la sesión de la PWA en las rutas
    /// de identidad y del resto de la API (salvo las que inician un ingreso, que apartan la cookie anterior, y los informes
    /// de la CSP, que no la usan); el manejador de la cookie guarda el resultado y <c>UseAuthentication</c> no vuelve a
    /// consultar la base.
    /// </summary>
    internal static IApplicationBuilder Usar(IApplicationBuilder app)
    {
        app.Use(async (http, siguiente) =>
        {
            var ruta = http.Request.Path;
            if ((EsIdentidad(ruta) || EsApi(ruta)) && !EsIngreso(ruta) && !EsReporteCsp(ruta))
            {
                var resultado = await http.AuthenticateAsync(ClaimsAtlas.EsquemaCookie);
                if (resultado.Succeeded)
                {
                    http.User = resultado.Principal;
                }
            }
            await siguiente(http);
        });
        return app.UseRateLimiter();
    }

    /// <summary>
    /// Rutas de identidad: el módulo, el retorno de OIDC, <c>/api/v1/plataforma/yo</c> y el servidor de tokens
    /// (<c>/connect/*</c> y <c>/.well-known/*</c>).
    /// </summary>
    internal static bool EsIdentidad(PathString ruta) =>
        ruta.StartsWithSegments("/api/v1/identidad", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/api/v1/plataforma/yo", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/connect", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/.well-known", StringComparison.OrdinalIgnoreCase);

    /// <summary>El resto de la API de la PWA y de los clientes con token: <c>/api/v1/**</c> y SignalR (<c>/hubs/**</c>).</summary>
    internal static bool EsApi(PathString ruta) =>
        ruta.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase);

    /// <summary>El portal de clientes (ATLAS-ADR-0043), que los módulos montan bajo un prefijo reservado.</summary>
    internal static bool EsPortal(PathString ruta) =>
        ruta.StartsWithSegments(ExtensionesModulos.RutaPortal, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Un endpoint que encola una exportación: el módulo lo marca con <c>ConLimiteDeExportacion</c>. El limitador corre
    /// después del enrutamiento (que <c>WebApplication</c> pone al principio), así que el endpoint ya está elegido.
    /// </summary>
    internal static bool EsSolicitudExportacion(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<MetadatoSolicitudExportacion>() is not null;

    /// <summary>Proxy del sandbox de contratos (HU 4.7): sin sesión, con su llave de sandbox como Bearer.</summary>
    internal static bool EsSandbox(PathString ruta) =>
        ruta.StartsWithSegments("/api/v1/contratos/sandbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// La partición de la llave de sandbox: su SHA-256 (los contadores en memoria no guardan la llave). Sin llave, solo
    /// cuenta el límite por IP.
    /// </summary>
    private static string? LlaveSandbox(HttpContext http)
    {
        var autorizacion = http.Request.Headers.Authorization.ToString();
        if (!autorizacion.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || autorizacion.Length <= 7)
        {
            return null;
        }
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(autorizacion[7..].Trim())));
    }

    /// <summary>Informes de violaciones de la CSP: sin sesión, con límites propios por IP y en total.</summary>
    internal static bool EsReporteCsp(PathString ruta) =>
        ruta.StartsWithSegments(EndpointsReportesCsp.Ruta, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reporte de errores de la PWA: con sesión y límites propios por persona y por IP.</summary>
    internal static bool EsErroresCliente(PathString ruta) =>
        ruta.StartsWithSegments(EndpointsErroresCliente.Ruta, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rutas que emiten códigos o tokens (HU 1.3c): límite propio por IP.</summary>
    internal static bool EsEmisionTokens(PathString ruta) =>
        ruta.StartsWithSegments("/connect/token", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/connect/authorize", StringComparison.OrdinalIgnoreCase);

    /// <summary>Rutas que inician un ingreso: más estrictas.</summary>
    internal static bool EsIngreso(PathString ruta) =>
        ruta.StartsWithSegments("/api/v1/identidad/entrar", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/api/v1/identidad/simulado", StringComparison.OrdinalIgnoreCase)
        || ruta.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
        || Externos.EndpointsCuentasExternas.EsRutaDeIngreso(ruta);

    private static PartitionedRateLimiter<HttpContext> Particion(int permisos, TimeSpan ventana, Func<HttpContext, string?> clave) =>
        PartitionedRateLimiter.Create<HttpContext, string>(http => clave(http) is { } k
            ? RateLimitPartition.GetFixedWindowLimiter(k, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permisos,
                Window = ventana,
                QueueLimit = 0,
                AutoReplenishment = true,
            })
            : RateLimitPartition.GetNoLimiter(string.Empty));

    private static string Ip(HttpContext http) => RegistroSeguridad.IpCliente(http)?.ToString() ?? "desconocida";

    private static string? Usuario(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true ? http.User.FindFirstValue(ClaimsAtlas.Sub) : null;

    private static string? Persona(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true ? http.User.FindFirstValue(ClaimsAtlas.PersonaId) : null;

    private static bool EsExterno(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true && http.User.FindFirstValue(ClaimsAtlas.TipoPrincipal) == ClaimsAtlas.TipoExterno;

    private static string? Organizacion(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true ? http.User.FindFirstValue(ClaimsAtlas.OrganizacionId) : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo registrar el evento de límite de tasa")]
    private static partial void NoSeRegistro(ILogger log, Exception error);

    private static async ValueTask AlRechazarAsync(OnRejectedContext contexto, CancellationToken ct)
    {
        var http = contexto.HttpContext;
        var limites = http.RequestServices.GetRequiredService<IOptions<OpcionesLimiteTasa>>().Value;
        var espera = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : limites.Ventana;
        var segundos = Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds));
        http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);

        var cliente = Ip(http) + "|" + Usuario(http);
        var ahora = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        if (http.RequestServices.GetRequiredService<AvisosLimiteTasa>().DebeRegistrar(cliente, ahora, limites.Ventana))
        {
            try
            {
                await http.RequestServices.GetRequiredService<RegistroSeguridad>().RegistrarAsync(
                    TiposEventoSeguridad.LimiteTasa, DatosEventoSeguridad.DeSesion(http.User) with
                    {
                        Detalle = new { metodo = http.Request.Method, ruta = http.Request.Path.Value, reintentar_en_segundos = segundos },
                    }, ct);
            }
#pragma warning disable CA1031 // Un fallo al auditar no debe convertir el 429 en un 500.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                NoSeRegistro(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LimitacionTasa)), ex);
            }
        }

        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Demasiadas peticiones. Intenta de nuevo en un momento.",
            extensions: new Dictionary<string, object?> { ["codigo"] = Codigo, ["reintentarEnSegundos"] = segundos })
            .ExecuteAsync(http);
    }
}

/// <summary>Recuerda a qué clientes ya se les registró el evento de límite de tasa en la ventana actual.</summary>
internal sealed class AvisosLimiteTasa
{
    private const int Maximo = 10_000;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _ultimos = new(StringComparer.Ordinal);

    public bool DebeRegistrar(string cliente, DateTimeOffset ahora, TimeSpan ventana)
    {
        if (_ultimos.Count > Maximo)
        {
            foreach (var (clave, momento) in _ultimos)
            {
                if (ahora - momento >= ventana)
                {
                    _ultimos.TryRemove(clave, out _);
                }
            }
        }
        var registrar = false;
        _ultimos.AddOrUpdate(cliente,
            _ => { registrar = true; return ahora; },
            (_, previo) =>
            {
                if (ahora - previo < ventana)
                {
                    return previo;
                }
                registrar = true;
                return ahora;
            });
        return registrar;
    }
}
