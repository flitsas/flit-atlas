using Flit.Atlas.Plataforma.Entidades;

namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>
/// Tipo de notificación con sus canales por defecto. Las preferencias de la persona (tabla
/// <c>plataforma_preferencia_notificacion</c>) solo guardan lo que cambió frente a estos valores.
/// </summary>
public sealed record TipoNotificacion(string Codigo, string Nombre, string Descripcion, IReadOnlySet<CanalNotificacion> PorDefecto);

/// <summary>
/// Catálogo de tipos (prompt maestro §8.9). Por ahora los del núcleo; cada módulo agregará los suyos (p. ej.
/// <c>soporte.turno_proximo</c> en F2) al registrarse como proveedor de notificaciones (ATLAS-ADR-0016).
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

    /// <summary>Canales que existen hoy. Correo y Teams llegan con la abstracción de envío de las invitaciones (HU 1.5).</summary>
    public static readonly IReadOnlySet<CanalNotificacion> Disponibles = new HashSet<CanalNotificacion>
    {
        CanalNotificacion.App,
        CanalNotificacion.Push,
    };
}
