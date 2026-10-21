using Microsoft.Extensions.Configuration;

namespace Flit.Atlas.ServiceDefaults;

/// <summary>
/// Falla el arranque si falta configuración obligatoria, nombrando todas las claves que faltan a la vez.
/// </summary>
public static class ConfiguracionRequerida
{
    /// <param name="configuracion">Configuración del host.</param>
    /// <param name="claves">Claves con la sintaxis de <see cref="IConfiguration"/> (<c>ConnectionStrings:atlas</c>).</param>
    /// <exception cref="ConfiguracionFaltanteException">Si alguna clave no existe o está vacía.</exception>
    public static void Validar(IConfiguration configuracion, params IReadOnlyCollection<string> claves)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var faltantes = claves.Where(c => string.IsNullOrWhiteSpace(configuracion[c])).ToList();
        if (faltantes.Count > 0)
        {
            throw new ConfiguracionFaltanteException(faltantes);
        }
    }

    /// <summary>Nombre de la variable de entorno equivalente: <c>ConnectionStrings:atlas</c> → <c>ConnectionStrings__atlas</c>.</summary>
    public static string ComoVariableDeEntorno(string clave) => clave.Replace(":", "__", StringComparison.Ordinal);
}

public sealed class ConfiguracionFaltanteException(IReadOnlyList<string> claves)
    : InvalidOperationException(
        "Faltan variables de configuración obligatorias: "
        + string.Join(", ", claves.Select(ConfiguracionRequerida.ComoVariableDeEntorno))
        + ". Defínelas como variables de entorno o en appsettings.")
{
    public IReadOnlyList<string> Claves { get; } = claves;
}
