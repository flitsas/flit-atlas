using Flit.Atlas.Modulos.Contratos.Canales;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Modulos.Contratos;

/// <summary>
/// Permisos de Contratos (prompt maestro §7.4, ATLAS-ADR-0023 y ATLAS-ADR-0035): los <c>atlas.contratos.*</c> de la lista
/// base, mudados de <see cref="ManifiestosNucleo.Contratos"/> con los mismos códigos y concesiones. Publicar desde la CI
/// no es un permiso de persona: lo autoriza el token OIDC de GitHub Actions y la lista blanca del catálogo.
/// </summary>
public static class PermisosContratos
{
    /// <summary>Ver los contratos internos, sus versiones, sus specs y su índice de operaciones.</summary>
    public const string VerInternos = "atlas.contratos.ver_internos";

    /// <summary>Publicar a mano, por excepción, y deprecar versiones (HU siguientes).</summary>
    public const string PublicarManual = "atlas.contratos.publicar_manual";

    /// <summary>Guías de integración con revisiones (HU 3.7).</summary>
    public const string GuiasEditar = "atlas.contratos.guias.editar";

    public static ManifiestoPermisos Manifiesto { get; } =
        new(ModuloContratos.IdModulo, ManifiestosNucleo.Contratos.Nombre, [.. ManifiestosNucleo.Contratos.Permisos]);
}

/// <summary>Eventos que publica el módulo en <c>flit.atlas</c> (ATLAS-ADR-0018 y ATLAS-ADR-0021).</summary>
public static class EventosContratos
{
    /// <summary>
    /// Una versión nueva de un contrato quedó publicada (no se emite en la respuesta idempotente). Lo consumirán el
    /// asistente (reindexar), el sandbox (importar a Microcks) y las notificaciones (plan 00 §12).
    /// </summary>
    public static readonly TipoEvento VersionPublicada = new("atlas.contratos.version_publicada", 1);

    /// <summary>
    /// El CD avisó que otra versión de un contrato corre en un ambiente (HU 3.4; no se emite si el canal ya la tenía). Lo
    /// consumirá arquitectura para relacionar el despliegue con la versión del contrato (plan 00 §12).
    /// </summary>
    public static readonly TipoEvento CanalActualizado = new("atlas.contratos.canal_actualizado", 1);
}

/// <summary>
/// Contratos de integración (F3, prompt maestro §8.4, plan 00 §8 y ATLAS-ADR-0035). HU 3.1 y 3.2: la publicación desde
/// la CI con el token OIDC de GitHub Actions y la lista blanca del catálogo, versiones inmutables (201, 200 idempotente,
/// 409 y 422), el índice de operaciones y canales, y la lectura interna. HU 3.3 y 3.4: la versión base de pdn para la CI,
/// los canales por ambiente que mueve el CD (con historial) y el Consolidado. HU 3.7: guías con revisiones inmutables,
/// restauración y publicación aprobada por el responsable (ATLAS-ADR-0040). Diff, paquetes y portal llegan en las HU
/// siguientes. Viene encendido (bandera <c>modulo.contratos</c>), como su entrada en la PWA.
/// </summary>
public sealed class ModuloContratos : IModuloAtlas
{
    public const string IdModulo = "contratos";

    public string Id => IdModulo;

    public string Nombre => "Contratos";

    public string Descripcion =>
        "Contratos de integración (OpenAPI, AsyncAPI y proto) publicados desde la CI de cada repo con el token OIDC de GitHub Actions y la lista blanca del catálogo: versiones semánticas inmutables con su hash y su procedencia, el spec de cada versión y el índice de operaciones, webhooks, canales y rpc.";

    public bool ActivoPorDefecto => true;

    public ManifiestoPermisos Permisos => PermisosContratos.Manifiesto;

    public Type? ContextoDatos => typeof(ContratosDbContext);

    public IReadOnlyList<TipoEvento> EventosPublicados => [EventosContratos.VersionPublicada, EventosContratos.CanalActualizado];

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<ContratosDbContext>(o => ContratosDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AgregarProveedorBusqueda<BusquedaContratos>();
        servicios.AddOptions<OpcionesCanales>().BindConfiguration(OpcionesCanales.Seccion).PostConfigure(o => o.Normalizar());
    }

    public void MapearEndpoints(RouteGroupBuilder grupo)
    {
        EndpointsPublicacion.Mapear(grupo);
        EndpointsCanales.Mapear(grupo);
        EndpointsConsolidado.Mapear(grupo);
        EndpointsContratos.Mapear(grupo);
        Guias.EndpointsGuias.Mapear(grupo);
        EndpointsVistaContrato.Mapear(grupo);
    }
}
