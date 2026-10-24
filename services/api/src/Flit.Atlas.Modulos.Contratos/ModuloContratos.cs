using Flit.Atlas.Modulos.Contratos.Canales;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Armar, revisar, publicar, fijar y archivar paquetes (HU 4.1, ATLAS-ADR-0041): global para el líder técnico y, por
    /// el rol implícito, sobre los aplicativos del responsable. No lleva el prefijo del módulo, así que sigue declarado en
    /// <see cref="ManifiestosNucleo.Portal"/> (ATLAS-ADR-0035); el módulo solo lo exige.
    /// </summary>
    public const string PaquetesGestionar = "atlas.paquetes.gestionar";

    /// <summary>Entrar al portal de clientes (HU 4.4, ATLAS-ADR-0043); lo que ve lo recorta <see cref="Portal.AccesoPortal"/>. Del manifiesto «portal».</summary>
    public const string PortalVer = "atlas.portal.ver";

    /// <summary>Exportar (HU 4.5); en el portal, además, la compartición con <c>puede_exportar</c>. Del manifiesto «portal».</summary>
    public const string ExportacionesSolicitar = "atlas.exportaciones.solicitar";

    /// <summary>Sandbox (HU 4.7); en el portal, además, la compartición con <c>puede_sandbox</c>. Del manifiesto «portal».</summary>
    public const string SandboxUsar = "atlas.sandbox.usar";

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

    /// <summary>
    /// Alguien pidió una exportación (HU 4.5, ATLAS-ADR-0042): la entrada ya está en el S3. Lo consume atlas-docs (cola
    /// <c>atlas.docs.exportaciones</c>), que la genera.
    /// </summary>
    public static readonly TipoEvento ExportacionSolicitada = new("atlas.contratos.exportacion_solicitada", 1);

    /// <summary>atlas-docs tomó la exportación (lo publica atlas-docs en <c>flit.atlas.docs</c>; lo consume atlas-api).</summary>
    public static readonly TipoEvento ExportacionIniciada = new("atlas.contratos.exportacion_iniciada", 1);

    /// <summary>Los archivos quedaron en el S3 (lo publica atlas-docs en <c>flit.atlas.docs</c>; lo consume atlas-api).</summary>
    public static readonly TipoEvento ExportacionLista = new("atlas.contratos.exportacion_lista", 1);

    /// <summary>La exportación no se pudo generar (lo publica atlas-docs en <c>flit.atlas.docs</c>; lo consume atlas-api).</summary>
    public static readonly TipoEvento ExportacionFallida = new("atlas.contratos.exportacion_fallida", 1);
}

