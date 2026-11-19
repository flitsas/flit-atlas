using Flit.Atlas.Api;
using Flit.Atlas.Composicion;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Emergencia;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
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
// Módulos funcionales: la lista única de Flit.Atlas.Composicion, con sus permisos y scopes (ATLAS-ADR-0026).
builder.Services.AgregarModulosAtlas(builder.Configuration, cadenaAtlas);
builder.Services.AgregarContratoOpenApi();

// Detrás del proxy de Vite (local) o del nginx del host y el de atlas-web (qa y pdn) el esquema real llega en
// X-Forwarded-Proto: la redirect_uri de Entra, el chequeo de Origin y las cookies Secure dependen de él. Por defecto
// solo se confía en proxies de loopback; en el clúster, la red de pods llega por Atlas__Proxy__RedesConfiables
// (desde el ConfigMap de flit-gitops, nunca desde este repo).
var redesConfiables = Dependencias.RedesConfiables(builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = builder.Configuration.GetValue(Dependencias.ProxyLimite, 2);
    foreach (var red in redesConfiables)
    {
        o.KnownIPNetworks.Add(red);
    }
});

// Comando de la cuenta de administrador de emergencia (ATLAS-ADR-0027): `dotnet Flit.Atlas.Api.dll emergencia activar`
// dentro del contenedor. Usa la misma configuración y servicios, pero no levanta el servidor ni los trabajos en segundo
// plano. Sin proveedores de log: lo único que sale es lo que el comando escribe (el enlace va solo a su stdout).
var comandoEmergencia = ComandoEmergencia.EsComando(args);
if (comandoEmergencia)
{
    builder.Logging.ClearProviders();
}

var app = builder.Build();

if (comandoEmergencia)
{
    Environment.ExitCode = await ComandoEmergencia.EjecutarAsync(app.Services, args, Console.Out, Console.Error);
    return;
}

app.UseForwardedHeaders();
// Cabeceras de seguridad de cada respuesta (HU 4.10, ATLAS-ADR-0048): CSP, nosniff, Referrer-Policy, Permissions-Policy,
// COOP, CORP y, fuera de local, HSTS.
app.UseCabecerasSeguridad(ambiente);

app.MapEndpointsDeSalud();
app.UseAntifalsificacionApi();
app.UseSeguridadIdentidad();
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentidad();
app.MapPlataforma();
app.MapModulos();
app.MapContratoOpenApi(ambiente);

app.MapGet("/api/v1/version", () => new VersionApi("atlas-api", typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithName("ObtenerVersion")
    .WithTags("Plataforma")
    .WithSummary("Versión de atlas-api")
    .WithDescription("Nombre del servicio y versión del ensamblado desplegado. No requiere sesión.");

await app.RunAsync();

/// <summary>Punto de entrada; público para las pruebas con <c>WebApplicationFactory</c>.</summary>
public partial class Program;
