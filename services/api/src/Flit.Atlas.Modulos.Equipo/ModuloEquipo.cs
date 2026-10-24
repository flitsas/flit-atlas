using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Endpoints;
using Flit.Atlas.Modulos.Equipo.GitHub;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Modulos.Equipo;

/// <summary>
/// Permisos de Equipo y responsables (ATLAS-ADR-0023 y ATLAS-ADR-0031). Son los tres de la lista base del §7.4, que
/// desde la HU 2.3 publica este módulo (antes en <see cref="ManifiestosNucleo.Equipo"/>, con los mismos códigos y
/// concesiones: son contrato).
/// </summary>
public static class PermisosEquipo
{
    /// <summary>Directorio, reparto, vistas, historial, mapa de carga, CODEOWNERS sugerido, fichas y «¿A quién llamo?».</summary>
    public const string Ver = "atlas.equipo.ver";

    /// <summary>Importar el documento, cambiar y restaurar el reparto, traspasos, revisiones y usuarios de GitHub.</summary>
    public const string Gestionar = "atlas.equipo.gestionar";

    /// <summary>Teléfono y datos de contacto de las personas.</summary>
    public const string VerContacto = "atlas.equipo.ver_contacto";

    public static ManifiestoPermisos Manifiesto { get; } = new(ModuloEquipo.IdModulo, ManifiestosNucleo.Equipo.Nombre,
        [.. ManifiestosNucleo.Equipo.Permisos]);
}

/// <summary>
/// Módulo Equipo y responsables (HU 2.3, 2.4 y 2.5, prompt maestro §8.2, ATLAS-ADR-0031): reparto de responsables
/// principal y secundario por aplicativo del catálogo, importado del documento con vista previa; vistas por persona, por
/// producto y módulo y mapa de carga; historial versionado con comparación y restauración; traspaso acompañado; revisión
/// trimestral con recordatorio; rol implícito de editor; CODEOWNERS y fichas; «¿A quién llamo?». El directorio de personas
/// (HU 2.2) sigue en <c>/api/v1/plataforma/directorio</c>. Viene encendido, como su entrada en la PWA.
/// </summary>
public sealed class ModuloEquipo : IModuloAtlas
{
    public const string IdModulo = "equipo";

    public string Id => IdModulo;

    public string Nombre => "Equipo";

    public string Descripcion =>
        "Responsables principal y secundario de cada producto y módulo: reparto importado del documento, vistas por persona y por aplicativo, mapa de carga, historial versionado, traspasos, revisión trimestral, CODEOWNERS y «¿A quién llamo?».";

    public bool ActivoPorDefecto => true;

    public ManifiestoPermisos Permisos => PermisosEquipo.Manifiesto;

    public Type? ContextoDatos => typeof(EquipoDbContext);

    public IReadOnlyList<TipoNotificacion> TiposNotificacion => [RecordatorioRevision.Tipo];

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<EquipoDbContext>(o => EquipoDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion)
    {
        // Rol implícito por propiedad: reemplaza al vacío de la plataforma (§7.2).
        servicios.AddScoped<IProveedorRolesImplicitos, RolesImplicitosEquipo>();

        servicios.AddOptions<OpcionesGitHub>().BindConfiguration(OpcionesGitHub.Seccion);
        servicios.AddSingleton<CacheTokenGitHub>();
        servicios.AddHttpClient<ClienteGitHub>();

        servicios.AddOptions<OpcionesRevision>().BindConfiguration(OpcionesRevision.Seccion);
        servicios.AddHostedService<TrabajoRecordatorioRevision>();
    }

    public void MapearEndpoints(RouteGroupBuilder grupo) => EndpointsEquipo.Mapear(grupo);
}
