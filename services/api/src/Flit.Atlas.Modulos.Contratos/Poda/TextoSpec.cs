using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;
using SharpYaml;
using SharpYaml.Events;
using SharpYaml.Serialization;

namespace Flit.Atlas.Modulos.Contratos.Poda;

/// <summary>
/// Lee un spec (JSON o YAML) a un árbol <see cref="JsonNode"/> y lo vuelve a escribir en el mismo formato, sin perder
/// nada: extensiones, ejemplos y el orden de las claves (ATLAS-ADR-0038).
/// <list type="bullet">
/// <item>JSON: <c>System.Text.Json</c>; los números conservan su texto original.</item>
/// <item>YAML: SharpYaml (el mismo lector que usa <c>Microsoft.OpenApi.YamlReader</c>) con los escalares sin comillas
/// resueltos con el esquema core de YAML 1.2 (null, booleanos, números). Al escribir, una cadena va sin comillas solo si
/// ningún lector YAML (1.1 o 1.2) la puede confundir con otro tipo; si no, entre comillas dobles o en bloque literal.</item>
/// </list>
/// Topes de tamaño, profundidad y nodos (contra alias que se multiplican).
/// </summary>
internal static partial class TextoSpec
{
    public const int ProfundidadMaxima = 128;

    public const int NodosMaximos = 2_000_000;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static FormatoSpec Formato(string contenido) =>
        contenido.AsSpan().TrimStart().StartsWith("{") ? FormatoSpec.Json : FormatoSpec.Yaml;

