using Flit.Atlas.Modulos.Diagramas.Datos;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Modulos.Diagramas;

/// <summary>
/// Códigos que exige el módulo (ATLAS-ADR-0054 §1). Siguen declarados en el núcleo: <c>atlas.arquitectura.*</c> no lleva el
/// prefijo del módulo. Leer pide <c>atlas.docs.ver</c>, porque un diagrama funcional se incrusta en los documentos.
/// </summary>
public static class PermisosDiagramas
{
    public const string Ver = "atlas.docs.ver";
    public const string Editar = "atlas.diagramas.editar";
}

/// <summary>
/// Diagramas que no viven en el código (F6, prompt maestro §8.6, ATLAS-ADR-0054). HU 6.6: lienzo con modelo propio, versiones,
/// comentarios y referencias al catálogo. HU 6.7: Mermaid y BPMN con fuente versionada y XML limpiado en el servidor. Viene
/// encendido (bandera <c>modulo.diagramas</c>), como su entrada en la PWA.
/// </summary>
public sealed class ModuloDiagramas : IModuloAtlas
{
    public const string IdModulo = "diagramas";

    public string Id => IdModulo;

    public string Nombre => "Diagramas";

    public string Descripcion =>
        "Diagramas hechos a mano en Atlas: lienzo con modelo propio, Mermaid y BPMN, con versiones inmutables, comentarios, referencias al catálogo y su nivel de visibilidad aplicado en cada consulta.";

    public bool ActivoPorDefecto => true;

    /// <summary>Sin permisos propios: viven en el manifiesto <c>arquitectura</c> del núcleo.</summary>
    public ManifiestoPermisos Permisos { get; } = new(IdModulo, "Diagramas (módulo)", []);

    public Type? ContextoDatos => typeof(DiagramasDbContext);

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<DiagramasDbContext>(o => DiagramasDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion) =>
        servicios.AgregarProveedorBusqueda<BusquedaDiagramas>();

    public void MapearEndpoints(RouteGroupBuilder grupo) => EndpointsDiagramas.Mapear(grupo);
}
