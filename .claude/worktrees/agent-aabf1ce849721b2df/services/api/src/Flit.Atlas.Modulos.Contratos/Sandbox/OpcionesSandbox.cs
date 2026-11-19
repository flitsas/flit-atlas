namespace Flit.Atlas.Modulos.Contratos.Sandbox;

/// <summary>
/// Sandbox nivel 1 con Microcks (HU 4.7, ATLAS-ADR-0045). Sección <c>Contratos:Sandbox</c>. Sin <see cref="UrlMicrocks"/>
/// el sandbox no está disponible: el portal lo muestra deshabilitado y la API responde 503. Las credenciales de Microcks
/// (si su API las pide) llegan por configuración del ambiente (SealedSecret en flit-gitops), nunca desde el repo.
/// </summary>
public sealed class OpcionesSandbox
{
    public const string Seccion = "Contratos:Sandbox";

    /// <summary>URL interna de Microcks (red del clúster o la de Aspire). Microcks nunca se expone: solo lo llama atlas-api.</summary>
    public string? UrlMicrocks { get; set; }

    /// <summary>
    /// Raíz pública con la que el cliente llama al sandbox (el host del ingress que llega a atlas-api, p. ej. el host propio
    /// del sandbox en qa). Vacía = <c>Atlas:UrlPublica</c> y, si tampoco está, la de la petición.
    /// </summary>
    public string? UrlPublica { get; set; }

    /// <summary>
    /// Microcks con Keycloak (su modo normal en Kubernetes): token por <c>client_credentials</c> de su cuenta de servicio.
    /// Vacía = sin autenticación (la imagen «uber» de desarrollo y de la 177 no la pide).
    /// </summary>
    public string? UrlToken { get; set; }

    public string? ClienteId { get; set; }

    public string? ClienteSecreto { get; set; }

    /// <summary>Cada cuánto revisa los sandboxes: vencidos, revocados, revisión nueva, servicios que faltan en Microcks y borrados pendientes.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Apaga solo el trabajo periódico (las pruebas lo llaman a mano).</summary>
    public bool TrabajoHabilitado { get; set; } = true;

    /// <summary>Tope del cuerpo que el proxy reenvía a Microcks, en KiB.</summary>
    public int CuerpoMaximoKiB { get; set; } = 1024;

    /// <summary>Tiempo de espera de cada llamada a Microcks.</summary>
    public TimeSpan TiempoEspera { get; set; } = TimeSpan.FromSeconds(30);

    public bool Disponible => Uri.TryCreate(UrlMicrocks?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public Uri? BaseMicrocks => Disponible ? new Uri(UrlMicrocks!.Trim().TrimEnd('/') + "/") : null;
}
