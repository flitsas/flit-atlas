namespace Flit.Atlas.Api;

/// <summary>
/// Nombres de las conexiones de atlas-api. Coinciden con los recursos del AppHost de Aspire
/// y con las variables <c>ConnectionStrings__*</c> de los manifiestos de flit-gitops.
/// </summary>
internal static class Dependencias
{
    public const string BaseDeDatos = "atlas";
    public const string Redis = "redis";
    public const string RabbitMq = "rabbitmq";

    /// <summary><c>local</c>, <c>dev</c>, <c>qa</c> o <c>pdn</c>: decide qué se permite (p. ej. el proveedor simulado).</summary>
    public const string Ambiente = "Atlas:Ambiente";

    public static readonly string[] AmbientesValidos = ["local", "dev", "qa", "pdn"];

    /// <summary>Redes (CIDR, separadas por coma) de los proxies cuyo <c>X-Forwarded-*</c> se acepta.</summary>
    public const string ProxyRedesConfiables = "Atlas:Proxy:RedesConfiables";

    /// <summary>Cuántos proxies hay delante de la API (por defecto 2: nginx del host y atlas-web).</summary>
    public const string ProxyLimite = "Atlas:Proxy:Limite";

    /// <summary>Ambientes desplegados detrás de los proxies del clúster: sin redes confiables, atlas-api no arranca.</summary>
    public static readonly string[] AmbientesConProxy = ["qa", "pdn"];

    /// <summary>
    /// Falla el arranque en qa y pdn si falta <see cref="ProxyRedesConfiables"/> (F4-SEC-13, ATLAS-ADR-0049): sin ella no se
    /// acepta el <c>X-Forwarded-For</c> de nginx ni de atlas-web, todas las peticiones parecen venir de la IP del proxy y los
    /// límites por IP de la identidad se vuelven uno solo para todo el mundo (y la auditoría guarda la IP del pod).
    /// </summary>
    public static void ExigirRedesConfiables(IConfiguration configuracion, string ambiente)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        if (AmbientesConProxy.Contains(ambiente) && string.IsNullOrWhiteSpace(configuracion[ProxyRedesConfiables]))
        {
            throw new InvalidOperationException(
                $"Falta {ServiceDefaults.ConfiguracionRequerida.ComoVariableDeEntorno(ProxyRedesConfiables)} en {ambiente}: son las redes (CIDR, " +
                "separadas por coma) de los proxies que ponen X-Forwarded-For (nginx del host y atlas-web). Sin ella todas las " +
                "peticiones parecen venir del proxy y los límites por IP serían uno solo para todos. Defínela en el ConfigMap de " +
                "flit-gitops (deploy/runbook-atlas.md).");
        }
    }

    public static IReadOnlyList<System.Net.IPNetwork> RedesConfiables(IConfiguration configuracion)
    {
        var valor = configuracion[ProxyRedesConfiables];
        if (string.IsNullOrWhiteSpace(valor))
        {
            return [];
        }
        var redes = new List<System.Net.IPNetwork>();
        foreach (var red in valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!System.Net.IPNetwork.TryParse(red, out var parseada))
            {
                throw new InvalidOperationException($"Atlas__Proxy__RedesConfiables: «{red}» no es una red CIDR válida.");
            }
            redes.Add(parseada);
        }
        return redes;
    }

    public static readonly string[] ClavesRequeridas =
    [
        $"ConnectionStrings:{BaseDeDatos}",
        $"ConnectionStrings:{Redis}",
        $"ConnectionStrings:{RabbitMq}",
        Ambiente,
    ];
}
