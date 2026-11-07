using System.Text.RegularExpressions;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>Un archivo de documentación separado en sus campos y su cuerpo.</summary>
internal sealed record ArchivoDocumento(IReadOnlyDictionary<string, IReadOnlyList<string>> Campos, string Cuerpo)
{
    public string? Texto(string clave) => Campos.TryGetValue(clave, out var v) && v.Count == 1 ? v[0] : null;

    public IReadOnlyList<string>? Lista(string clave) => Campos.GetValueOrDefault(clave);
}

/// <summary>
/// Lector del front-matter (ATLAS-ADR-0053 §4). Solo el subconjunto plano que usan los documentos: <c>clave: valor</c>,
/// listas <c>[a, b]</c> o con guiones y comentarios. Sin anclas, etiquetas ni mapas anidados: lo demás es un error, así un
/// archivo nunca se interpreta distinto de lo que parece.
/// </summary>
internal static partial class FrontMatter
{
    /// <summary>Campos obligatorios (prompt maestro §8.5 y plan 03 §18.2).</summary>
    public static readonly IReadOnlyList<string> Obligatorios = ["aplicativo", "tipo", "titulo", "visibilidad", "audiencias", "responsable"];

    /// <summary>Lee un archivo ya normalizado (saltos de línea <c>\n</c>).</summary>
    public static (ArchivoDocumento? Archivo, IReadOnlyList<string> Errores) Leer(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var lineas = texto.Split('\n');
        if (lineas[0].TrimEnd() != "---")
        {
            return (null, ["El archivo debe empezar con el front-matter («---»)."]);
        }
        var fin = Array.FindIndex(lineas, 1, l => l.TrimEnd() == "---");
        if (fin < 0)
        {
            return (null, ["El front-matter no se cierra («---»)."]);
        }

        var errores = new List<string>();
        var campos = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        string? listaAbierta = null;
        List<string>? lista = null;
        for (var i = 1; i < fin; i++)
        {
            var linea = lineas[i];
            if (string.IsNullOrWhiteSpace(linea) || linea.TrimStart().StartsWith('#'))
            {
                continue;
            }
            var guion = Guion().Match(linea);
            if (guion.Success && lista is not null)
            {
                lista.Add(Escalar(guion.Groups["valor"].Value));
                continue;
            }
            var par = Par().Match(linea);
            if (!par.Success)
            {
                errores.Add($"Front-matter, línea {i + 1}: solo se admite «clave: valor» y listas.");
                continue;
            }
            var clave = par.Groups["clave"].Value;
            var valor = SinComentario(par.Groups["valor"].Value).Trim();
            if (campos.ContainsKey(clave))
            {
                errores.Add($"Front-matter, línea {i + 1}: «{clave}» está repetida.");
                continue;
            }
            listaAbierta = null;
            lista = null;
            if (valor.Length == 0)
            {
                listaAbierta = clave;
                lista = [];
                campos[listaAbierta] = lista;
            }
            else if (valor.StartsWith('[') && valor.EndsWith(']'))
            {
                campos[clave] = [.. valor[1..^1].Split(',').Select(Escalar).Where(v => v.Length > 0)];
            }
            else if (valor[0] is '[' or '{' or '&' or '*' or '!' or '|' or '>')
            {
                errores.Add($"Front-matter, línea {i + 1}: «{clave}» usa una forma de YAML que no se admite.");
            }
            else
            {
                campos[clave] = [Escalar(valor)];
            }
        }

        foreach (var faltante in Obligatorios.Where(o => !campos.ContainsKey(o)))
        {
            errores.Add($"Front-matter: falta «{faltante}».");
        }
        if (errores.Count > 0)
        {
            return (null, errores);
        }
        return (new ArchivoDocumento(campos, string.Join('\n', lineas.Skip(fin + 1)).TrimStart('\n')), []);
    }

    private static string Escalar(string texto)
    {
        var t = SinComentario(texto).Trim();
        return t.Length >= 2 && t[0] is '"' or '\'' && t[^1] == t[0] ? t[1..^1] : t;
    }

    /// <summary>Quita un comentario « #…» que esté fuera de comillas.</summary>
    private static string SinComentario(string texto)
    {
        char? comilla = null;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (comilla is not null)
            {
                comilla = c == comilla ? null : comilla;
            }
            else if (c is '"' or '\'' && (i == 0 || char.IsWhiteSpace(texto[i - 1]) || texto[i - 1] is '[' or ','))
            {
                comilla = c;
            }
            else if (c == '#' && i > 0 && char.IsWhiteSpace(texto[i - 1]))
            {
                return texto[..i];
            }
        }
        return texto;
    }

    [GeneratedRegex(@"^(?<clave>[A-Za-z_][A-Za-z0-9_\-]*):(\s+(?<valor>.*))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Par();

    [GeneratedRegex(@"^\s+-\s+(?<valor>.*)$|^-\s+(?<valor>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Guion();
}
