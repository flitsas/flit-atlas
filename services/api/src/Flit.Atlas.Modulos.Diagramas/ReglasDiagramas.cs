using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Flit.Atlas.Modulos.Diagramas.Datos;
using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Modulos.Diagramas;

/// <summary>Reglas puras de los diagramas (ATLAS-ADR-0054): valores, normalización y huella.</summary>
internal static partial class ReglasDiagramas
{
    /// <summary>El mismo tope del visor Mermaid (ATLAS-ADR-0053 §5).</summary>
    public const int LargoMermaid = 20_000;

    public const int LargoFuenteXml = 1_000_000;

    public const int LargoFuenteLienzo = 1_000_000;

    public static readonly IReadOnlyDictionary<string, TipoDiagrama> Tipos = new Dictionary<string, TipoDiagrama>(StringComparer.Ordinal)
    {
        ["lienzo"] = TipoDiagrama.Lienzo,
        ["mermaid"] = TipoDiagrama.Mermaid,
        ["bpmn"] = TipoDiagrama.Bpmn,
    };

    public static readonly IReadOnlyDictionary<string, VisibilidadDiagrama> Visibilidades = new Dictionary<string, VisibilidadDiagrama>(StringComparer.Ordinal)
    {
        ["interno_negocio"] = VisibilidadDiagrama.InternoNegocio,
        ["interno_ti"] = VisibilidadDiagrama.InternoTi,
        ["restringido"] = VisibilidadDiagrama.Restringido,
    };

    public static string Texto(TipoDiagrama t) => Tipos.First(p => p.Value == t).Key;

    public static string Texto(VisibilidadDiagrama v) => Visibilidades.First(p => p.Value == v).Key;

    public static string Texto(OrigenVersionDiagrama o) => o switch
    {
        OrigenVersionDiagrama.Creacion => "creacion",
        OrigenVersionDiagrama.Edicion => "edicion",
        _ => "restauracion",
    };

    public static bool CodigoValido(string? codigo) => codigo is { Length: > 0 and <= DiagramasDbContext.LargoCodigo } && FormatoCodigo().IsMatch(codigo);

