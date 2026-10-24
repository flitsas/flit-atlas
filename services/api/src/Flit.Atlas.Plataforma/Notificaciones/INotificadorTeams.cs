using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>Aviso corto para Teams: título, texto y, si hay, la ruta interna de la PWA a la que lleva.</summary>
/// <param name="Enlace">Ruta interna (<c>/soporte</c>); la implementación arma la URL pública del ambiente.</param>
public sealed record AvisoTeams(string Titulo, string Texto, string? Enlace);

/// <summary>
/// Envío de avisos por Microsoft Teams (prompt maestro §8.3 y §8.9). Es la interfaz mínima que necesitan los
/// recordatorios de soporte (HU 2.11); la implementación real (Graph o Workflows, por verificar) la trae la integración
/// con Microsoft 365 de la HU 2.10, que la registra en lugar de <see cref="TeamsDesactivado"/>. Las implementaciones nunca
/// registran el texto del aviso ni a quién va.
/// </summary>
public interface INotificadorTeams
{
    /// <summary><c>false</c> si el ambiente no tiene Teams configurado: el canal no se ofrece ni se intenta.</summary>
    bool Habilitado { get; }

    /// <summary>Aviso directo a una persona interna (<c>plataforma_persona</c>). Lanza si Teams no lo acepta.</summary>
    Task AvisarPersonaAsync(Guid personaId, AvisoTeams aviso, CancellationToken ct = default);

    /// <summary>Aviso en el canal de soporte configurado en Teams. Lanza si Teams no lo acepta.</summary>
    Task AvisarCanalAsync(AvisoTeams aviso, CancellationToken ct = default);
}

/// <summary>Sin integración con Teams (hasta la HU 2.10, o en local): no envía nada.</summary>
public sealed partial class TeamsDesactivado(ILogger<TeamsDesactivado> log) : INotificadorTeams
{
    public bool Habilitado => false;

    public Task AvisarPersonaAsync(Guid personaId, AvisoTeams aviso, CancellationToken ct = default)
    {
        NoEnviado(log);
        return Task.CompletedTask;
    }

    public Task AvisarCanalAsync(AvisoTeams aviso, CancellationToken ct = default)
    {
        NoEnviado(log);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Aviso de Teams no enviado: el ambiente no tiene la integración con Teams")]
    private static partial void NoEnviado(ILogger log);
}
