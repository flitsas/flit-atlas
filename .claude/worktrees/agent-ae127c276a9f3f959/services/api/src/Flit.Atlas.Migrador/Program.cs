// Migrador de Flit Atlas: aplica las migraciones de EF Core del núcleo y de cada módulo registrado, crea las banderas de
// los módulos nuevos, aplica a la base los roles y la matriz rol × permiso del código (ATLAS-ADR-0023) y termina. Los
// módulos salen de la misma lista que usa atlas-api (ModulosAtlas, ATLAS-ADR-0026).
// En local lo corre el AppHost antes de atlas-api; en dev/qa/pdn corre como Job o initContainer (HU 1.8).
// Usa el usuario del servicio (flit_atlas), dueño del esquema atlas según el script de flit.

using Flit.Atlas.Composicion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
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
builder.Services.RegistrarDatosModulos(cadena, ModulosAtlas.Registrados);

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Flit.Atlas.Migrador");

// Orden por dependencia (ATLAS-ADR-0016): primero el núcleo, después cada módulo (sus políticas RLS usan funciones del
// núcleo).
await MigrarAsync("plataforma", sp => sp.GetRequiredService<PlataformaDbContext>());
await MigrarAsync("identidad", sp => sp.GetRequiredService<IdentidadDbContext>());
foreach (var modulo in ModulosAtlas.Registrados.Where(m => m.ContextoDatos is not null))
{
    await MigrarAsync(modulo.Id, sp => ExtensionesModulos.ContextoDatosDe(sp, modulo)!);
}

using (var alcance = host.Services.CreateScope())
{
    var plataforma = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();

    // Banderas de los módulos nuevos, con su valor por defecto; las existentes no se tocan (las decide cada ambiente).
    var banderas = await ExtensionesModulos.RegistrarBanderasAsync(plataforma, ModulosAtlas.Registrados, TimeProvider.System);
    Registro.Banderas(log, ModulosAtlas.Registrados.Count, banderas);

    // Roles del §7 y matriz rol × permiso de los manifiestos del núcleo y de los módulos (idempotente).
    var catalogo = CatalogoPermisos.Crear(ModulosAtlas.Manifiestos);
    var sembrado = await SembradorPermisos.SembrarAsync(plataforma, catalogo);
    Registro.Permisos(log, sembrado.RolesCreados, sembrado.RolesActualizados, sembrado.PermisosCreados, sembrado.PermisosRetirados,
        sembrado.ConcesionesCreadas, sembrado.ConcesionesRetiradas);
}

async Task MigrarAsync(string nombre, Func<IServiceProvider, DbContext> resolver)
{
    using var alcance = host.Services.CreateScope();
    var contexto = resolver(alcance.ServiceProvider);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Módulos: {Registrados} registrados; {Creadas} banderas nuevas")]
    public static partial void Banderas(ILogger log, int registrados, int creadas);

    [LoggerMessage(Level = LogLevel.Information, Message = "Roles y permisos: {RolesCreados} roles creados, {RolesActualizados} actualizados; {PermisosCreados} permisos creados, {PermisosRetirados} retirados; {ConcesionesCreadas} concesiones creadas, {ConcesionesRetiradas} retiradas")]
    public static partial void Permisos(ILogger log, int rolesCreados, int rolesActualizados, int permisosCreados, int permisosRetirados, int concesionesCreadas, int concesionesRetiradas);
}