    /// <summary>SHA-256 en un JSON con orden fijo: restaurar la N da la misma huella que la N.</summary>
    public static string Huella(string titulo, VisibilidadDiagrama visibilidad, string fuente) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            titulo,
            visibilidad = Texto(visibilidad),
            fuente,
        }))));

    public static string Normalizar(string? texto) =>
        (texto ?? string.Empty).TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>El aplicativo y sus ancestros (tope de profundidad contra datos corruptos).</summary>
    public static IEnumerable<Guid> ConAncestros(Guid aplicativoId, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        Guid? actual = aplicativoId;
        for (var i = 0; actual is { } id && i < 10; i++)
        {
            yield return id;
            actual = aplicativos.TryGetValue(id, out var a) ? a.PadreId : null;
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoCodigo();
}

/// <summary>El resultado de validar una fuente: la fuente que se guarda, o los errores.</summary>
internal sealed record FuenteValidada(string? Fuente, LienzoFuente? Lienzo, IReadOnlyList<string> Errores)
{
    public static FuenteValidada Mal(params string[] errores) => new(null, null, errores);
}

/// <summary>
/// Valida y limpia el XML de BPMN (ATLAS-ADR-0054 §4): DTD prohibida y sin resolver contra XXE y expansión de entidades, y una
/// lista blanca de espacios de nombres. Lo que no es del estándar se quita; lo que queda es lo que se guarda.
/// </summary>
internal static class ValidadorBpmn
{
    private const string EspacioBpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private const string EspacioBpmnDi = "http://www.omg.org/spec/BPMN/20100524/DI";
    private const string EspacioDc = "http://www.omg.org/spec/DD/20100524/DC";
    private const string EspacioDi = "http://www.omg.org/spec/DD/20100524/DI";
    private const string EspacioXsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly HashSet<string> Permitidos = new([EspacioBpmn, EspacioBpmnDi, EspacioDc, EspacioDi], StringComparer.Ordinal);

    /// <summary>Del estándar, pero traen código o referencias a otros archivos.</summary>
    private static readonly HashSet<string> ElementosQuitados = new(["extensionElements", "script", "import"], StringComparer.Ordinal);

    public static FuenteValidada Validar(string? xml)
    {
        var fuente = ReglasDiagramas.Normalizar(xml);
        if (string.IsNullOrWhiteSpace(fuente) || fuente.Length > ReglasDiagramas.LargoFuenteXml)
        {
            return FuenteValidada.Mal($"El XML es obligatorio y va hasta {ReglasDiagramas.LargoFuenteXml / 1000} mil caracteres.");
        }

        XDocument documento;
        var ajustes = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = ReglasDiagramas.LargoFuenteXml * 2L,
        };
        try
        {
            using var texto = new StringReader(fuente);
            using var lector = XmlReader.Create(texto, ajustes);
            documento = XDocument.Load(lector);
        }
        catch (XmlException)
        {
            // Sin el mensaje de la librería: puede citar el contenido.
            return FuenteValidada.Mal("El XML está mal formado o trae una DTD, que no se admite.");
        }

        var raiz = documento.Root!;
        if (raiz.Name != XName.Get("definitions", EspacioBpmn))
        {
            return FuenteValidada.Mal("La raíz debe ser bpmn:definitions de BPMN 2.0.");
        }

        Limpiar(raiz);

        var modelo = raiz.Elements().Any(e => e.Name == XName.Get("process", EspacioBpmn) || e.Name == XName.Get("collaboration", EspacioBpmn));
        if (!modelo)
        {
            return FuenteValidada.Mal("Debe traer al menos un proceso o una colaboración.");
        }
        if (!raiz.Elements(XName.Get("BPMNDiagram", EspacioBpmnDi)).Any())
        {
            return FuenteValidada.Mal("Debe traer el dibujo (bpmndi:BPMNDiagram): sin él no se puede mostrar.");
        }
        documento.Declaration = null;
        return new FuenteValidada(documento.ToString(SaveOptions.DisableFormatting), null, []);
    }

    private static void Limpiar(XElement raiz)
    {
        // A una lista: quitar mientras se recorre rompería el recorrido. Lo que está debajo de algo quitado se va con él.
        foreach (var elemento in raiz.DescendantsAndSelf().ToList())
        {
            if (elemento != raiz && (!Permitidos.Contains(elemento.Name.NamespaceName)
                || (elemento.Name.NamespaceName == EspacioBpmn && ElementosQuitados.Contains(elemento.Name.LocalName))))
            {
                if (elemento.Parent is not null)
                {
                    elemento.Remove();
                }
                continue;
            }
            foreach (var atributo in elemento.Attributes().ToList())
            {
                if (!AtributoPermitido(atributo))
                {
                    atributo.Remove();
                }
            }
        }
    }

    private static bool AtributoPermitido(XAttribute a)
    {
        if (a.IsNamespaceDeclaration)
        {
            return Permitidos.Contains(a.Value) || a.Value == EspacioXsi;
        }
        var espacio = a.Name.NamespaceName;
        if (espacio.Length == 0)
        {
            return !a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase);
        }
        return espacio == EspacioXsi && a.Name.LocalName == "type";
    }
}

/// <summary>Un nodo del lienzo tal como se guarda.</summary>
/// <param name="Tipo"><c>actor</c>, <c>sistema</c>, <c>contenedor</c>, <c>componente</c> o <c>nota</c>.</param>
internal sealed record NodoLienzo(
    string Id, string Tipo, string Etiqueta, double X, double Y, Guid? AplicativoId = null, string? ElementoLikeC4 = null);

/// <summary>Una relación del lienzo tal como se guarda.</summary>
internal sealed record RelacionLienzo(string Id, string Origen, string Destino, string? Etiqueta = null);

/// <summary>El modelo propio del lienzo: el JSON canónico de la versión.</summary>
internal sealed record LienzoFuente(IReadOnlyList<NodoLienzo> Nodos, IReadOnlyList<RelacionLienzo> Relaciones);

/// <summary>Valida el JSON del lienzo (ATLAS-ADR-0054 §4): estricto y con topes, y lo deja en forma canónica.</summary>
internal static partial class ValidadorLienzo
{
    public const int MaximoNodos = 500;
    public const int MaximoRelaciones = 1000;
    private const double MaximaCoordenada = 1_000_000;

    public static readonly IReadOnlySet<string> TiposNodo = new HashSet<string>(StringComparer.Ordinal) { "actor", "sistema", "contenedor", "componente", "nota" };

