using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Modulos.Arquitectura.Drift;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Flit.Atlas.Plataforma.Permisos.RolesAtlas;

namespace Flit.Atlas.Modulos.Arquitectura;

/// <summary>
/// Permisos del módulo: los códigos y las concesiones del §7.4, que se mudan de <c>ManifiestosNucleo</c> sin cambios
/// (ATLAS-ADR-0056 §1). <c>ext.asesor_comercial</c> conserva <see cref="Ver"/>, pero hoy no ve nada: <see cref="AccesoArquitectura"/>
/// solo da niveles a roles internos.
/// </summary>
public static class PermisosArquitectura
{
    public const string Ver = "atlas.arquitectura.ver";
    public const string VerDespliegue = "atlas.arquitectura.ver_despliegue";

    /// <summary>Solo para resolver los contratos de la ficha de un elemento; no lo declara este módulo.</summary>
    public const string VerContratos = "atlas.contratos.ver_internos";

    public static ManifiestoPermisos Manifiesto { get; } = new(ModuloArquitectura.IdModulo, "Arquitectura",
    [
        new(Ver, "Ver los diagramas de arquitectura (LikeC4) según su visibilidad.", LiderTecnico, Desarrollador, AsesorComercial),
        new(VerDespliegue, "Ver las vistas de despliegue por ambiente y el drift.", LiderTecnico, Desarrollador),
    ]);
}

/// <summary>Sección <c>Arquitectura</c>. Ninguna credencial tiene valor por defecto: sin configuración, Argo CD queda «sin datos».</summary>
public sealed class OpcionesArquitectura
{
    public const string Seccion = "Arquitectura";

    /// <summary>Rama que despliega cada canal: de aquí sale el canal de un modelo.</summary>
    public Dictionary<string, string> RamasPorCanal { get; set; } = new(StringComparer.Ordinal)
    {
        ["develop"] = "dev",
        ["staging"] = "qa",
        ["release"] = "pdn",
    };

    /// <summary>
    /// Repos que pueden publicar snapshots. El catálogo no tiene un tipo de fuente <c>despliegue</c> (solo <c>likec4</c>), así que esta
    /// lista evita que cualquier repo con modelo publique también lo desplegado (ATLAS-ADR-0056 §2). Vacía: <c>flitsas/flit-gitops</c>.
    /// </summary>
    public List<string> RepositoriosDespliegue { get; set; } = [];

    /// <summary>Trabajo de drift; se apaga en las pruebas que lo corren a mano.</summary>
    public bool DriftHabilitado { get; set; } = true;

    /// <summary>Cada cuánto se revisa el drift (plan 01 §11.4).</summary>
    public TimeSpan IntervaloDrift { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Con <c>reconciledAt</c> más viejo que esto, Argo se da por ciego aunque diga «Synced» (plan 01 §11.4).</summary>
    public TimeSpan ToleranciaReconciliacion { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Argo CD por clúster. Vacía por defecto: nada se inventa.</summary>
    public List<ClusterArgo> Argo { get; set; } = [];

    public void Normalizar()
    {
        if (RepositoriosDespliegue.Count == 0)
        {
            RepositoriosDespliegue.Add("flitsas/flit-gitops");
        }
    }
}

/// <summary>Cómo leer el Argo CD de un clúster. El token es de solo lectura y llega por un SealedSecret; nunca al repo ni a los logs.</summary>
public sealed class ClusterArgo
{
    public string Cluster { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Arquitectura como código (F6, HU 6.3 a 6.5, prompt maestro §8.6, plan 01, ATLAS-ADR-0056): recibe por CI los modelos de LikeC4 y
/// los snapshots de despliegue, los guarda como versiones inmutables, los muestra y detecta el drift. Viene encendido, como los
/// demás módulos de negocio: sin publicaciones solo muestra una lista vacía a quien tiene el permiso.
/// </summary>
public sealed class ModuloArquitectura : IModuloAtlas
{
    public const string IdModulo = "arquitectura";

    public string Id => IdModulo;

    public string Nombre => "Arquitectura";

    public string Descripcion =>
        "Arquitectura como código: modelos de LikeC4 y snapshots de despliegue publicados por CI como versiones inmutables por sha y canal, sus vistas, y el drift entre el modelo, gitops y Argo CD.";

    public bool ActivoPorDefecto => true;

    public ManifiestoPermisos Permisos => PermisosArquitectura.Manifiesto;

    public Type? ContextoDatos => typeof(ArquitecturaDbContext);

    public IReadOnlyList<TipoNotificacion> TiposNotificacion => [AlertasDrift.Tipo];

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<ArquitecturaDbContext>(o => ArquitecturaDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddOptions<OpcionesArquitectura>().BindConfiguration(OpcionesArquitectura.Seccion).PostConfigure(o => o.Normalizar());
        servicios.AddHttpClient(LectorArgoCdHttp.NombreCliente, c => c.Timeout = TimeSpan.FromSeconds(15));
        servicios.AddSingleton<ILectorArgoCd, LectorArgoCdHttp>();
        servicios.AddSingleton<DisparadorDrift>();
        servicios.AddScoped<AlertasDrift>();
        servicios.AddScoped<ServicioDrift>();
        servicios.AddHostedService<TrabajoDrift>();
    }

    public void MapearEndpoints(RouteGroupBuilder grupo)
    {
        EndpointsPublicacionArquitectura.Mapear(grupo);
        EndpointsArquitectura.Mapear(grupo);
        EndpointsDespliegue.Mapear(grupo);
        EndpointsDrift.Mapear(grupo);
    }
}
