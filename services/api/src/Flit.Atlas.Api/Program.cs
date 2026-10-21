using Flit.Atlas.Api;
using Flit.Atlas.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

ConfiguracionRequerida.Validar(builder.Configuration, Dependencias.ClavesRequeridas);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource(Dependencias.BaseDeDatos);
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
