using Flit.Atlas.Api;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

ConfiguracionRequerida.Validar(builder.Configuration, Dependencias.ClavesRequeridas);

builder.AddServiceDefaults();
builder.Services.AgregarPlataforma(builder.Configuration.GetConnectionString(Dependencias.BaseDeDatos)!);
// Telemetría y chequeo de salud de Aspire sobre el DbContext. Sin reintentos automáticos: chocan con las
// transacciones explícitas de la outbox (ATLAS-ADR-0018).
builder.EnrichNpgsqlDbContext<PlataformaDbContext>(s => s.DisableRetry = true);
builder.AddRedisClient(Dependencias.Redis);
builder.AddRabbitMQClient(Dependencias.RabbitMq);

var app = builder.Build();

app.MapEndpointsDeSalud();

app.MapGet("/api/v1/version", () => new
{
    servicio = "atlas-api",
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
});

await app.RunAsync();

/// <summary>Punto de entrada; público para las pruebas con <c>WebApplicationFactory</c>.</summary>
public partial class Program;
