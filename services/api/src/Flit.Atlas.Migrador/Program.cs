// Migrador de Flit Atlas: aplica las migraciones de EF Core del núcleo (y, más adelante, de cada módulo) y termina.
// En local lo corre el AppHost antes de atlas-api; en dev/qa/pdn corre como Job o initContainer (HU 1.8).
// Usa el usuario del servicio (flit_atlas), dueño del esquema atlas según el script de flit.

using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.ServiceDefaults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

const string ClaveConexion = "ConnectionStrings:atlas";
ConfiguracionRequerida.Validar(builder.Configuration, ClaveConexion);

builder.AddServiceDefaults();
var cadena = builder.Configuration[ClaveConexion]!;
builder.Services.AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, cadena));
// Identity arma su modelo según IdentityOptions (esquema v3, con passkeys).
builder.Services.Configure<IdentityOptions>(IdentidadDbContext.ConfigurarIdentity);
builder.Services.AddDbContext<IdentidadDbContext>(o => IdentidadDbContext.Configurar(o, cadena));

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Flit.Atlas.Migrador");

// Orden por dependencia (ATLAS-ADR-0016): primero el núcleo, después cada módulo.
await MigrarAsync<PlataformaDbContext>("plataforma");
await MigrarAsync<IdentidadDbContext>("identidad");

async Task MigrarAsync<TContexto>(string nombre) where TContexto : DbContext
{
    using var alcance = host.Services.CreateScope();
    var contexto = alcance.ServiceProvider.GetRequiredService<TContexto>();
    var pendientes = (await contexto.Database.GetPendingMigrationsAsync()).ToList();
    Registro.Pendientes(log, nombre, pendientes.Count);
    await contexto.Database.MigrateAsync();
    Registro.AlDia(log, nombre);
}

internal static partial class Registro
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{Contexto}: {Pendientes} migraciones pendientes")]
    public static partial void Pendientes(ILogger log, string contexto, int pendientes);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Contexto}: esquema al día")]
    public static partial void AlDia(ILogger log, string contexto);
}
