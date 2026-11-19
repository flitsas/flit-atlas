using System.Globalization;
using System.Text.RegularExpressions;

namespace Flit.Atlas.Modulos.Contratos.Specs;

/// <summary>
/// Versión semántica (SemVer 2.0) sin metadatos de compilación (<c>+...</c>): la versión va en las rutas y dos versiones
/// que solo difieren en el <c>+</c> son la misma para SemVer, lo que rompería la inmutabilidad.
/// </summary>
public sealed partial record VersionSemantica(int Mayor, int Menor, int Parche, string? Prerelease)
{
    public bool EsEstable => Prerelease is null;

    /// <summary><c>X.Y.Z</c>, sin prerelease.</summary>
    public string Nucleo => string.Create(CultureInfo.InvariantCulture, $"{Mayor}.{Menor}.{Parche}");

    public override string ToString() => Prerelease is null ? Nucleo : $"{Nucleo}-{Prerelease}";

    public static VersionSemantica? Leer(string? texto)
    {
        if (texto is null || texto.Length > 100)
        {
            return null;
        }
        var m = Formato().Match(texto.Trim());
        if (!m.Success
            || !int.TryParse(m.Groups["mayor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var mayor)
            || !int.TryParse(m.Groups["menor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var menor)
            || !int.TryParse(m.Groups["parche"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parche))
        {
            return null;
        }
        return new VersionSemantica(mayor, menor, parche, m.Groups["pre"].Success ? m.Groups["pre"].Value : null);
    }

    // La gramática de semver.org, sin el bloque de compilación y con números de hasta 9 cifras (caben en int).
    [GeneratedRegex(@"^(?<mayor>0|[1-9]\d{0,8})\.(?<menor>0|[1-9]\d{0,8})\.(?<parche>0|[1-9]\d{0,8})(?:-(?<pre>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?$")]
    private static partial Regex Formato();
}