/// <summary>
/// Contratos de integración (F3, prompt maestro §8.4, plan 00 §8 y ATLAS-ADR-0035). HU 3.1 y 3.2: la publicación desde
/// la CI con el token OIDC de GitHub Actions y la lista blanca del catálogo, versiones inmutables (201, 200 idempotente,
/// 409 y 422), el índice de operaciones y canales, y la lectura interna. HU 3.3 y 3.4: la versión base de pdn para la CI,
/// los canales por ambiente que mueve el CD (con historial) y el Consolidado. HU 3.7: guías con revisiones inmutables,
/// restauración y publicación aprobada por el responsable (ATLAS-ADR-0040). HU 4.1: paquetes con revisiones inmutables,
/// publicación y fijación de una revisión anterior (ATLAS-ADR-0041). HU 4.2: la poda de specs para clientes
/// (<see cref="Poda.Podadores"/>, ATLAS-ADR-0038). HU 4.3 y 4.4: comparticiones de paquetes con organizaciones y el portal
/// de clientes en <c>/api/v1/portal</c> (ATLAS-ADR-0043). HU 4.7: el sandbox nivel 1 con Microcks, con el spec podado y un
/// proxy con llave en atlas-api (ATLAS-ADR-0045). Viene encendido (bandera <c>modulo.contratos</c>), como su entrada en la PWA.
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

    public IReadOnlyList<TipoEvento> EventosPublicados => [EventosContratos.VersionPublicada, EventosContratos.CanalActualizado, EventosContratos.ExportacionSolicitada];

    /// <summary>Los resultados de atlas-docs (HU 4.5): no son de otro módulo, sino del servicio que genera los exportes.</summary>
    public IReadOnlyList<TipoEvento> EventosConsumidos =>
        [EventosContratos.ExportacionIniciada, EventosContratos.ExportacionLista, EventosContratos.ExportacionFallida];

    public IReadOnlyList<TipoNotificacion> TiposNotificacion => [Exportaciones.ManejadorResultadosExportacion.Notificacion];

    /// <summary>
    /// Para el token de servicio con audiencia <c>atlas-docs</c> (ATLAS-ADR-0044): quien llame a <c>POST /exportes</c> de
    /// atlas-docs lo pide con <c>client_credentials</c>.
    /// </summary>
    public IReadOnlyList<ScopeModulo> Scopes => [new(ScopeExportar, "Generar exportaciones en atlas-docs (servicio a servicio).")];

    public const string ScopeExportar = "atlas.contratos.exportar";

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<ContratosDbContext>(o => ContratosDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AgregarProveedorBusqueda<BusquedaContratos>();
        servicios.AddOptions<OpcionesCanales>().BindConfiguration(OpcionesCanales.Seccion).PostConfigure(o => o.Normalizar());
        // Exportaciones (HU 4.5, ATLAS-ADR-0042): la cola de resultados de atlas-docs, la retención y la consulta del portal.
        servicios.AddOptions<Exportaciones.OpcionesExportaciones>().BindConfiguration(Exportaciones.OpcionesExportaciones.Seccion).ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<Exportaciones.OpcionesExportaciones>, Exportaciones.ValidadorOpcionesExportaciones>();
        servicios.AgregarManejadorEventos<Exportaciones.ManejadorResultadosExportacion>();
        servicios.AddHostedService<Exportaciones.VencedorExportaciones>();
        // Portal de clientes (HU 4.3 y 4.4, ATLAS-ADR-0043): el acceso se calcula por petición; la caché de specs podados
        // es del proceso y tiene tope de tamaño.
        servicios.AddOptions<Portal.OpcionesPortal>().BindConfiguration(Portal.OpcionesPortal.Seccion);
        servicios.AddSingleton<Portal.CachePodas>();
        servicios.AddScoped<Portal.AccesoPortal>();
        servicios.AddScoped<Portal.SpecsDelPortal>();
        // Sandbox nivel 1 con Microcks (HU 4.7, ATLAS-ADR-0045): sin Contratos:Sandbox:UrlMicrocks no está disponible (503)
        // y el trabajo periódico no hace nada.
        servicios.AddOptions<Sandbox.OpcionesSandbox>().BindConfiguration(Sandbox.OpcionesSandbox.Seccion);
        servicios.AddHttpClient(Sandbox.ClienteMicrocks.NombreCliente).RemoveAllLoggers();
        servicios.AddSingleton<Sandbox.IClienteMicrocks, Sandbox.ClienteMicrocks>();
        servicios.AddScoped<Sandbox.ServicioSandbox>();
        servicios.AddHostedService<Sandbox.TrabajoSandbox>();
    }

    public void MapearEndpoints(RouteGroupBuilder grupo)
    {
        EndpointsPublicacion.Mapear(grupo);
        EndpointsCanales.Mapear(grupo);
        EndpointsConsolidado.Mapear(grupo);
        EndpointsContratos.Mapear(grupo);
        Guias.EndpointsGuias.Mapear(grupo);
        EndpointsVistaContrato.Mapear(grupo);
        Paquetes.EndpointsPaquetes.Mapear(grupo);
        Comparticiones.EndpointsComparticiones.Mapear(grupo);
        Exportaciones.EndpointsExportaciones.Mapear(grupo);
        Sandbox.EndpointsSandbox.Mapear(grupo);
    }

    public void MapearEndpointsPortal(RouteGroupBuilder portal)
    {
        Portal.EndpointsPortal.Mapear(portal);
        Exportaciones.EndpointsExportaciones.MapearPortal(portal);
        Sandbox.EndpointsSandbox.MapearPortal(portal);
    }
}
