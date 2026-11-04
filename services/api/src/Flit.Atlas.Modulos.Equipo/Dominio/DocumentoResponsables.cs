using System.Globalization;
using System.Text.RegularExpressions;

namespace Flit.Atlas.Modulos.Equipo.Dominio;

/// <summary>Fila de la tabla 2 del documento: la persona y sus listas, en el orden del documento.</summary>
public sealed record FilaPersona(string Persona, IReadOnlyList<string> PrincipalDe, IReadOnlyList<string> SecundarioDe);

/// <summary>Fila de las tablas 3.1 y 3.2: el aplicativo con su principal y su secundario.</summary>
public sealed record FilaAplicativo(string Aplicativo, string? Principal, string? Secundario);

/// <summary>Lo que se leyó del documento de responsables (Markdown), sin enlazar todavía con el catálogo ni las personas.</summary>
/// <param name="DedicacionSecundario">El porcentaje que dice el §1 para el secundario («~20 %»).</param>
public sealed record DocumentoLeido(
    int? Version, DateOnly? Fecha, string? DefinidoPor, string? DescripcionVersion,
    IReadOnlyList<DefinicionRol> Definiciones, int? DedicacionSecundario,
    IReadOnlyList<FilaPersona> PorPersona, IReadOnlyList<FilaAplicativo> Productos, IReadOnlyList<FilaAplicativo> Modulos,
    DateOnly? ProximaRevision, IReadOnlyList<string> Errores);

/// <summary>
/// Lector del documento «Responsables por producto y por módulo» (HU 2.3, ATLAS-ADR-0031) con la estructura de la versión
/// 5: encabezado con fecha, «Definió» y versión; §1 definiciones de cada rol; §2 tabla por persona; §3.1 productos; §3.2
/// módulos; §4 con la próxima revisión. No interpreta más de lo que el documento dice: lo que falta queda en
/// <see cref="DocumentoLeido.Errores"/>.
/// </summary>
public static partial class DocumentoResponsables
{
    /// <summary>Tamaño máximo del Markdown que se acepta (el documento real pesa unos 4 KB).</summary>
    public const int TamanoMaximo = 100_000;

    private enum Seccion
    {
        Ninguna,
        Definiciones,
        PorPersona,
        Productos,
        Modulos,
        Otra,
    }

    public static DocumentoLeido Leer(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var errores = new List<string>();
        var texto = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);

        var v = Version().Match(texto);
        var version = v.Success ? int.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
        var descripcion = v.Success ? Limpio(v.Groups[2].Value) : null;
        var fecha = FechaDoc().Match(texto) is { Success: true } f ? LeerFecha(f.Groups[1].Value) : null;
        var definidoPor = Definio().Match(texto) is { Success: true } d ? Limpio(d.Groups[1].Value) : null;
        var proxima = Proxima().Match(texto) is { Success: true } p ? LeerFecha(p.Groups[1].Value) : null;

        var definiciones = new List<(string Titulo, List<string> Puntos)>();
        var porPersona = new List<FilaPersona>();
        var productos = new List<FilaAplicativo>();
        var modulos = new List<FilaAplicativo>();
        var seccion = Seccion.Ninguna;
        var encabezadoTabla = false;

        foreach (var crudo in texto.Split('\n'))
        {
            var linea = crudo.TrimEnd();
            if (linea.StartsWith('#'))
            {
                seccion = SeccionDe(linea);
                encabezadoTabla = false;
                continue;
            }
            if (seccion == Seccion.Definiciones)
            {
                if (TituloNegrita().Match(linea) is { Success: true } t)
                {
                    definiciones.Add((t.Groups[1].Value.Trim(), []));
                }
                else if (linea.StartsWith("- ", StringComparison.Ordinal) && definiciones.Count > 0)
                {
                    definiciones[^1].Puntos.Add(linea[2..].Trim());
                }
                continue;
            }
            if (!linea.TrimStart().StartsWith('|'))
            {
                encabezadoTabla = false;
                continue;
            }
            var celdas = Celdas(linea);
            if (!encabezadoTabla)
            {
                // La primera fila de cada tabla es el encabezado; la segunda, el separador.
                encabezadoTabla = true;
                continue;
            }
            if (celdas.All(c => c.Trim('-', ':', ' ').Length == 0))
            {
                continue;
            }
            switch (seccion)
            {
                case Seccion.PorPersona when celdas.Count >= 3:
                    porPersona.Add(new FilaPersona(SinNegrita(celdas[0]), Lista(celdas[1]), Lista(celdas[2])));
                    break;
                case Seccion.Productos when celdas.Count >= 3:
                    productos.Add(new FilaAplicativo(SinNegrita(celdas[0]), Persona(celdas[1]), Persona(celdas[2])));
                    break;
                case Seccion.Modulos when celdas.Count >= 3:
                    modulos.Add(new FilaAplicativo(SinNegrita(celdas[0]), Persona(celdas[1]), Persona(celdas[2])));
                    break;
                default:
                    break;
            }
        }

