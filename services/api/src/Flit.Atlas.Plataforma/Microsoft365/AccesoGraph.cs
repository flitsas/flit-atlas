namespace Flit.Atlas.Plataforma.Microsoft365;

/// <summary>
/// Token de aplicación de Microsoft Graph para los módulos (ATLAS-ADR-0033). Un módulo no referencia la identidad
/// (ATLAS-ADR-0016): la identidad registra la implementación con la credencial de Entra (la misma de
/// <c>PerfilGraph</c>) y los módulos piden aquí la cabecera <c>Authorization</c>. Sin Entra configurado (local y pruebas
/// con el proveedor simulado), <see cref="Disponible"/> es <c>false</c> y nada llama a Graph.
/// </summary>
public interface IAccesoGraph
{
    /// <summary>Hay credencial de Entra para pedir tokens de aplicación.</summary>
    bool Disponible { get; }

    /// <summary>
    /// Cabecera <c>Authorization</c> (<c>Bearer …</c>) con el token de aplicación de Graph (<c>.default</c>). El token nunca
    /// se guarda en la base ni sale a los logs.
    /// </summary>
    Task<string> CabeceraAsync(CancellationToken ct = default);
}

/// <summary>Cliente HTTP de Graph que usan los módulos (<c>IHttpClientFactory</c>), con la base de Graph.</summary>
public static class ClienteGraph
{
    public const string Nombre = "atlas-graph";

    public static readonly Uri Base = new("https://graph.microsoft.com/");

    public const string ScopeAplicacion = "https://graph.microsoft.com/.default";
}

/// <summary>Sin Entra no hay Graph.</summary>
internal sealed class SinAccesoGraph : IAccesoGraph
{
    public bool Disponible => false;

    public Task<string> CabeceraAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("Este ambiente no tiene Entra ID configurado: no hay token de Graph.");
}
