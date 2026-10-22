using Flit.Atlas.Api;
using Flit.Atlas.Identidad;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.ServiceDefaults;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

ConfiguracionRequerida.Validar(builder.Configuration, Dependencias.ClavesRequeridas);
var ambiente = builder.Configuration[Dependencias.Ambiente]!.Trim().ToLowerInvariant();
if (!Dependencias.AmbientesValidos.Contains(ambiente))
{
    throw new InvalidOperationException(
        $"Atlas__Ambiente inválido: «{ambiente}». Valores permitidos: {string.Join(", ", Dependencias.AmbientesValidos)}.");
}
var cadenaAtlas = builder.Configuration.GetConnectionString(Dependencias.BaseDeDatos)!;

builder.AddServiceDefaults();
builder.Services.AgregarPlataforma(cadenaAtlas);
// Telemetría y chequeo de salud de Aspire sobre el DbContext. Sin reintentos automáticos: chocan con las
// transacciones explícitas de la outbox (ATLAS-ADR-0018).
builder.EnrichNpgsqlDbContext<PlataformaDbContext>(s => s.DisableRetry = true);
builder.AddRedisClient(Dependencias.Redis);
builder.AddRabbitMQClient(Dependencias.RabbitMq);
builder.AgregarIdentidad(ambiente, cadenaAtlas);

// Detrás del proxy de Vite (local) o del Ingress (dev/qa/pdn) el esquema real llega en X-Forwarded-Proto: la
// redirect_uri de Entra y las cookies Secure dependen de él. Por defecto solo se confía en proxies de loopback; la
// red del Ingress se agrega en la HU 1.8.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

app.UseForwardedHeaders();

app.MapEndpointsDeSalud();
app.UseAntifalsificacionApi();
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentidad();

app.MapGet("/api/v1/version", () => new
{
    servicio = "atlas-api",
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
});

await app.RunAsync();

/// <summary>Punto de entrada; público para las pruebas con <c>WebApplicationFactory</c>.</summary>
public partial class Program;
