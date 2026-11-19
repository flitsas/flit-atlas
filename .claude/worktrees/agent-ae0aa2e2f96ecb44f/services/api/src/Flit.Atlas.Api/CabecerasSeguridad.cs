using Flit.Atlas.Identidad.Seguridad;

namespace Flit.Atlas.Api;

/// <summary>
/// Cabeceras de seguridad de cada respuesta de atlas-api (HU 4.10, prompt maestro §12, ATLAS-ADR-0008 y ATLAS-ADR-0048).
/// La API responde JSON (y unas pocas páginas propias sin scripts): su CSP no permite cargar nada, ni enmarcarla, ni
/// enviar formularios. Las de la PWA y del visor las pone el nginx de atlas-web (<c>apps/web/deploy</c>), que reenvía a la
/// API sin tocar las suyas. Una respuesta que ya trae una cabecera (p. ej. la página de resultado del servidor de tokens)
/// la conserva.
/// </summary>
internal static class CabecerasSeguridad
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; " +
        $"report-uri {EndpointsReportesCsp.Ruta}; report-to {EndpointsReportesCsp.GrupoReportes}";

    /// <summary>Destino de <c>report-to</c> (Reporting API); la ruta se resuelve contra el origen de la respuesta.</summary>
    public const string ReportingEndpoints = $"{EndpointsReportesCsp.GrupoReportes}=\"{EndpointsReportesCsp.Ruta}\"";

    /// <summary>
    /// Lo mínimo: nada de sensores, cámara, ubicación, pagos ni USB; el micrófono solo para el propio origen (dictado del
    /// asistente, F7).
    /// </summary>
    public const string PermissionsPolicy =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(self), payment=(), usb=()";

    /// <summary>Un año, con los subdominios del host de Atlas. Sin <c>preload</c>: es una decisión del dominio, no de Atlas.</summary>
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    private static readonly (string Nombre, string Valor)[] Fijas =
    [
        ("Content-Security-Policy", ContentSecurityPolicy),
        ("Reporting-Endpoints", ReportingEndpoints),
        ("X-Content-Type-Options", "nosniff"),
        ("Referrer-Policy", "no-referrer"),
        ("Permissions-Policy", PermissionsPolicy),
        ("Cross-Origin-Opener-Policy", "same-origin"),
        ("Cross-Origin-Resource-Policy", "same-origin"),
    ];

    /// <summary>
    /// Va justo después de <c>UseForwardedHeaders</c>, para que el esquema sea el que vio el nginx del host. HSTS solo fuera
    /// de desarrollo (no en <c>local</c>) y solo en respuestas HTTPS: en local la API corre en http detrás de Vite.
    /// </summary>
    public static IApplicationBuilder UseCabecerasSeguridad(this IApplicationBuilder app, string ambiente)
    {
        var conHsts = !string.Equals(ambiente, "local", StringComparison.Ordinal);
        return app.Use((http, siguiente) =>
        {
            http.Response.OnStarting(() =>
            {
                Aplicar(http, conHsts);
                return Task.CompletedTask;
            });
            return siguiente(http);
        });
    }

    internal static void Aplicar(HttpContext http, bool conHsts)
    {
        var cabeceras = http.Response.Headers;
        foreach (var (nombre, valor) in Fijas)
        {
            cabeceras.TryAdd(nombre, valor);
        }
        if (conHsts && http.Request.IsHttps)
        {
            cabeceras.TryAdd("Strict-Transport-Security", StrictTransportSecurity);
        }
    }
}