    /// <exception cref="PodaException">Con <see cref="MotivoPoda.SpecInvalida"/> si no se puede leer o no es un objeto.</exception>
    public static JsonObject Leer(string contenido, out FormatoSpec formato)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        formato = Formato(contenido);
        if (Encoding.UTF8.GetByteCount(contenido) > AnalizadorSpec.TamanoMaximo)
        {
            throw new PodaException(MotivoPoda.SpecInvalida, "El spec supera el tamaño máximo.");
        }
        JsonNode? raiz;
        try
        {
            var nodos = 0;
            if (formato == FormatoSpec.Json)
            {
                raiz = JsonNode.Parse(contenido, documentOptions: new JsonDocumentOptions { MaxDepth = ProfundidadMaxima });
                // Recorrerlo todo materializa los objetos: así salen aquí las claves repetidas y los topes.
                Contar(raiz, 0, ref nodos);
            }
            else
            {
                var flujo = new YamlStream();
                flujo.Load(new StringReader(contenido));
                if (flujo.Documents.Count != 1)
                {
                    throw new FormatException("El YAML debe tener un solo documento.");
                }
                raiz = DesdeYaml(flujo.Documents[0].RootNode, 0, ref nodos);
            }
        }
        catch (PodaException)
        {
            throw;
        }
#pragma warning disable CA1031 // Cualquier fallo del lector es un spec inválido, no un error del servidor.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            throw new PodaException(MotivoPoda.SpecInvalida, $"No se pudo leer el spec: {Recortar(ex.Message)}");
        }
        return raiz as JsonObject ?? throw new PodaException(MotivoPoda.SpecInvalida, "El spec debe ser un objeto.");
    }

    public static string Escribir(JsonObject raiz, FormatoSpec formato)
    {
        if (formato == FormatoSpec.Json)
        {
            return raiz.ToJsonString(OpcionesJson) + "\n";
        }
        var salida = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var emisor = new Emitter(salida, bestIndent: 2, bestWidth: 4000, isCanonical: false, forceIndentLess: false, emitKeyQuoted: false);
        emisor.Emit(new StreamStart());
        emisor.Emit(new DocumentStart());
        Emitir(emisor, raiz);
        emisor.Emit(new DocumentEnd(true));
        emisor.Emit(new StreamEnd());
        return salida.ToString();
    }

    private static string Recortar(string texto) => texto.Length > 300 ? texto[..300] : texto;

    private static void Contar(JsonNode? nodo, int profundidad, ref int nodos)
    {
        if (++nodos > NodosMaximos || profundidad > ProfundidadMaxima)
        {
            throw new FormatException("El spec supera el tope de nodos o de profundidad.");
        }
        switch (nodo)
        {
            case JsonObject objeto:
                foreach (var (_, hijo) in objeto)
                {
                    Contar(hijo, profundidad + 1, ref nodos);
                }
                break;
            case JsonArray arreglo:
                foreach (var hijo in arreglo)
                {
                    Contar(hijo, profundidad + 1, ref nodos);
                }
                break;
        }
    }

    // --- YAML → JSON ---

    private static JsonNode? DesdeYaml(YamlNode nodo, int profundidad, ref int nodos)
    {
        if (++nodos > NodosMaximos || profundidad > ProfundidadMaxima)
        {
            throw new FormatException("El spec supera el tope de nodos o de profundidad.");
        }
        switch (nodo)
        {
            case YamlMappingNode mapa:
                var objeto = new JsonObject();
                foreach (var (llave, valor) in mapa.Children)
                {
                    if (llave is not YamlScalarNode { Value: var texto })
                    {
                        throw new FormatException("Las llaves de un mapa deben ser escalares.");
                    }
                    texto ??= string.Empty;
                    if (texto == "<<")
                    {
                        throw new FormatException("Las llaves de mezcla de YAML (<<) no se admiten.");
                    }
                    if (!objeto.TryAdd(texto, DesdeYaml(valor, profundidad + 1, ref nodos)))
                    {
                        throw new FormatException($"Llave repetida: «{Recortar(texto)}».");
                    }
                }
                return objeto;
            case YamlSequenceNode secuencia:
                var arreglo = new JsonArray();
                foreach (var hijo in secuencia.Children)
                {
                    arreglo.Add(DesdeYaml(hijo, profundidad + 1, ref nodos));
                }
                return arreglo;
            case YamlScalarNode escalar:
                return Escalar(escalar);
            default:
                throw new FormatException("Nodo YAML desconocido.");
        }
    }

    private static JsonNode? Escalar(YamlScalarNode escalar)
    {
        var valor = escalar.Value ?? string.Empty;
        var etiqueta = escalar.Tag;
        if (etiqueta is "!!str" or "tag:yaml.org,2002:str")
        {
            return JsonValue.Create(valor);
        }
        if (escalar.Style is not (ScalarStyle.Plain or ScalarStyle.Any))
        {
            return JsonValue.Create(valor);
        }
        if (valor is "" or "~" or "null" or "Null" or "NULL")
        {
            return null;
        }
        if (valor is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }
        if (valor is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }
        if (NumeroJson().IsMatch(valor))
        {
            // Conserva el texto del número tal cual (1.0 sigue siendo 1.0).
            return JsonNode.Parse(valor);
        }
        if (NumeroYaml().IsMatch(valor) && decimal.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero))
        {
            return JsonValue.Create(numero);
        }
        if (valor.StartsWith("0x", StringComparison.Ordinal) && long.TryParse(valor.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
        {
            return JsonValue.Create(hex);
        }
        return JsonValue.Create(valor);
    }

    [GeneratedRegex(@"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?$")]
    private static partial Regex NumeroJson();

    [GeneratedRegex(@"^[-+]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][-+]?[0-9]+)?$")]
    private static partial Regex NumeroYaml();

    // --- JSON → YAML ---

    private static void Emitir(Emitter emisor, JsonNode? nodo)
    {
        switch (nodo)
        {
            case JsonObject objeto:
                emisor.Emit(new MappingStart(null, null, true, objeto.Count == 0 ? YamlStyle.Flow : YamlStyle.Block));
                foreach (var (clave, hijo) in objeto)
                {
                    emisor.Emit(Cadena(clave));
                    Emitir(emisor, hijo);
                }
                emisor.Emit(new MappingEnd());
                break;
            case JsonArray arreglo:
                emisor.Emit(new SequenceStart(null, null, true, arreglo.Count == 0 ? YamlStyle.Flow : YamlStyle.Block));
                foreach (var hijo in arreglo)
                {
                    Emitir(emisor, hijo);
                }
                emisor.Emit(new SequenceEnd());
                break;
            case JsonValue valor:
                emisor.Emit(valor.GetValueKind() switch
                {
                    JsonValueKind.String => Cadena(valor.GetValue<string>()),
                    JsonValueKind.True => Plano("true"),
                    JsonValueKind.False => Plano("false"),
                    JsonValueKind.Number => Plano(valor.ToJsonString()),
                    _ => Plano("null"),
                });
                break;
            default:
                emisor.Emit(Plano("null"));
                break;
        }
    }

    private static Scalar Plano(string texto) => new(null, null, texto, ScalarStyle.Plain, true, true);

    private static Scalar Cadena(string texto)
    {
        if (EsPlanoSeguro(texto))
        {
            return Plano(texto);
        }
        // El emisor cae a comillas dobles si el bloque literal no puede representar el texto (espacios al final…).
        var estilo = texto.Contains('\n', StringComparison.Ordinal) && !texto.Contains('\r', StringComparison.Ordinal) ? ScalarStyle.Literal : ScalarStyle.DoubleQuoted;
        return new(null, null, texto, estilo, false, true);
    }

    /// <summary>Sin comillas solo lo que ningún lector (YAML 1.1 o 1.2) puede leer como otra cosa que una cadena.</summary>
    private static bool EsPlanoSeguro(string texto) =>
        CadenaPlana().IsMatch(texto) && !Reservadas.Contains(texto);

    private static readonly HashSet<string> Reservadas = new(StringComparer.OrdinalIgnoreCase)
    {
        "y", "n", "yes", "no", "on", "off", "true", "false", "null",
    };

    // Empieza por letra, _, $ o / (nada de indicadores de YAML, dígitos ni signos) y no tiene «:», «#», comillas ni
    // espacios al final: así ningún lector la toma por número, fecha, booleano, null, alias o comentario.
    [GeneratedRegex(@"^[\p{L}_$/](?:[\p{L}\p{N}_ .,/()\-${}+~@*]*[\p{L}\p{N}_.,/()\-${}+~@*])?$")]
    private static partial Regex CadenaPlana();
}
