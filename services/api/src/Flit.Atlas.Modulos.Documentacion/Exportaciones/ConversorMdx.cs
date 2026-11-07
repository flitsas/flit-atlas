using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Flit.Atlas.Modulos.Documentacion.Exportaciones;

/// <summary>Un componente vivo encontrado en el MDX, con sus atributos (cadenas literales).</summary>
internal sealed record ComponenteVivo(string Nombre, IReadOnlyDictionary<string, string> Atributos);

/// <summary>
/// Convierte el MDX de una versión en Markdown estático para exportarlo (ATLAS-ADR-0053 §7): sin evaluar nada, recorre el
/// texto, deja intactos los bloques y el código en línea y cambia las etiquetas de la lista blanca. <c>Contrato</c>,
/// <c>Diagrama</c>, <c>Responsables</c> y <c>SoporteDeTurno</c> se reemplazan por el texto que dé <c>resolver</c>;
/// <c>Alerta</c> deja su título en negrilla y su contenido; <c>Pestanas</c> deja su contenido, con los títulos que abren
/// cada pestaña. Lo que no reconoce queda como texto: atlas-docs escapa el HTML al generar.
/// </summary>
internal static partial class ConversorMdx
{
    private static readonly Dictionary<string, string> TitulosAlerta = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["info"] = "Nota",
        ["aviso"] = "Atención",
        ["peligro"] = "Cuidado",
        ["exito"] = "Bien",
    };

    public static async Task<string> ConvertirAsync(string mdx, Func<ComponenteVivo, Task<string>> resolver)
    {
        ArgumentNullException.ThrowIfNull(mdx);
        ArgumentNullException.ThrowIfNull(resolver);
        var salida = new StringBuilder(mdx.Length);
        var bloque = new StringBuilder();
        char? cerca = null;
        var largoCerca = 0;

        async Task VaciarAsync()
        {
            if (bloque.Length > 0)
            {
                await EscribirAsync(bloque.ToString(), salida, resolver).ConfigureAwait(false);
                bloque.Clear();
            }
        }

        foreach (var linea in mdx.Split('\n'))
        {
            var marca = Cerca().Match(linea);
            if (cerca is not null)
            {
                salida.Append(linea).Append('\n');
                if (marca.Success && marca.Groups["marca"].Value[0] == cerca && marca.Groups["marca"].Length >= largoCerca
                    && string.IsNullOrWhiteSpace(marca.Groups["resto"].Value))
                {
                    cerca = null;
                }
                continue;
            }
            if (marca.Success)
            {
                await VaciarAsync().ConfigureAwait(false);
                cerca = marca.Groups["marca"].Value[0];
                largoCerca = marca.Groups["marca"].Length;
                salida.Append(linea).Append('\n');
                continue;
            }
            bloque.Append(linea).Append('\n');
        }
        await VaciarAsync().ConfigureAwait(false);
        return salida.ToString().TrimEnd('\n') + "\n";
    }

    private static async Task EscribirAsync(string texto, StringBuilder salida, Func<ComponenteVivo, Task<string>> resolver)
    {
        var i = 0;
        while (i < texto.Length)
        {
            var c = texto[i];
            if (c == '\\' && i + 1 < texto.Length)
            {
                salida.Append(c).Append(texto[i + 1]);
                i += 2;
            }
            else if (c == '`')
            {
                var fin = FinDeCodigo(texto, i);
                salida.Append(texto, i, fin - i);
                i = fin;
            }
            else if (c == '<' && Etiqueta().Match(texto, i) is { Success: true } etiqueta && etiqueta.Index == i)
            {
                salida.Append(await ReemplazoAsync(etiqueta, resolver).ConfigureAwait(false));
                i += etiqueta.Length;
            }
            else
            {
                salida.Append(c);
                i++;
            }
        }
    }

    private static async Task<string> ReemplazoAsync(Match etiqueta, Func<ComponenteVivo, Task<string>> resolver)
    {
        var nombre = etiqueta.Groups["nombre"].Value;
        var cierre = etiqueta.Groups["cierre"].Success;
        if (nombre == "Pestanas")
        {
            return string.Empty;
        }
        if (nombre == "Alerta")
        {
            if (cierre)
            {
                return string.Empty;
            }
            var atributos = Atributos(etiqueta.Groups["atributos"].Value);
            var titulo = atributos.GetValueOrDefault("titulo")
                ?? TitulosAlerta.GetValueOrDefault(atributos.GetValueOrDefault("tipo") ?? "info")
                ?? TitulosAlerta["info"];
            return string.Create(CultureInfo.InvariantCulture, $"**{Escapar(titulo)}**\n");
        }
        return cierre ? string.Empty : await resolver(new ComponenteVivo(nombre, Atributos(etiqueta.Groups["atributos"].Value))).ConfigureAwait(false);
    }

    private static Dictionary<string, string> Atributos(string texto)
    {
        var atributos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match a in Atributo().Matches(texto))
        {
            atributos[a.Groups["nombre"].Value] = a.Groups["doble"].Success ? a.Groups["doble"].Value : a.Groups["simple"].Value;
        }
        return atributos;
    }

    /// <summary>Fin del código en línea; sin cierre en la misma línea, el acento grave es texto, como en <see cref="ValidadorMdx"/>.</summary>
    private static int FinDeCodigo(string texto, int inicio)
    {
        var fin = inicio;
        while (fin < texto.Length && texto[fin] == '`')
        {
            fin++;
        }
        var largo = fin - inicio;
        var j = fin;
        while (j < texto.Length && texto[j] != '\n')
        {
            if (texto[j] != '`')
            {
                j++;
                continue;
            }
            var k = j;
            while (k < texto.Length && texto[k] == '`')
            {
                k++;
            }
            if (k - j == largo)
            {
                return k;
            }
            j = k;
        }
        return fin;
    }

    /// <summary>Escapa un valor (un nombre, un título) para que no se lea como Markdown.</summary>
    public static string Escapar(string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        var salida = new StringBuilder(valor.Length);
        foreach (var c in valor)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '|')
            {
                salida.Append('\\');
            }
            salida.Append(c is '\n' or '\r' ? ' ' : c);
        }
        return salida.ToString();
    }

    [GeneratedRegex(@"^ {0,3}(?<marca>`{3,}|~{3,})(?<resto>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Cerca();

    [GeneratedRegex(@"\G<(?<cierre>/)?(?<nombre>Contrato|Diagrama|Responsables|SoporteDeTurno|Alerta|Pestanas)(?<atributos>(?:\s+[A-Za-z0-9_-]+=(?:""[^""]*""|'[^']*'))*)\s*/?>", RegexOptions.CultureInvariant)]
    private static partial Regex Etiqueta();

    [GeneratedRegex(@"(?<nombre>[A-Za-z0-9_-]+)=(?:""(?<doble>[^""]*)""|'(?<simple>[^']*)')", RegexOptions.CultureInvariant)]
    private static partial Regex Atributo();
}
