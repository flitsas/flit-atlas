// Migrador de Flit Atlas: aplica las migraciones de EF Core del núcleo (y, más adelante, de cada módulo) y termina.
// En local lo corre el AppHost antes de atlas-api; en dev/qa/pdn corre como Job o initContainer (HU 1.8).
// Usa el usuario del servicio (flit_atlas), dueño del esquema atlas según el script de flit.

using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

const string ClaveConexion = "ConnectionStrings:atlas";
ConfiguracionRequerida.Validar(builder.Configuration, ClaveConexion);

builder.AddServiceDefaults();
builder.Services.AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, builder.Configuration[ClaveConexion]!));

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Flit.Atlas.Migrador");

using (var alcance = host.Services.CreateScope())
{
    var contexto = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();
    var pendientes = (await contexto.Database.GetPendingMigrationsAsync()).ToList();
    Registro.Pendientes(log, "plataforma", pendientes.Count);
    await contexto.Database.MigrateAsync();
    Registro.AlDia(log, "plataforma");
}

internal static partial class Registro
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{Contexto}: {Pendientes} migraciones pendientes")]
    public static partial void Pendientes(ILogger log, string contexto, int pendientes);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Contexto}: esquema al día")]
    public static partial void AlDia(ILogger log, string contexto);
}
