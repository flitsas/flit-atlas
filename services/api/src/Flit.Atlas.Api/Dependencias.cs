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

    public static readonly string[] ClavesRequeridas =
    [
        $"ConnectionStrings:{BaseDeDatos}",
        $"ConnectionStrings:{Redis}",
        $"ConnectionStrings:{RabbitMq}",
        Ambiente,
    ];
}
