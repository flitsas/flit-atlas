using Flit.Atlas.Plataforma.Datos;

namespace Flit.Atlas.Plataforma.Entidades;

/// <summary>Notificación en la app (centro de notificaciones, prompt maestro §8.9).</summary>
public sealed class Notificacion : Entidad
{
    public required Guid PersonaId { get; set; }

    /// <summary>Tipo de notificación (<c>soporte.turno_proximo</c>, …); define las preferencias.</summary>
    public required string Tipo { get; set; }

    public required string Titulo { get; set; }

    public required string Cuerpo { get; set; }

    /// <summary>Ruta interna de la PWA a la que lleva la notificación.</summary>
    public string? Enlace { get; set; }

    public DateTimeOffset? LeidaEn { get; set; }
}

public enum CanalNotificacion
{
    App,
    Push,
    Correo,
    Teams,
}

/// <summary>Preferencia de una persona por tipo de notificación y canal.</summary>
public sealed class PreferenciaNotificacion
{
    public required Guid PersonaId { get; init; }

    public required string Tipo { get; init; }

    public required CanalNotificacion Canal { get; init; }

    public bool Activa { get; set; } = true;

    public DateTimeOffset ActualizadoEn { get; set; }
}

/// <summary>Suscripción de Web Push (VAPID) de un dispositivo (prompt maestro §11).</summary>
public sealed class SuscripcionPush : Entidad
{
    public required Guid PersonaId { get; set; }

    public required string Endpoint { get; set; }

    public required string P256dh { get; set; }

    public required string Auth { get; set; }

    public string? Dispositivo { get; set; }

    public DateTimeOffset? UltimoUsoEn { get; set; }

    /// <summary>Envíos fallidos seguidos (5xx o red); al llegar al tope la suscripción se borra (HU 1.6).</summary>
    public int FallosConsecutivos { get; set; }

    public DateTimeOffset? UltimoFalloEn { get; set; }
}
