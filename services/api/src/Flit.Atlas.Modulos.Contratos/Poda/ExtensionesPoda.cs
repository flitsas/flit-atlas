using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flit.Atlas.Modulos.Contratos.Poda;

/// <summary>
/// Lista blanca de extensiones <c>x-*</c> de un spec podado (ATLAS-ADR-0038, hallazgo F4-SEC-09): solo salen las que el
/// portal y las exportaciones necesitan, con su forma saneada; cualquier otra <c>x-*</c>, en cualquier nivel (incluidos
/// <c>info</c>, <c>paths</c>, <c>webhooks</c>, <c>channels</c> y <c>components/&lt;tipo&gt;</c>), se quita.
/// </summary>
/// <remarks>
/// <para>Una clave <c>x-…</c> solo es extensión donde el objeto es de la especificación. No lo es donde las claves son
/// nombres del autor (<c>properties</c>, cabeceras de OpenAPI, <c>content</c>, <c>variables</c>…: una cabecera
/// <c>x-request-id</c> es legítima) ni dentro de los datos (<c>example</c>, <c>default</c>, <c>enum</c>, <c>const</c>,
/// <c>value</c> y <c>examples</c> de JSON Schema), que se dejan como están. Ante la duda se trata como extensión y se
/// quita: el error posible es perder un nombre raro, nunca dejar salir una extensión.</para>
/// <para>Permitidas (y por qué):</para>
/// <list type="bullet">
/// <item><c>x-enum-descriptions</c>: descripción de cada valor de un <c>enum</c> (tablas de referencia); lista de cadenas o
/// mapa valor → cadena.</item>
/// <item><c>x-flit-obligatoriedad</c>: matriz de obligatoriedad por tipo de trámite (HU 3.9). Se reconstruye solo con
/// <c>propiedad</c>, <c>tipos[{clave, nombre, valor}]</c> y las filas de <c>campos</c> o <c>documentos</c> (campo, valor,
/// nombre, condición y una celda por tipo). Se quitan <c>discriminador</c> (nota de diseño interna) y
/// <c>columna_excel</c> (encabezados del Excel de origen).</item>
/// <item><c>x-flit-tabla</c>: tabla de referencia del Excel; solo <c>titulo</c>, <c>columnas</c> y <c>filas</c> (sin la
/// <c>hoja</c> de origen).</item>
/// <item><c>x-flit-decimales</c>: decimales de un número (entero de 0 a 18).</item>
/// <item><c>x-public</c>: la operación es compartible (booleano); el visor la usa para la marca «pública».</item>
/// </list>
/// <para>No salen, entre otras: <c>x-flit-fuente</c>, <c>x-flit-origen</c>, <c>x-flit-hoja-excel</c>,
/// <c>info.x-flit-changelog</c>, <c>x-flit-ambiente(s)</c> (los servidores ya son los del destino),
/// <c>x-flit-visibilidad</c> (dato de gobierno de Atlas, no del cliente), <c>x-flit-sin-autenticacion</c>,
/// <c>x-internal</c>/<c>x-interno</c> en falso, <c>x-atlas-*</c> ni extensiones de terceros.</para>
/// </remarks>
internal static class ExtensionesPoda
{
    public const string EnumDescripciones = "x-enum-descriptions";

    public const string Obligatoriedad = "x-flit-obligatoriedad";

    public const string Tabla = "x-flit-tabla";

    public const string Decimales = "x-flit-decimales";

    public const string Publica = "x-public";

    /// <summary>Las únicas extensiones que pueden quedar en un spec podado.</summary>
    public static IReadOnlySet<string> Permitidas { get; } =
        new HashSet<string>(StringComparer.Ordinal) { EnumDescripciones, Obligatoriedad, Tabla, Decimales, Publica };

    /// <summary>Claves cuyo valor es un mapa de nombres del autor (una <c>x-…</c> ahí es un nombre, no una extensión).</summary>
    private static readonly HashSet<string> MapasDeNombres = new(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "$defs", "definitions", "dependentSchemas", "dependentRequired",
        "content", "encoding", "links", "callbacks", "variables", "scopes", "availableScopes", "mapping",
    };

    /// <summary>Claves cuyo valor es un mapa de unidades de la especificación (sus hijos son objetos de la especificación).</summary>
    private static readonly HashSet<string> MapasDeUnidades = new(StringComparer.Ordinal)
    {
        "paths", "webhooks", "responses", "channels", "operations", "messages", "servers", "parameters", "pathItems",
    };