    private static readonly JsonSerializerOptions Lectura = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };

    private static readonly JsonSerializerOptions Escritura = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static FuenteValidada Validar(string? json, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        ArgumentNullException.ThrowIfNull(aplicativos);
        if (string.IsNullOrWhiteSpace(json) || json.Length > ReglasDiagramas.LargoFuenteLienzo)
        {
            return FuenteValidada.Mal($"El lienzo es obligatorio y va hasta {ReglasDiagramas.LargoFuenteLienzo / 1000} mil caracteres.");
        }
        LienzoFuente? lienzo;
        try
        {
            lienzo = JsonSerializer.Deserialize<LienzoFuente>(json, Lectura);
        }
        catch (JsonException)
        {
            return FuenteValidada.Mal("El lienzo no tiene la forma esperada: nodos y relaciones, sin otras propiedades.");
        }
        if (lienzo?.Nodos is not { Count: > 0 } || lienzo.Relaciones is null)
        {
            return FuenteValidada.Mal("El lienzo debe traer al menos un nodo y la lista de relaciones.");
        }
        if (lienzo.Nodos.Count > MaximoNodos || lienzo.Relaciones.Count > MaximoRelaciones)
        {
            return FuenteValidada.Mal($"El lienzo va hasta {MaximoNodos} nodos y {MaximoRelaciones} relaciones.");
        }

        var errores = new List<string>();
        var claves = new HashSet<string>(StringComparer.Ordinal);
        var nodos = new List<NodoLienzo>(lienzo.Nodos.Count);
        foreach (var n in lienzo.Nodos)
        {
            var etiqueta = (n?.Etiqueta ?? string.Empty).Trim();
            if (n is null || !ClaveValida(n.Id) || !claves.Add(n.Id))
            {
                errores.Add("Cada nodo necesita un id único, de letras, números, guiones y hasta 64 caracteres.");
                continue;
            }
            if (!TiposNodo.Contains(n.Tipo ?? string.Empty))
            {
                errores.Add($"Nodo {n.Id}: el tipo debe ser {string.Join(", ", TiposNodo.Order(StringComparer.Ordinal))}.");
            }
            if (etiqueta.Length is 0 or > DiagramasDbContext.LargoEtiqueta)
            {
                errores.Add($"Nodo {n.Id}: la etiqueta es obligatoria y va hasta {DiagramasDbContext.LargoEtiqueta} caracteres.");
            }
            if (Math.Abs(n.X) > MaximaCoordenada || Math.Abs(n.Y) > MaximaCoordenada)
            {
                errores.Add($"Nodo {n.Id}: la posición está fuera del lienzo.");
            }
            if (n.AplicativoId is { } aplicativo && !aplicativos.ContainsKey(aplicativo))
            {
                errores.Add($"Nodo {n.Id}: el aplicativo no existe en el catálogo.");
            }
            if (n.ElementoLikeC4 is { } elemento && !FormatoElemento().IsMatch(elemento))
            {
                errores.Add($"Nodo {n.Id}: el elemento de LikeC4 no tiene un id válido.");
            }
            nodos.Add(n with { Etiqueta = etiqueta, X = Math.Round(n.X, 1), Y = Math.Round(n.Y, 1) });
        }

        var deRelacion = new HashSet<string>(StringComparer.Ordinal);
        var relaciones = new List<RelacionLienzo>(lienzo.Relaciones.Count);
        foreach (var r in lienzo.Relaciones)
        {
            if (r is null || !ClaveValida(r.Id) || !deRelacion.Add(r.Id))
            {
                errores.Add("Cada relación necesita un id único, de letras, números, guiones y hasta 64 caracteres.");
                continue;
            }
            if (!claves.Contains(r.Origen ?? string.Empty) || !claves.Contains(r.Destino ?? string.Empty))
            {
                errores.Add($"Relación {r.Id}: el origen y el destino deben ser nodos del lienzo.");
            }
            var etiqueta = string.IsNullOrWhiteSpace(r.Etiqueta) ? null : r.Etiqueta.Trim();
            if (etiqueta is { Length: > DiagramasDbContext.LargoEtiqueta })
            {
                errores.Add($"Relación {r.Id}: la etiqueta va hasta {DiagramasDbContext.LargoEtiqueta} caracteres.");
            }
            relaciones.Add(r with { Etiqueta = etiqueta });
        }
        if (errores.Count > 0)
        {
            return new FuenteValidada(null, null, errores.Take(20).ToList());
        }
        var canonico = new LienzoFuente(nodos, relaciones);
        return new FuenteValidada(JsonSerializer.Serialize(canonico, Escritura), canonico, []);
    }

    /// <summary>Lee un JSON ya guardado, para volver a proyectarlo al restaurar.</summary>
    public static LienzoFuente Leer(string json) => JsonSerializer.Deserialize<LienzoFuente>(json, Lectura)!;

    private static bool ClaveValida(string? clave) => clave is { Length: > 0 and <= DiagramasDbContext.LargoClave } && FormatoClave().IsMatch(clave);

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoClave();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoElemento();
}
