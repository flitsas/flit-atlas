namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>
/// Servicios de push a los que Atlas acepta enviar (sección <c>Plataforma:Push:HostsPermitidos</c>, separados por
/// coma). El endpoint de una suscripción lo pone el navegador, pero llega del cliente: sin esta lista, una persona
/// podría hacer que atlas-api enviara peticiones a una dirección interna.
/// </summary>
public sealed class OpcionesPush
{
    public const string ClaveHostsPermitidos = "Plataforma:Push:HostsPermitidos";

    /// <summary>FCM (Chrome, Edge y Android), Mozilla, Apple (Safari e iOS) y WNS (Edge en Windows).</summary>
    public static readonly string[] HostsPorDefecto =
    [
        "fcm.googleapis.com",
        "android.googleapis.com",
        "updates.push.services.mozilla.com",
        "*.push.services.mozilla.com",
        "web.push.apple.com",
        "*.push.apple.com",
        "*.notify.windows.com",
    ];

    public IReadOnlyList<string> HostsPermitidos { get; init; } = HostsPorDefecto;

    public bool HostPermitido(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return HostsPermitidos.Any(permitido => permitido.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(permitido[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(host, permitido, StringComparison.OrdinalIgnoreCase));
    }
}