    /// <summary>Claves cuyo valor es dato del autor (ejemplos, valores por defecto, enumeraciones): no se recorre.</summary>
    private static readonly HashSet<string> Datos = new(StringComparer.Ordinal) { "example", "default", "enum", "const", "value" };

    private enum Contexto
    {
        /// <summary>Objeto de la especificación: una <c>x-…</c> es extensión; los hijos se clasifican por su clave.</summary>
        Especificacion,

        /// <summary>Mapa de nombres del autor: ninguna clave es extensión; los hijos son objetos de la especificación.</summary>
        Nombres,

        /// <summary>Mapa de unidades (paths, responses, components/&lt;tipo&gt;…): una <c>x-…</c> es extensión; los hijos son objetos de la especificación.</summary>
        Unidades,

        /// <summary><c>components</c>: una <c>x-…</c> es extensión; cada hijo es un mapa de unidades.</summary>
        Componentes,
    }

    /// <summary>Quita toda extensión no permitida y sanea las permitidas; devuelve cuántas quitó.</summary>
    /// <param name="cabecerasSonMapa">OpenAPI: <c>headers</c> es un mapa de nombres; en AsyncAPI es un schema.</param>
    public static int Quitar(JsonObject raiz, bool cabecerasSonMapa)
    {
        var quitadas = 0;
        Recorrer(raiz, cabecerasSonMapa, (objeto, clave, valor) =>
        {
            var saneado = Permitidas.Contains(clave) ? Sanear(clave, valor) : null;
            if (saneado is null)
            {
                objeto.Remove(clave);
                quitadas++;
            }
            else if (!JsonNode.DeepEquals(saneado, valor))
            {
                objeto[clave] = saneado;
            }
        });
        return quitadas;
    }

    /// <summary>Falla cerrado si queda una extensión no permitida o una permitida sin su forma saneada.</summary>
    public static void Verificar(JsonObject raiz, bool cabecerasSonMapa) =>
        Recorrer(raiz, cabecerasSonMapa, (_, clave, valor) =>
        {
            if (!Permitidas.Contains(clave))
            {
                throw new PodaException(MotivoPoda.VerificacionFallida, "El resultado aún tiene una extensión que no está en la lista blanca.", [clave]);
            }
            if (Sanear(clave, valor) is not { } saneado || !JsonNode.DeepEquals(saneado, valor))
            {
                throw new PodaException(MotivoPoda.VerificacionFallida, "El resultado tiene una extensión permitida con contenido no saneado.", [clave]);
            }
        });

