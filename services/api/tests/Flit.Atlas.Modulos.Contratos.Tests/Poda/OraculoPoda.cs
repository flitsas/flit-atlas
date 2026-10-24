using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Poda;
using Flit.Atlas.Modulos.Contratos.Specs;

namespace Flit.Atlas.Modulos.Contratos.Tests.Poda;

/// <summary>
/// Revisiones de un spec podado escritas aparte del podador (no reusan su recorrido), para las pruebas de propiedades
/// (HU 4.2, ATLAS-ADR-0038). El índice y la validez los da <see cref="AnalizadorSpec"/>, que lee con otro lector
/// (Microsoft.OpenApi para OpenAPI; YamlConverter para AsyncAPI).
/// </summary>
internal static class OraculoPoda
{
    public static JsonObject Leer(string contenido) => TextoSpec.Leer(contenido, out _);

    /// <summary>Raíz del repo (donde está <c>Flit.Atlas.slnx</c>).</summary>
    public static string Raiz { get; } = Buscar();

    private static string Buscar()
    {
        for (var carpeta = new DirectoryInfo(AppContext.BaseDirectory); carpeta is not null; carpeta = carpeta.Parent)
        {
            if (File.Exists(Path.Combine(carpeta.FullName, "Flit.Atlas.slnx")))
            {
                return carpeta.FullName;
            }
        }
        throw new InvalidOperationException("No se encontró Flit.Atlas.slnx encima de " + AppContext.BaseDirectory);
    }

    public static string Spec(string rutaRelativa) => File.ReadAllText(Path.Combine(Raiz, rutaRelativa));

