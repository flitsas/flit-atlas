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

    /// <summary>
    /// Precedencia de SemVer 2.0 (§11): núcleo numérico; una prerelease va antes que su estable; las prereleases se comparan
    /// identificador por identificador (numéricos como números y antes que los alfanuméricos; más identificadores, mayor).
    /// </summary>
    public static int Comparar(VersionSemantica a, VersionSemantica b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var nucleo = (a.Mayor, a.Menor, a.Parche).CompareTo((b.Mayor, b.Menor, b.Parche));
        if (nucleo != 0)
        {
            return nucleo;
        }
        if (a.Prerelease is null || b.Prerelease is null)
        {
            return (a.Prerelease is null ? 1 : 0) - (b.Prerelease is null ? 1 : 0);
        }
        var x = a.Prerelease.Split('.');
        var y = b.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var numX = long.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out var nx);
            var numY = long.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ny);
            var orden = (numX, numY) switch
            {
                (true, true) => nx.CompareTo(ny),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(x[i], y[i]),
            };
            if (orden != 0)
            {
                return Math.Sign(orden);
            }
        }
        return x.Length.CompareTo(y.Length);
    }

    // La gramática de semver.org, sin el bloque de compilación y con números de hasta 9 cifras (caben en int).
    [GeneratedRegex(@"^(?<mayor>0|[1-9]\d{0,8})\.(?<menor>0|[1-9]\d{0,8})\.(?<parche>0|[1-9]\d{0,8})(?:-(?<pre>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?$")]
    private static partial Regex Formato();
}
