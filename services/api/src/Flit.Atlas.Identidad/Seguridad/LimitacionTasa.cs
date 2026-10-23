using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Flit.Atlas.Identidad.Sesiones;
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
    /// Reportes de errores de la PWA por persona y ventana (<c>POST /api/v1/plataforma/errores-cliente</c>, con sesión):
    /// una pantalla de error envía un solo reporte, así que un tope bajo evita que una sesión llene el log.
    /// </summary>
    public int ErroresClientePorPersona { get; set; } = 10;

    /// <summary>
    /// Reportes de errores de la PWA por IP y ventana, además del tope por persona (también cuenta los intentos sin
    /// sesión, que responden 401). Una oficina comparte IP: es más holgado.
    /// </summary>
    public int ErroresClientePorIp { get; set; } = 60;
}

/// <summary>
/// Limitación de tasa de las rutas de identidad (ATLAS-ADR-0022) con <c>Microsoft.AspNetCore.RateLimiting</c>: por IP
/// (la del cliente, resuelta por <c>UseForwardedHeaders</c> solo desde los proxies confiables de
/// <c>Atlas:Proxy:RedesConfiables</c>) y por usuario. Responde 429 con <c>Retry-After</c> y registra
/// <c>identidad.limite_tasa</c>, una vez por cliente y ventana, para que un ataque no inunde la auditoría.
/// Los contadores viven en memoria de cada réplica.
/// </summary>
/// <summary>Metadato de los endpoints con límite de tasa (lo lee el contrato OpenAPI para declarar el 429).</summary>
public sealed class MetadatoLimiteTasa;

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
                Particion(l.PorUsuario, l.Ventana, http => EsIdentidad(http.Request.Path) && Usuario(http) is { } sub ? "usuario:" + sub : null));
            o.OnRejected = AlRechazarAsync;
        });
    }

    /// <summary>
    /// Va <b>antes</b> de <c>UseAuthentication</c> para cubrir también el retorno de Entra (lo atiende el middleware de
    /// autenticación). Para limitar por usuario o por persona, lee aquí la sesión de las rutas de identidad y del reporte
    /// de errores de la PWA; el manejador de la cookie guarda el resultado y <c>UseAuthentication</c> no vuelve a
    /// consultar la base.
    /// </summary>
    internal static IApplicationBuilder Usar(IApplicationBuilder app)
    {
        app.Use(async (http, siguiente) =>
        {
            if ((EsIdentidad(http.Request.Path) && !EsIngreso(http.Request.Path)) || EsErroresCliente(http.Request.Path))
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
