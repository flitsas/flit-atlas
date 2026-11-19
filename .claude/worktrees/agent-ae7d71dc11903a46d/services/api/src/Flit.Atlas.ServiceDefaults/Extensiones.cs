using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Flit.Atlas.ServiceDefaults;

/// <summary>
/// Configuración común de los servicios .NET de Atlas: telemetría, salud, descubrimiento de servicios y resiliencia.
/// </summary>
public static class Extensiones
{
    /// <summary>Etiqueta de los chequeos que solo miran el propio proceso (sonda de vida).</summary>
    public const string EtiquetaVida = "vida";

    public const string RutaVida = "/health/live";
    public const string RutaListo = "/health/ready";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigurarOpenTelemetry();

        builder.Services.AddHealthChecks()
            .AddCheck("proceso", () => HealthCheckResult.Healthy(), [EtiquetaVida]);

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    private static TBuilder ConfigurarOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                // Las sondas de salud no generan trazas: son ruido cada pocos segundos.
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Publica <c>/health/live</c> (solo el proceso) y <c>/health/ready</c> (todas las dependencias).
    /// Responden solo el estado, sin detalles de las dependencias.
    /// </summary>
    public static WebApplication MapEndpointsDeSalud(this WebApplication app)
    {
        app.MapHealthChecks(RutaVida, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(EtiquetaVida),
        });
        app.MapHealthChecks(RutaListo);
        return app;
    }
}
