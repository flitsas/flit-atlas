namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Cuentas externas e invitaciones (sección <c>Identidad:Externos</c>, HU 1.5, ATLAS-ADR-0025). Los valores por defecto
/// son los del prompt maestro §6.2 y de ATLAS-ADR-0004 §5.
/// </summary>
public sealed class OpcionesExternos
{
    public const string Seccion = "Identidad:Externos";

    /// <summary>URL pública del ambiente (<c>Atlas:UrlPublica</c>), con la que se arman los enlaces de los correos.</summary>
    public const string ClaveUrlPublica = "Atlas:UrlPublica";

    /// <summary>El enlace de la invitación vence a las 72 h de cada envío.</summary>
    public TimeSpan VigenciaEnlace { get; set; } = TimeSpan.FromHours(72);

    /// <summary>Vencimiento máximo del acceso de un externo (§6.2: «máximo configurable, por defecto 90 días»).</summary>
    public TimeSpan VigenciaMaximaAcceso { get; set; } = TimeSpan.FromDays(90);

    /// <summary>El enlace de recuperación de la cuenta vence a los 30 minutos (ATLAS-ADR-0004 §5).</summary>
    public TimeSpan VigenciaRecuperacion { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Cuánto vive un paso intermedio del registro o del ingreso (opciones de passkey, clave TOTP pendiente, segundo
    /// paso del ingreso). Lo mismo que el tiempo del autenticador de WebAuthn.
    /// </summary>
    public TimeSpan VigenciaPaso { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Correos de recuperación por cuenta en una hora (más allá, se responde igual pero no se envía).</summary>
    public int RecuperacionesPorHora { get; set; } = 3;

    /// <summary>Consultar Have I Been Pwned por rango de hash (k-anonimato). Sin red, queda la lista local.</summary>
    public bool ConsultarContrasenasFiltradas { get; set; } = true;
}