        var roles = new List<DefinicionRol>();
        foreach (var (titulo, puntos) in definiciones)
        {
            var normal = Textos.Normalizar(titulo);
            var rol = normal.Contains("principal", StringComparison.Ordinal) ? "principal"
                : normal.Contains("secundario", StringComparison.Ordinal) ? "secundario" : null;
            if (rol is not null && roles.All(r => r.Rol != rol))
            {
                roles.Add(new DefinicionRol(rol, titulo, puntos));
            }
        }
        var dedicacion = roles.FirstOrDefault(r => r.Rol == "secundario")?.Puntos
            .Select(punto => Porcentaje().Match(punto)).FirstOrDefault(m => m.Success) is { } porcentaje
            ? int.Parse(porcentaje.Groups[1].Value, CultureInfo.InvariantCulture)
            : (int?)null;

        if (version is null)
        {
            errores.Add("No se encontró la versión del documento («**Versión N:**»).");
        }
        if (fecha is null)
        {
            errores.Add("No se encontró la fecha del documento («**Fecha:** aaaa-mm-dd»).");
        }
        if (roles.Count < 2)
        {
            errores.Add("No se encontraron las definiciones del responsable principal y del secundario (§1).");
        }
        if (productos.Count == 0 && modulos.Count == 0)
        {
            errores.Add("No se encontraron las tablas por producto (§3.1) ni por módulo (§3.2).");
        }
        if (dedicacion is not (null or (>= 1 and <= 100)))
        {
            errores.Add("La dedicación del secundario debe estar entre 1 y 100 %.");
            dedicacion = null;
        }

        return new DocumentoLeido(version, fecha, definidoPor, descripcion, roles, dedicacion, porPersona, productos, modulos, proxima, errores);
    }

    private static Seccion SeccionDe(string titulo)
    {
        var normal = Textos.Normalizar(titulo.TrimStart('#'));
        return normal switch
        {
            _ when normal.StartsWith("1.", StringComparison.Ordinal) => Seccion.Definiciones,
            _ when normal.StartsWith("2.", StringComparison.Ordinal) => Seccion.PorPersona,
            _ when normal.StartsWith("3.1", StringComparison.Ordinal) => Seccion.Productos,
            _ when normal.StartsWith("3.2", StringComparison.Ordinal) => Seccion.Modulos,
            _ => Seccion.Otra,
        };
    }

    private static List<string> Celdas(string linea)
    {
        var partes = linea.Trim().Split('|');
        // «| a | b |» deja un vacío al principio y otro al final.
        return [.. partes.Skip(1).Take(Math.Max(0, partes.Length - 2)).Select(c => c.Trim())];
    }

    private static string SinNegrita(string texto) => texto.Replace("**", string.Empty, StringComparison.Ordinal).Trim();

    private static string? Persona(string celda)
    {
        var limpio = SinNegrita(celda);
        return limpio.Length == 0 || limpio is "—" or "-" ? null : limpio;
    }

    private static List<string> Lista(string celda) =>
        [.. SinNegrita(celda).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(n => n is not ("—" or "-"))];

    private static string? Limpio(string texto)
    {
        var limpio = texto.Replace("**", string.Empty, StringComparison.Ordinal).Trim().TrimEnd('·').Trim();
        return limpio.Length == 0 ? null : limpio;
    }

    private static DateOnly? LeerFecha(string texto) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;

    [GeneratedRegex(@"\*\*Versi[oó]n\s+(\d{1,4}):?\*\*:?\s*([^\n·]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Version();

    [GeneratedRegex(@"\*\*Fecha:?\*\*:?\s*(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex FechaDoc();

    [GeneratedRegex(@"\*\*Defini[oó]:?\*\*:?\s*([^\n·]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Definio();

    [GeneratedRegex(@"pr[oó]xima:?\s*(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex Proxima();

    [GeneratedRegex(@"^\*\*([^*]+)\*\*$")]
    private static partial Regex TituloNegrita();

    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex Porcentaje();
}
