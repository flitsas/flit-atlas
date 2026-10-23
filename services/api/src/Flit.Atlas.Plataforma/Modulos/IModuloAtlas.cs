using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Plataforma.Modulos;

/// <summary>
/// Contrato de un módulo funcional del backend (prompt maestro §10, ATLAS-ADR-0016 y ATLAS-ADR-0026). Cada
/// <c>Flit.Atlas.Modulos.&lt;X&gt;</c> lo implementa una vez y el host lo registra en una sola lista
/// (<c>ModulosAtlas.Registrados</c>, en <c>Flit.Atlas.Composicion</c>), que usan atlas-api y el migrador. Agregar un
/// módulo no obliga a tocar los demás: solo depende de <c>Flit.Atlas.Plataforma</c> (lo verifica una prueba de
/// arquitectura).
/// </summary>
public interface IModuloAtlas
{
    /// <summary>
    /// Id en snake_case (<c>soporte</c>, <c>hola</c>). Define todo lo demás: rutas bajo <c>/api/v1/&lt;id&gt;</c>, bandera
    /// <c>modulo.&lt;id&gt;</c>, prefijo de tablas <c>&lt;id&gt;_</c>, historial <c>__ef_migraciones_&lt;id&gt;</c>,
    /// permisos <c>atlas.&lt;id&gt;.*</c> y eventos <c>atlas.&lt;id&gt;.*</c>.
    /// </summary>
    string Id { get; }

    /// <summary>Nombre para mostrar; es también la etiqueta de sus operaciones en el contrato OpenAPI.</summary>
    string Nombre { get; }

    /// <summary>Una o dos frases: la descripción de su etiqueta en el contrato OpenAPI.</summary>
    string Descripcion { get; }

    /// <summary>
    /// Valor de la bandera cuando el ambiente aún no la tiene en <c>plataforma_modulo</c>. El migrador crea la fila con
    /// este valor la primera vez y nunca la sobrescribe: desde ahí manda la base de cada ambiente.
    /// </summary>
    bool ActivoPorDefecto { get; }

    /// <summary>Permisos del módulo y a qué roles se conceden (ATLAS-ADR-0023). Su <c>Modulo</c> es <see cref="Id"/>.</summary>
    ManifiestoPermisos Permisos { get; }

    /// <summary>
    /// <see cref="DbContext"/> del módulo (derivado de <see cref="Datos.ContextoAtlas"/>, con su propio historial de
    /// migraciones), o <c>null</c> si no tiene tablas. El migrador lo migra después del núcleo.
    /// </summary>
    Type? ContextoDatos { get; }

    /// <summary>Eventos que publica en <c>flit.atlas</c>; cada uno también va en el AsyncAPI (ATLAS-ADR-0021).</summary>
    IReadOnlyList<TipoEvento> EventosPublicados => [];

    /// <summary>Eventos de otros módulos que consume (la comunicación entre módulos va por eventos, ATLAS-ADR-0016).</summary>
    IReadOnlyList<TipoEvento> EventosConsumidos => [];

    /// <summary>Tipos de notificación que envía (códigos <c>&lt;id&gt;.&lt;tipo&gt;</c>), para el centro de notificaciones.</summary>
    IReadOnlyList<TipoNotificacion> TiposNotificacion => [];

    /// <summary>
    /// Scopes de los tokens de OpenIddict que usan sus endpoints para clientes que no son el navegador (ATLAS-ADR-0024):
    /// <c>atlas.&lt;id&gt;</c> o <c>atlas.&lt;id&gt;.&lt;accion&gt;</c>. La composición los registra en la identidad junto al
    /// manifiesto de permisos del módulo.
    /// </summary>
    IReadOnlyList<ScopeModulo> Scopes => [];

    /// <summary>
    /// Registra su <see cref="ContextoDatos"/> (con <see cref="Datos.ContextoAtlas.ConfigurarOpciones"/>). Lo llaman
    /// atlas-api, el migrador y las pruebas; no registra nada más.
    /// </summary>
    void RegistrarDatos(IServiceCollection servicios, string cadenaConexion);

    /// <summary>
    /// Servicios del módulo en atlas-api: dominio, trabajos en segundo plano, proveedores de búsqueda
    /// (<see cref="ExtensionesBusqueda.AgregarProveedorBusqueda{TProveedor}"/>), etc. El manifiesto de permisos ya lo
    /// registra el host.
    /// </summary>
    void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion);

    /// <summary>
    /// Sus endpoints, en un grupo que el host ya montó en <c>/api/v1/&lt;id&gt;</c> con la etiqueta del módulo, sesión
    /// obligatoria y la bandera (404 si está apagada para la organización de la persona). Cada endpoint declara su
    /// permiso con <c>RequierePermiso</c>.
    /// </summary>
    void MapearEndpoints(RouteGroupBuilder grupo);
}

/// <summary>Scope de OpenIddict que declara un módulo; el dueño es el módulo que lo declara.</summary>
/// <param name="Nombre"><c>atlas.&lt;id&gt;</c> o <c>atlas.&lt;id&gt;.&lt;accion&gt;</c>.</param>
/// <param name="Descripcion">Qué permite, en español.</param>
public sealed record ScopeModulo(string Nombre, string Descripcion);
