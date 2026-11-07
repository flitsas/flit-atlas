using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>
/// Validador conservador de MDX en el servidor (ATLAS-ADR-0053 §5). Nunca compila ni evalúa: recorre el texto y, ante la
/// duda, rechaza. Fuera de los bloques y del código en línea no admite <c>import</c> ni <c>export</c> de nivel superior,
/// expresiones entre llaves, HTML, etiquetas fuera de la lista blanca, atributos que no sean cadenas literales ni enlaces
/// con otro esquema que <c>https</c> o <c>mailto</c>. La PWA aplica sus propias reglas al pintar: no confía en este.
/// </summary>
internal static partial class ValidadorMdx
{
    public static readonly FrozenSet<string> Componentes =
        new[] { "Contrato", "Diagrama", "Responsables", "SoporteDeTurno", "Alerta", "Pestanas" }.ToFrozenSet(StringComparer.Ordinal);

    private const int MaximoErrores = 20;

    /// <summary>Los problemas encontrados, con su línea; vacío si el texto es válido.</summary>
    public static IReadOnlyList<string> Validar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var errores = new List<string>();
        var etiquetas = new Stack<(string Nombre, int Linea)>();
        var lineas = texto.Split('\n');
        var bloque = new List<string>();
        var inicioBloque = 0;
        char? cerca = null;
        var largoCerca = 0;

        void CerrarBloque()
        {
            if (bloque.Count > 0)
            {
                Revisar(string.Join('\n', bloque), inicioBloque, etiquetas, errores);
                bloque.Clear();
            }
        }

        for (var i = 0; i < lineas.Length; i++)
        {
            var linea = lineas[i];
            var marca = Cerca().Match(linea);
            if (cerca is not null)
            {
                if (marca.Success && marca.Groups["marca"].Value[0] == cerca && marca.Groups["marca"].Length >= largoCerca
                    && string.IsNullOrWhiteSpace(marca.Groups["resto"].Value))
                {
                    cerca = null;
                }
                continue;
            }
            if (marca.Success)
            {
                CerrarBloque();
                cerca = marca.Groups["marca"].Value[0];
                largoCerca = marca.Groups["marca"].Length;
                continue;
            }
            if (Modulo().IsMatch(linea))
            {
                errores.Add($"Línea {i + 1}: «import» y «export» no se admiten.");
            }
            var definicion = DefinicionEnlace().Match(linea);
            if (definicion.Success && !DestinoValido(definicion.Groups["destino"].Value))
            {
                errores.Add($"Línea {i + 1}: el enlace solo admite https, mailto o una ruta interna.");
            }
            if (bloque.Count == 0)
            {
                inicioBloque = i + 1;
            }
            bloque.Add(linea);
        }
        CerrarBloque();