    /// <summary>
    /// Visita cada extensión (objeto dueño, clave, valor) sin entrar en su valor ni en los datos del autor. Primero junta y
    /// luego visita, así la visita puede quitar o reemplazar.
    /// </summary>
    private static void Recorrer(JsonObject raiz, bool cabecerasSonMapa, Action<JsonObject, string, JsonNode?> visitar)
    {
        var extensiones = new List<(JsonObject Objeto, string Clave, JsonNode? Valor)>();
        var pila = new Stack<(JsonNode Nodo, Contexto Contexto)>();
        pila.Push((raiz, Contexto.Especificacion));
        while (pila.Count > 0)
        {
            var (nodo, contexto) = pila.Pop();
            if (nodo is JsonArray arreglo)
            {
                foreach (var hijo in arreglo)
                {
                    if (hijo is not null)
                    {
                        pila.Push((hijo, Contexto.Especificacion));
                    }
                }
                continue;
            }
            if (nodo is not JsonObject objeto)
            {
                continue;
            }
            foreach (var (clave, hijo) in objeto)
            {
                if (contexto != Contexto.Nombres && clave.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
                {
                    extensiones.Add((objeto, clave, hijo));
                    continue;
                }
                if (hijo is null)
                {
                    continue;
                }
                var siguiente = contexto switch
                {
                    Contexto.Nombres or Contexto.Unidades => Contexto.Especificacion,
                    Contexto.Componentes => Contexto.Unidades,
                    _ => DeClave(clave, hijo, cabecerasSonMapa),
                };
                if (siguiente is { } c)
                {
                    pila.Push((hijo, c));
                }
            }
        }
        foreach (var (objeto, clave, valor) in extensiones)
        {
            visitar(objeto, clave, valor);
        }
    }

    /// <summary>Contexto del valor de <paramref name="clave"/> en un objeto de la especificación; null si es dato del autor.</summary>
    private static Contexto? DeClave(string clave, JsonNode hijo, bool cabecerasSonMapa)
    {
        if (Datos.Contains(clave) || (clave == "examples" && hijo is JsonArray))
        {
            return null;
        }
        if (clave == NucleoPoda.Componentes)
        {
            return Contexto.Componentes;
        }
        if (MapasDeNombres.Contains(clave) || (clave == "examples" && hijo is JsonObject) || (clave == "headers" && cabecerasSonMapa))
        {
            return Contexto.Nombres;
        }
        return MapasDeUnidades.Contains(clave) && hijo is JsonObject ? Contexto.Unidades : Contexto.Especificacion;
    }

    // --- Saneo de las permitidas: devuelve la forma que puede salir o null si no hay nada que salga ---

    private static JsonNode? Sanear(string clave, JsonNode? valor) => clave switch
    {
        Publica => valor is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? valor.DeepClone() : null,
        Decimales => valor is JsonValue v && v.GetValueKind() == JsonValueKind.Number
            && int.TryParse(v.ToJsonString(), NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d <= 18 ? valor.DeepClone() : null,
        EnumDescripciones => SanearDescripciones(valor),
        Tabla => SanearTabla(valor),
        Obligatoriedad => SanearObligatoriedad(valor),
        _ => null,
    };

    private static bool Escalar(JsonNode? nodo) =>
        nodo is null || (nodo is JsonValue v && v.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False);

    private static bool EsTexto(JsonNode? nodo) => nodo is JsonValue v && v.GetValueKind() == JsonValueKind.String;

    private static JsonNode? SanearDescripciones(JsonNode? valor) => valor switch
    {
        JsonArray lista => new JsonArray([.. lista.Select(e => EsTexto(e) ? e!.DeepClone() : null)]),
        JsonObject mapa => Objeto(mapa.Where(p => EsTexto(p.Value))),
        _ => null,
    };

    private static JsonObject Objeto(IEnumerable<KeyValuePair<string, JsonNode?>> pares)
    {
        var salida = new JsonObject();
        foreach (var (k, v) in pares)
        {
            salida[k] = v?.DeepClone();
        }
        return salida;
    }

    /// <summary>Copia de <paramref name="fila"/> solo con las claves dadas y valores escalares.</summary>
    private static JsonObject Fila(JsonObject fila, Func<string, bool> permitida) =>
        Objeto(fila.Where(p => permitida(p.Key) && Escalar(p.Value)));

    private static JsonObject? SanearTabla(JsonNode? valor)
    {
        if (valor is not JsonObject tabla)
        {
            return null;
        }
        var salida = new JsonObject();
        if (EsTexto(tabla["titulo"]))
        {
            salida["titulo"] = tabla["titulo"]!.DeepClone();
        }
        if (tabla["columnas"] is JsonArray columnas)
        {
            var nombres = columnas.Where(EsTexto).Select(c => c!.GetValue<string>()).ToList();
            salida["columnas"] = new JsonArray([.. nombres.Select(n => (JsonNode)JsonValue.Create(n)!)]);
            if (tabla["filas"] is JsonArray filas)
            {
                salida["filas"] = new JsonArray([.. filas.OfType<JsonObject>().Select(f => (JsonNode)Fila(f, nombres.Contains))]);
            }
        }
        return salida.Count == 0 ? null : salida;
    }

    private static readonly string[] ClavesTipo = ["clave", "nombre", "valor"];

    private static readonly string[] ClavesCampo = ["campo", "condicion"];

    private static readonly string[] ClavesDocumento = ["valor", "nombre", "condicion"];

    private static JsonObject? SanearObligatoriedad(JsonNode? valor)
    {
        if (valor is not JsonObject matriz || matriz["tipos"] is not JsonArray tipos)
        {
            return null;
        }
        var salida = new JsonObject();
        if (EsTexto(matriz["propiedad"]))
        {
            salida["propiedad"] = matriz["propiedad"]!.DeepClone();
        }
        var limpios = tipos.OfType<JsonObject>().Where(t => EsTexto(t["clave"])).Select(t => Fila(t, ClavesTipo.Contains)).ToList();
        salida["tipos"] = new JsonArray([.. limpios]);
        var claves = limpios.Select(t => t["clave"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var (clase, fijas) in new[] { ("campos", ClavesCampo), ("documentos", ClavesDocumento) })
        {
            if (matriz[clase] is JsonArray filas)
            {
                salida[clase] = new JsonArray([.. filas.OfType<JsonObject>().Select(f => (JsonNode)Fila(f, k => fijas.Contains(k) || claves.Contains(k)))]);
            }
        }
        return salida;
    }
}