    /// <summary>Todos los valores de <c>$ref</c> del árbol.</summary>
    public static List<string> Referencias(JsonNode? nodo)
    {
        var lista = new List<string>();
        Recorrer(nodo, (o, _) =>
        {
            if (o["$ref"] is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            {
                lista.Add(v.GetValue<string>());
            }
        });
        return lista;
    }

    public static void Recorrer(JsonNode? nodo, Action<JsonObject, string> visitar, string ruta = "")
    {
        switch (nodo)
        {
            case JsonObject o:
                visitar(o, ruta);
                foreach (var (k, h) in o)
                {
                    Recorrer(h, visitar, ruta + "/" + k);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    Recorrer(a[i], visitar, ruta + "/" + i.ToString(CultureInfo.InvariantCulture));
                }
                break;
        }
    }

    public static JsonNode? Resolver(JsonNode raiz, string referencia)
    {
        if (!referencia.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }
        JsonNode? actual = raiz;
        foreach (var parte in referencia[2..].Split('/'))
        {
            var clave = Uri.UnescapeDataString(parte).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            actual = actual switch
            {
                JsonObject o when o.TryGetPropertyValue(clave, out var h) => h,
                JsonArray a when int.TryParse(clave, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < a.Count => a[i],
                _ => null,
            };
            if (actual is null)
            {
                return null;
            }
        }
        return actual;
    }

    /// <summary>Referencias que no son locales o no resuelven.</summary>
    public static List<string> ReferenciasRotas(JsonObject raiz) =>
        [.. Referencias(raiz).Where(r => Resolver(raiz, r) is null).Distinct(StringComparer.Ordinal)];

    /// <summary>Nombres usados en algún <c>security</c> (OpenAPI y AsyncAPI 2) que no están en components/securitySchemes.</summary>
    public static List<string> EsquemasFaltantes(JsonObject raiz)
    {
        var esquemas = (raiz["components"] as JsonObject)?["securitySchemes"] as JsonObject ?? [];
        var faltantes = new List<string>();
        void Revisar(JsonObject o)
        {
            foreach (var req in (o["security"] as JsonArray ?? []).OfType<JsonObject>())
            {
                faltantes.AddRange(req.Select(p => p.Key).Where(n => n != "$ref" && !esquemas.ContainsKey(n)));
            }
        }
        Revisar(raiz);
        foreach (var (k, h) in raiz)
        {
            if (k != "components")
            {
                Recorrer(h, (o, _) => Revisar(o));
            }
        }
        return [.. faltantes.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Extensiones de procedencia interna que nunca salen en un spec podado (escritas aquí aparte del podador).</summary>
    private static readonly string[] Procedencia = ["x-flit-fuente", "x-flit-ambientes"];

    /// <summary>Rutas de los nodos marcados como internos y de las extensiones de procedencia interna.</summary>
    public static List<string> Internos(JsonObject raiz)
    {
        var lista = new List<string>();
        Recorrer(raiz, (o, ruta) =>
        {
            if (Marcado(o["x-internal"]) || Marcado(o["x-interno"]))
            {
                lista.Add(ruta);
            }
            foreach (var extension in Procedencia)
            {
                if (o.ContainsKey(extension))
                {
                    lista.Add($"{ruta}/{extension}");
                }
            }
        });
        return lista;
    }

    private static bool Marcado(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    /// <summary>
    /// Componentes (y canales de AsyncAPI 3) que nada alcanza desde la raíz: búsqueda en anchura por <c>$ref</c>, nombres
    /// de <c>security</c> y de <c>discriminator.mapping</c>.
    /// </summary>
    public static List<string> Huerfanos(JsonObject raiz, bool canalesSonUnidades)
    {
        var unidades = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (tipo, mapa) in raiz["components"] as JsonObject ?? [])
        {
            foreach (var (nombre, nodo) in mapa as JsonObject ?? [])
            {
                if (!nombre.StartsWith("x-", StringComparison.Ordinal) && nodo is not null)
                {
                    unidades[$"components/{tipo}/{nombre}"] = nodo;
                }
            }
        }
        if (canalesSonUnidades)
        {
            foreach (var (nombre, nodo) in raiz["channels"] as JsonObject ?? [])
            {
                unidades[$"channels/{nombre}"] = nodo!;
            }
        }
        var alcanzadas = new HashSet<string>(StringComparer.Ordinal);
        var cola = new Queue<JsonNode>();
        foreach (var (k, h) in raiz)
        {
            if (k != "components" && !(canalesSonUnidades && k == "channels") && h is not null)
            {
                cola.Enqueue(h);
            }
        }
        void Alcanzar(string unidad)
        {
            if (unidades.TryGetValue(unidad, out var nodo) && alcanzadas.Add(unidad))
            {
                cola.Enqueue(nodo);
            }
        }
        foreach (var req in (raiz["security"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (var (nombre, _) in req)
            {
                Alcanzar($"components/securitySchemes/{nombre}");
            }
        }
        while (cola.Count > 0)
        {
            Recorrer(cola.Dequeue(), (o, _) =>
            {
                if (o["$ref"] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is var r && r.StartsWith("#/", StringComparison.Ordinal))
                {
                    var partes = r[2..].Split('/').Select(p => Uri.UnescapeDataString(p).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
                    if (partes.Length >= 3 && partes[0] == "components")
                    {
                        Alcanzar($"components/{partes[1]}/{partes[2]}");
                    }
                    else if (partes.Length >= 2 && partes[0] == "channels")
                    {
                        Alcanzar($"channels/{partes[1]}");
                    }
                }
                if (o["security"] is JsonArray requisitos)
                {
                    foreach (var req in requisitos.OfType<JsonObject>())
                    {
                        foreach (var (nombre, _) in req)
                        {
                            Alcanzar($"components/securitySchemes/{nombre}");
                        }
                    }
                }
                if (o["discriminator"] is JsonObject d && d["mapping"] is JsonObject m)
                {
                    foreach (var (_, valor) in m)
                    {
                        if (valor is JsonValue mv && mv.GetValue<string>() is var destino && !destino.StartsWith('#'))
                        {
                            Alcanzar($"components/schemas/{destino}");
                        }
                    }
                }
            });
        }
        return [.. unidades.Keys.Where(u => !alcanzadas.Contains(u))];
    }

    /// <summary>Cada objeto del resultado tiene sus claves en el original y cada arreglo es subsecuencia del original.</summary>
    public static bool Subconjunto(JsonNode? resultado, JsonNode? original) => (resultado, original) switch
    {
        (JsonObject r, JsonObject o) => r.All(p => o.TryGetPropertyValue(p.Key, out var h) && Subconjunto(p.Value, h)),
        (JsonArray r, JsonArray o) => Subsecuencia(r, o),
        (null, null) => true,
        (JsonValue r, JsonValue o) => JsonNode.DeepEquals(r, o),
        _ => false,
    };

    private static bool Subsecuencia(JsonArray r, JsonArray o)
    {
        var j = 0;
        foreach (var elemento in r)
        {
            while (j < o.Count && !Subconjunto(elemento, o[j]))
            {
                j++;
            }
            if (j == o.Count)
            {
                return false;
            }
            j++;
        }
        return true;
    }

    /// <summary>Claves del índice de <see cref="AnalizadorSpec"/>.</summary>
    /// <remarks>
    /// El spec debe ser válido salvo por los errores que ya traía el original (<paramref name="admitidos"/>; p. ej.
    /// atlas.v1.yaml tiene dos rutas con la misma firma): la poda no puede agregar errores.
    /// </remarks>
    public static List<string> ClavesAnalizador(TipoContrato tipo, string contenido, IReadOnlyCollection<string> admitidos)
    {
        var analizado = AnalizadorSpec.Analizar(tipo, contenido);
        var nuevos = analizado.Errores.Except(admitidos).ToList();
        Assert.True(nuevos.Count == 0, string.Join('\n', nuevos));
        return [.. analizado.Operaciones.Select(o => o.Clave)];
    }

    public static IReadOnlyList<string> ErroresAnalizador(TipoContrato tipo, string contenido) => AnalizadorSpec.Analizar(tipo, contenido).Errores;
}