        foreach (var (nombre, numero) in etiquetas)
        {
            errores.Add($"Línea {numero}: falta cerrar <{nombre}>.");
        }
        return errores.Count > MaximoErrores ? [.. errores.Take(MaximoErrores), $"…y {errores.Count - MaximoErrores} más."] : errores;
    }

    private static void Revisar(string texto, int primeraLinea, Stack<(string Nombre, int Linea)> etiquetas, List<string> errores)
    {
        var i = 0;
        int Linea(int posicion) => primeraLinea + texto.AsSpan(0, posicion).Count('\n');

        while (i < texto.Length)
        {
            var c = texto[i];
            switch (c)
            {
                case '\\':
                    i += 2;
                    break;
                case '`':
                    i = SaltarCodigo(texto, i);
                    break;
                case '{' or '}':
                    errores.Add($"Línea {Linea(i)}: las expresiones entre llaves no se admiten; escríbelas como \\{c} o en código.");
                    i++;
                    break;
                case '<':
                    i = RevisarEtiqueta(texto, i, Linea, etiquetas, errores);
                    break;
                case ']' when i + 1 < texto.Length && texto[i + 1] == '(':
                    i = RevisarEnlace(texto, i + 2, Linea, errores);
                    break;
                default:
                    i++;
                    break;
            }
        }
    }

    /// <summary>
    /// Salta un código en línea; sin su cierre, el acento grave es texto normal y lo demás se sigue revisando. El cierre debe
    /// estar en la misma línea: un párrafo o un título cortan el código en Markdown, y saltar hasta el siguiente acento grave
    /// dejaría pasar lo que hay en medio.
    /// </summary>
    private static int SaltarCodigo(string texto, int inicio)
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

    private static int RevisarEtiqueta(string texto, int inicio, Func<int, int> linea, Stack<(string Nombre, int Linea)> etiquetas, List<string> errores)
    {
        var i = inicio + 1;
        var cierre = i < texto.Length && texto[i] == '/';
        if (cierre)
        {
            i++;
        }
        var inicioNombre = i;
        while (i < texto.Length && (char.IsAsciiLetterOrDigit(texto[i]) || (i > inicioNombre && texto[i] is '_' or '-' or '.' or ':')))
        {
            i++;
        }
        var nombre = texto[inicioNombre..i];
        if (nombre.Length == 0 || !char.IsAsciiLetter(nombre[0]))
        {
            errores.Add($"Línea {linea(inicio)}: «<» no se admite suelto ni el HTML; escríbelo como \\< o en código.");
            return inicio + 1;
        }
        if (!Componentes.Contains(nombre))
        {
            errores.Add(string.Create(CultureInfo.InvariantCulture,
                $"Línea {linea(inicio)}: la etiqueta <{nombre}> no está en la lista blanca ({string.Join(", ", Componentes.Order(StringComparer.Ordinal))})."));
            return i;
        }

        var autocierre = false;
        while (true)
        {
            while (i < texto.Length && char.IsWhiteSpace(texto[i]))
            {
                i++;
            }
            if (i >= texto.Length)
            {
                errores.Add($"Línea {linea(inicio)}: la etiqueta <{nombre}> no se cierra.");
                return i;
            }
            if (texto[i] == '>')
            {
                i++;
                break;
            }
            if (texto[i] == '/' && i + 1 < texto.Length && texto[i + 1] == '>')
            {
                autocierre = true;
                i += 2;
                break;
            }
            if (cierre)
            {
                errores.Add($"Línea {linea(inicio)}: el cierre </{nombre}> no lleva atributos.");
                return i + 1;
            }
            var inicioAtributo = i;
            while (i < texto.Length && (char.IsAsciiLetterOrDigit(texto[i]) || texto[i] is '_' or '-'))
            {
                i++;
            }
            if (i == inicioAtributo || i + 1 >= texto.Length || texto[i] != '=' || texto[i + 1] is not ('"' or '\''))
            {
                errores.Add($"Línea {linea(inicio)}: los atributos de <{nombre}> solo pueden ser cadenas literales (nombre=\"valor\").");
                return Math.Max(i, inicioAtributo + 1);
            }
            var comilla = texto[i + 1];
            var finValor = texto.IndexOf(comilla, i + 2);
            if (finValor < 0)
            {
                errores.Add($"Línea {linea(inicio)}: un atributo de <{nombre}> no cierra su comilla.");
                return texto.Length;
            }
            i = finValor + 1;
        }

        if (cierre)
        {
            if (etiquetas.Count == 0 || etiquetas.Peek().Nombre != nombre)
            {
                errores.Add($"Línea {linea(inicio)}: </{nombre}> no cierra ninguna etiqueta abierta.");
            }
            else
            {
                etiquetas.Pop();
            }
        }
        else if (!autocierre)
        {
            etiquetas.Push((nombre, linea(inicio)));
        }
        return i;
    }

    /// <summary>Revisa el destino de <c>[texto](destino)</c> o de una imagen; <paramref name="inicio"/> queda tras el paréntesis.</summary>
    private static int RevisarEnlace(string texto, int inicio, Func<int, int> linea, List<string> errores)
    {
        var i = inicio;
        while (i < texto.Length && !char.IsWhiteSpace(texto[i]) && texto[i] != ')')
        {
            i++;
        }
        if (!DestinoValido(texto[inicio..i]))
        {
            errores.Add($"Línea {linea(inicio)}: el enlace solo admite https, mailto o una ruta interna.");
        }
        return i;
    }

    /// <summary>
    /// <c>https:</c>, <c>mailto:</c>, ancla o ruta interna. Un destino con entidades o barras invertidas se rechaza: podría
    /// esconder un esquema (<c>javascript&amp;#58;</c>).
    /// </summary>
    internal static bool DestinoValido(string destino)
    {
        var d = destino.Trim().TrimStart('<').TrimEnd('>');
        if (d.Contains('\\', StringComparison.Ordinal) || d.Contains("&#", StringComparison.Ordinal)
            || d.Contains("&colon", StringComparison.OrdinalIgnoreCase) || d.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }
        var esquema = Esquema().Match(d);
        return !esquema.Success
            || esquema.Value.Equals("https:", StringComparison.OrdinalIgnoreCase)
            || esquema.Value.Equals("mailto:", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^ {0,3}(?<marca>`{3,}|~{3,})(?<resto>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Cerca();

    [GeneratedRegex(@"^(import|export)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Modulo();

    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:\s*(?<destino>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex DefinicionEnlace();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex Esquema();
}
