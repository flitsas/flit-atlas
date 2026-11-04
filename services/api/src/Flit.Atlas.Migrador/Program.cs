// Migrador de Flit Atlas: aplica las migraciones de EF Core del núcleo y de cada módulo registrado, crea las banderas de
// los módulos nuevos, aplica a la base los roles y la matriz rol × permiso del código (ATLAS-ADR-0023), corre la semilla
// idempotente de cada módulo (IModuloAtlas.SembrarAsync, p. ej. el catálogo, ATLAS-ADR-0028) y termina. Los
// módulos salen de la misma lista que usa atlas-api (ModulosAtlas, ATLAS-ADR-0026).
// En local lo corre el AppHost antes de atlas-api; en dev/qa/pdn corre como Job o initContainer (HU 1.8).
// Usa el rol del migrador, dueño del esquema atlas, y al terminar concede al rol de la aplicación (flit_atlas, sin
// propiedad ni BYPASSRLS) lo justo para que RLS aplique siempre (ATLAS-ADR-0050, deploy/runbook-atlas.md).

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

// Datos iniciales de cada módulo (idempotentes: el migrador corre en cada despliegue).
await ExtensionesModulos.SembrarModulosAsync(host.Services, ModulosAtlas.Registrados);

// Permisos del rol de la aplicación sobre lo que acaba de migrar (ATLAS-ADR-0050). Sin Atlas__BaseDeDatos__RolAplicacion,
// flit_atlas. Si el migrador corre con el mismo rol de la aplicación (local, o un ambiente sin separar), no hay nada que
// conceder: atlas-api lo advierte en local y no arranca en dev, qa ni pdn.
var rolAplicacion = builder.Configuration[RolesBaseDeDatos.ClaveRolAplicacion] ?? RolesBaseDeDatos.RolAplicacionPorDefecto;
using (var alcance = host.Services.CreateScope())
{
    var plataforma = alcance.ServiceProvider.GetRequiredService<PlataformaDbContext>();
    await plataforma.Database.OpenConnectionAsync();
    var estado = await RolesBaseDeDatos.RevisarAsync(plataforma.Database.GetDbConnection());
    if (estado.Rol == rolAplicacion)
    {
        Registro.SinSeparar(log, rolAplicacion);
    }
    else if (!await RolesBaseDeDatos.ExisteAsync(plataforma.Database.GetDbConnection(), rolAplicacion))
    {
        Registro.SinRolAplicacion(log, rolAplicacion);
    }
    else
    {
        await RolesBaseDeDatos.ConcederAsync(plataforma, rolAplicacion);
        Registro.Concedido(log, rolAplicacion);
    }
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
    [LoggerMessage(Level = LogLevel.Information, Message = "Permisos concedidos al rol de la aplicación {Rol}")]
    public static partial void Concedido(ILogger log, string rol);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El migrador corre con el rol de la aplicación {Rol}: no se separaron los roles (ATLAS-ADR-0050)")]
    public static partial void SinSeparar(ILogger log, string rol);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No existe el rol de la aplicación {Rol}: no se concedieron permisos (ATLAS-ADR-0050)")]
    public static partial void SinRolAplicacion(ILogger log, string rol);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Contexto}: {Pendientes} migraciones pendientes")]
    public static partial void Pendientes(ILogger log, string contexto, int pendientes);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Contexto}: esquema al día")]
    public static partial void AlDia(ILogger log, string contexto);

    [LoggerMessage(Level = LogLevel.Information, Message = "Módulos: {Registrados} registrados; {Creadas} banderas nuevas")]
    public static partial void Banderas(ILogger log, int registrados, int creadas);

    [LoggerMessage(Level = LogLevel.Information, Message = "Roles y permisos: {RolesCreados} roles creados, {RolesActualizados} actualizados; {PermisosCreados} permisos creados, {PermisosRetirados} retirados; {ConcesionesCreadas} concesiones creadas, {ConcesionesRetiradas} retiradas")]
    public static partial void Permisos(ILogger log, int rolesCreados, int rolesActualizados, int permisosCreados, int permisosRetirados, int concesionesCreadas, int concesionesRetiradas);
}
