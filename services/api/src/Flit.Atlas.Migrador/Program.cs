// Migrador de Flit Atlas: aplica las migraciones de EF Core del núcleo (y, más adelante, de cada módulo), aplica a la
// base los roles y la matriz rol × permiso del código (ATLAS-ADR-0023) y termina.
// En local lo corre el AppHost antes de atlas-api; en dev/qa/pdn corre como Job o initContainer (HU 1.8).
// Usa el usuario del servicio (flit_atlas), dueño del esquema atlas según el script de flit.

using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
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

// Roles del §7 y matriz rol × permiso de los manifiestos (idempotente). Cuando lleguen los módulos (IModuloAtlas), sus
// manifiestos se suman aquí desde el mismo registro que usa atlas-api.
using (var alcance = host.Services.CreateScope())
{
    var catalogo = CatalogoPermisos.Crear(PermisosIdentidad.ManifiestosDelNucleo);
    var sembrado = await SembradorPermisos.SembrarAsync(alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>(), catalogo);
    Registro.Permisos(log, sembrado.RolesCreados, sembrado.RolesActualizados, sembrado.PermisosCreados, sembrado.PermisosRetirados,
        sembrado.ConcesionesCreadas, sembrado.ConcesionesRetiradas);
}

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Roles y permisos: {RolesCreados} roles creados, {RolesActualizados} actualizados; {PermisosCreados} permisos creados, {PermisosRetirados} retirados; {ConcesionesCreadas} concesiones creadas, {ConcesionesRetiradas} retiradas")]
    public static partial void Permisos(ILogger log, int rolesCreados, int rolesActualizados, int permisosCreados, int permisosRetirados, int concesionesCreadas, int concesionesRetiradas);
}
