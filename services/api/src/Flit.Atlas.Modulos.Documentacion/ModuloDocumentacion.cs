using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.Plataforma.Busqueda;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>
/// Códigos que exige el módulo. Son de la lista base del §7.4 y no llevan su prefijo (<c>atlas.docs.*</c>, no
/// <c>atlas.documentacion.*</c>), así que siguen declarados en <see cref="ManifiestosNucleo.Documentacion"/> (ATLAS-ADR-0053
/// §1), como <c>atlas.paquetes.gestionar</c> en el portal (ATLAS-ADR-0035).
/// </summary>
public static class PermisosDocumentacion
{
    public const string Ver = "atlas.docs.ver";
    public const string Editar = "atlas.docs.editar";
    public const string Aprobar = "atlas.docs.aprobar";
}

/// <summary>Eventos que publica el módulo en <c>flit.atlas</c> (ATLAS-ADR-0018 y ATLAS-ADR-0053 §7).</summary>
public static class EventosDocumentacion
{
    /// <summary>Hay una versión nueva de un documento (no se emite si el contenido ya existía). Lo consumirá el asistente (F7).</summary>
    public static readonly TipoEvento VersionPublicada = new("atlas.documentacion.version_publicada", 1);
}

/// <summary>
/// Documentación viva (F5, prompt maestro §8.5, ATLAS-ADR-0053). HU 5.1: documentos con versiones inmutables y revisiones,
/// estados y aprobación del responsable. HU 5.2: publicación por lote desde la CI. HU 5.5: búsqueda full-text en español.
/// Viene encendido (bandera <c>modulo.documentacion</c>), como su entrada en la PWA.
/// </summary>
public sealed class ModuloDocumentacion : IModuloAtlas
{
    public const string IdModulo = "documentacion";

    public string Id => IdModulo;

    public string Nombre => "Documentación";

    public string Descripcion =>
        "Documentación técnica y funcional viva: lo que llega de los repos por la CI como versiones inmutables y lo que se escribe en Atlas con revisiones y aprobación del responsable, con su nivel de visibilidad aplicado en cada consulta.";

    public bool ActivoPorDefecto => true;

    /// <summary>Sin permisos propios: los <c>atlas.docs.*</c> viven en el manifiesto <c>docs</c> del núcleo.</summary>
    public ManifiestoPermisos Permisos { get; } = new(IdModulo, "Documentación (módulo)", []);

    public Type? ContextoDatos => typeof(DocumentacionDbContext);

    public IReadOnlyList<TipoEvento> EventosPublicados => [EventosDocumentacion.VersionPublicada];

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<DocumentacionDbContext>(o => DocumentacionDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AgregarProveedorBusqueda<BusquedaDocumentacion>();
        servicios.AddOptions<OpcionesDocumentacion>().BindConfiguration(OpcionesDocumentacion.Seccion).PostConfigure(o => o.Normalizar());
    }

    public void MapearEndpoints(RouteGroupBuilder grupo)
    {
        EndpointsPublicacionDocumentos.Mapear(grupo);
        EndpointsDocumentos.Mapear(grupo);
    }
}

/// <summary>Ambiente que despliega cada rama (ATLAS-ADR-0053 §4).</summary>
public sealed class AmbienteDocumentacion
{
    public string Nombre { get; set; } = string.Empty;

    public string Rama { get; set; } = string.Empty;
}

/// <summary>Sección <c>Documentacion</c>: por defecto, <c>develop</c> → dev, <c>staging</c> → qa y <c>release</c> → pdn.</summary>
public sealed class OpcionesDocumentacion
{
    public const string Seccion = "Documentacion";

    /// <summary>El canal que se lee por defecto.</summary>
    public const string CanalVigente = "pdn";

    public List<AmbienteDocumentacion> Ambientes { get; set; } = [];

    public void Normalizar()
    {
        if (Ambientes.Count == 0)
        {
            Ambientes.AddRange(
            [
                new AmbienteDocumentacion { Nombre = "dev", Rama = "develop" },
                new AmbienteDocumentacion { Nombre = "qa", Rama = "staging" },
                new AmbienteDocumentacion { Nombre = CanalVigente, Rama = "release" },
            ]);
        }
    }

    public string? AmbienteDe(string rama) => Ambientes.FirstOrDefault(a => a.Rama == rama)?.Nombre;
}
