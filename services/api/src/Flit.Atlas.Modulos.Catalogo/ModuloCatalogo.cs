using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Modulos.Catalogo.Semilla;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Flit.Atlas.Plataforma.Permisos.RolesAtlas;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>
/// Permisos del catálogo (prompt maestro §7.4, ATLAS-ADR-0023 y ATLAS-ADR-0028). <c>ver</c> y <c>gestionar</c> son los de
/// la lista base (antes en <c>ManifiestosNucleo</c>, con las mismas concesiones); <c>editar</c> es nuevo: cambia los datos
/// de un aplicativo existente sin tocar la jerarquía, y lo recibe también el responsable implícito, solo para su
/// aplicativo y lo que cuelga de él.
/// </summary>
public static class PermisosCatalogo
{
    public const string Ver = "atlas.catalogo.ver";
    public const string Gestionar = "atlas.catalogo.gestionar";
    public const string Editar = "atlas.catalogo.editar";

    public static ManifiestoPermisos Manifiesto { get; } = new(ModuloCatalogo.IdModulo, "Catálogo",
    [
        new(Ver, "Ver el catálogo de aplicativos y sus fichas.", [.. Empleados]),
        new(Gestionar, "Crear y editar productos, módulos y servicios del catálogo.", LiderTecnico),
        new(Editar, "Editar los datos de un aplicativo existente (descripción, repo, tecnología, ambientes, criticidad, estado, etiquetas) sin cambiar la jerarquía; el responsable, solo los suyos.", LiderTecnico, Responsable),
    ], [TiposAlcance.Aplicativo]);
}

/// <summary>
/// Catálogo de aplicativos (HU 2.1, prompt maestro §8.1, ATLAS-ADR-0028): jerarquía producto → módulo → servicio, CRUD con
/// permisos, la ficha que enlazan los demás módulos y la semilla con el inventario de FLIT. Viene <b>encendido</b>
/// (bandera <c>modulo.catalogo</c>): es la base común de los demás módulos.
/// </summary>
public sealed partial class ModuloCatalogo : IModuloAtlas
{
    public const string IdModulo = "catalogo";

    public string Id => IdModulo;

    public string Nombre => "Catálogo";

    public string Descripcion =>
        "Catálogo de aplicativos de FLIT: productos, módulos y servicios con su repo, tecnología, ambientes, criticidad, datos personales, estado y etiquetas, y la ficha de cada uno.";

    public bool ActivoPorDefecto => true;

    public ManifiestoPermisos Permisos => PermisosCatalogo.Manifiesto;

    public Type? ContextoDatos => typeof(CatalogoDbContext);

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<CatalogoDbContext>(o => CatalogoDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AgregarProveedorBusqueda<BusquedaCatalogo>();
        // Lectura para los demás módulos (responsables, ATLAS-ADR-0031).
        servicios.AddScoped<ILectorAplicativos, LectorAplicativos>();
    }

    public void MapearEndpoints(RouteGroupBuilder grupo) => EndpointsCatalogo.Mapear(grupo);

    /// <summary>
    /// La semilla del inventario (<c>Semilla/aplicativos.json</c>). La corre el migrador después de las migraciones; es
    /// idempotente por slug y nunca pisa lo que se editó o eliminó en Atlas.
    /// </summary>
    public async Task SembrarAsync(IServiceProvider servicios, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        var contexto = servicios.GetRequiredService<CatalogoDbContext>();
        var resultado = await SemillaCatalogo.AplicarAsync(contexto, SemillaCatalogo.Cargar(), ct).ConfigureAwait(false);
        var log = servicios.GetService<ILoggerFactory>()?.CreateLogger<ModuloCatalogo>() ?? NullLogger<ModuloCatalogo>.Instance;
        Sembrado(log, resultado.Creados, resultado.YaAplicados, resultado.Existentes, resultado.SinPadre);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catálogo: semilla aplicada; {Creados} aplicativos creados, {YaAplicados} ya aplicados, {Existentes} ya existían, {SinPadre} sin su padre")]
    private static partial void Sembrado(ILogger log, int creados, int yaAplicados, int existentes, int sinPadre);
}
