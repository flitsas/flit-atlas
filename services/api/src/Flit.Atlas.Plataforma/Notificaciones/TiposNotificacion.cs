using Flit.Atlas.Plataforma.Entidades;

namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>
/// Tipo de notificación con sus canales por defecto. Las preferencias de la persona (tabla
/// <c>plataforma_preferencia_notificacion</c>) solo guardan lo que cambió frente a estos valores.
/// </summary>
/// <param name="Canales">
/// Canales por los que puede salir este tipo (agregado en la HU 2.11). Vacío: los del núcleo
/// (<see cref="TiposNotificacion.Disponibles"/>, app y push). Un canal que el ambiente no tiene configurado (correo sin
/// proveedor, Teams sin integración) se muestra como no disponible aunque esté aquí.
/// </param>
public sealed record TipoNotificacion(
    string Codigo, string Nombre, string Descripcion, IReadOnlySet<CanalNotificacion> PorDefecto, IReadOnlySet<CanalNotificacion>? Canales = null)
{
    /// <summary>Canales que este tipo admite (sin mirar el ambiente).</summary>
    public IReadOnlySet<CanalNotificacion> CanalesPosibles => Canales ?? TiposNotificacion.Disponibles;
}

/// <summary>
/// Catálogo de tipos del núcleo (prompt maestro §8.9). Los módulos declaran los suyos en
/// <c>IModuloAtlas.TiposNotificacion</c> (p. ej. <c>soporte.recordatorio_turno</c>); <see cref="CatalogoNotificaciones"/>
/// los junta para el centro de notificaciones y sus preferencias.
/// </summary>
public static class TiposNotificacion
{
    public static readonly TipoNotificacion Avisos = new(
        "plataforma.aviso", "Avisos de Atlas", "Novedades, mantenimientos y módulos nuevos.",
        new HashSet<CanalNotificacion> { CanalNotificacion.App, CanalNotificacion.Push });

    public static readonly TipoNotificacion Prueba = new(
        "plataforma.prueba", "Notificaciones de prueba", "Las que envías desde Ajustes para revisar este dispositivo.",
        new HashSet<CanalNotificacion> { CanalNotificacion.App, CanalNotificacion.Push });

    public static IReadOnlyList<TipoNotificacion> Todos { get; } = [Avisos, Prueba];

    public static TipoNotificacion? Buscar(string codigo) => Todos.FirstOrDefault(t => t.Codigo == codigo);

    /// <summary>
    /// Canales de un tipo que no declara los suyos: la bandeja y push. Correo y Teams solo salen en los tipos que los
    /// declaran (los recordatorios de soporte, HU 2.11).
    /// </summary>
    public static readonly IReadOnlySet<CanalNotificacion> Disponibles = new HashSet<CanalNotificacion>
    {
        CanalNotificacion.App,
        CanalNotificacion.Push,
    };
}
