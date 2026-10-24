using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;
using static Flit.Atlas.Modulos.Contratos.Poda.NucleoPoda;

namespace Flit.Atlas.Modulos.Contratos.Poda;

/// <summary>
/// Poda de OpenAPI 3.0 y 3.1 (plan 00 §8.6, ATLAS-ADR-0038), sobre el árbol JSON del spec:
/// <list type="number">
/// <item>Conserva solo los métodos seleccionados de <c>paths</c> (y de <c>webhooks</c> en 3.1); una ruta que es
/// <c>$ref</c> a <c>components/pathItems</c> se copia en línea antes de podarla.</item>
/// <item>Reescribe los <c>servers</c> de la raíz con las URL públicas del destino y quita los de rutas, operaciones y links.</item>
/// <item>Quita todo nodo <c>x-internal: true</c> o <c>x-interno: true</c>, toda extensión <c>x-*</c> fuera de la lista blanca
/// (<see cref="ExtensionesPoda"/>) y los links a operaciones que no se comparten.</item>
/// <item>Cierre transitivo de <c>$ref</c> (más <c>discriminator.mapping</c> y los <c>securitySchemes</c> usados en
/// <c>security</c>): se quita todo componente no alcanzado y las etiquetas sin uso.</item>
/// <item>Verifica el resultado (referencias, nada interno, índice ⊆ selección) antes de devolverlo.</item>
/// </list>
/// </summary>
internal sealed partial class PodadorOpenApi : IPodadorSpec
{
    /// <summary>Métodos de un Path Item (OpenAPI 3.0 y 3.1), en el orden en que se recorren.</summary>
    private static readonly string[] Metodos = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public TipoContrato Tipo => TipoContrato.Openapi;

    public IReadOnlyList<OperacionPodable> Operaciones(string contenido)
    {
        var raiz = TextoSpec.Leer(contenido, out _);
        return [.. Indice(raiz, Es31(raiz)).Select(e => new OperacionPodable(e.Clave, e.Interna))];
    }

    public SpecPodado Podar(SolicitudPoda solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var urls = ValidarDestino(solicitud.Destino, soloHttps: true);
        var raiz = TextoSpec.Leer(solicitud.Contenido, out var formato);
        var es31 = Es31(raiz);
        var indice = Indice(raiz, es31);
        var seleccion = Seleccionar(indice, solicitud.Claves);
        var ubicaciones = seleccion.Select(e => e.Ubicacion).ToHashSet(StringComparer.Ordinal);
        var esquemas = EsquemasDeSeguridad(raiz);
        var advertencias = new List<string>();

        var trabajo = (JsonObject)raiz.DeepClone();
        if (!es31 && trabajo.Remove("webhooks"))
        {
            advertencias.Add("Se quitó webhooks: no existe en OpenAPI 3.0.");
        }
        PodarSeccion(raiz, trabajo, "paths", ubicaciones);
        if (es31)
        {
            PodarSeccion(raiz, trabajo, "webhooks", ubicaciones);
            if (trabajo["webhooks"] is JsonObject { Count: 0 })
            {
                trabajo.Remove("webhooks");
            }
        }
        Servidores(trabajo, urls, solicitud.Destino.Ambiente);
        var internos = QuitarInternos(trabajo);
        ExtensionesPoda.Quitar(trabajo, cabecerasSonMapa: true);
        QuitarLinksNoCompartidos(trabajo, advertencias);
        QuitarServidoresAnidados(trabajo);
        var (conservados, eliminados) = Cerrar(trabajo, [], esquemas);
        var etiquetas = QuitarEtiquetas(trabajo["tags"] as JsonArray, EtiquetasUsadas(trabajo), t => Texto(Hijo(t, "name")));
        if (trabajo["tags"] is JsonArray { Count: 0 })
        {
            trabajo.Remove("tags");
        }

        Verificar(trabajo, cabecerasSonMapa: true);
        var resultado = Indice(trabajo, es31);
        if (resultado.Any(e => !ubicaciones.Contains(e.Ubicacion)) || resultado.Count != ubicaciones.Count)
        {
            throw new PodaException(MotivoPoda.VerificacionFallida, "El resultado no tiene exactamente las operaciones seleccionadas.");
        }
        var resumen = new ResumenPoda(
            [.. indice.Where(seleccion.Contains).Select(e => e.Clave)], conservados, eliminados, etiquetas, internos,
            [.. urls.Select(UrlBase)], advertencias);
        return new SpecPodado(TextoSpec.Escribir(trabajo, formato), formato, resumen);
    }

    /// <summary>OpenAPI 3.1 (true) o 3.0 (false); cualquier otra cosa es inválida.</summary>
    private static bool Es31(JsonObject raiz)
    {
        var version = raiz["openapi"] is JsonValue valor
            ? (valor.GetValueKind() == JsonValueKind.String ? valor.GetValue<string>() : valor.ToJsonString())
            : null;
        if (version is not null && Version30().IsMatch(version))
        {
            return false;
        }
        if (version is not null && Version31().IsMatch(version))
        {
            return true;
        }
        throw Invalido("Solo se podan documentos OpenAPI 3.0 y 3.1.");
    }

    [GeneratedRegex(@"^3\.0(?:\.\d+)?$")]
    private static partial Regex Version30();

    [GeneratedRegex(@"^3\.1(?:\.\d+)?$")]
    private static partial Regex Version31();

    /// <summary>El mismo índice que <see cref="AnalizadorSpec"/>: <c>GET /ruta</c> y <c>POST webhook:nombre</c>.</summary>
    internal static List<EntradaIndice> Indice(JsonObject raiz, bool es31)
    {
        var crudas = new List<(string Base, string? OperationId, string Ubicacion, bool Interna)>();
        void Seccion(string seccion)
        {
            if (raiz[seccion] is null)
            {
                return;
            }
            if (raiz[seccion] is not JsonObject mapa)
            {
                throw Invalido($"{seccion} debe ser un objeto.");
            }
            foreach (var (nombre, nodo) in mapa)
            {
                if (nombre.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }
                var item = ItemResuelto(raiz, nodo, out var internoPorRef);
                var interna = EsInterno(nodo) || internoPorRef;
                foreach (var metodo in Metodos)
                {
                    if (item[metodo] is not JsonObject operacion)
                    {
                        continue;
                    }
                    var verbo = metodo.ToUpperInvariant();
                    var clave = seccion == "paths" ? $"{verbo} {nombre}" : $"{verbo} webhook:{nombre}";
                    crudas.Add((clave, AnalizadorSpec.Recortado(Texto(operacion["operationId"])), $"{seccion}|{nombre}|{metodo}", interna || EsInterno(operacion)));
                }
            }
        }
        Seccion("paths");
        if (es31)
        {
            Seccion("webhooks");
        }
        var claves = AnalizadorSpec.ClavesUnicas(crudas.Select(c => (c.Base, c.OperationId)));
        return [.. crudas.Select((c, i) => new EntradaIndice(claves[i], null, c.Ubicacion, c.Interna))];
    }

    /// <summary>El Path Item, o aquel al que apunta su <c>$ref</c> (solo local).</summary>
    private static JsonObject ItemResuelto(JsonObject raiz, JsonNode? nodo, out bool interno)
    {
        interno = false;
        if (nodo is not JsonObject item)
        {
            throw Invalido("Cada ruta debe ser un objeto.");
        }
        if (Texto(item["$ref"]) is not { } referencia)
        {
            return item;
        }
        if (Partes(referencia) is not { } partes)
        {
            throw new PodaException(referencia.StartsWith('#') ? MotivoPoda.ReferenciaRota : MotivoPoda.ReferenciaExterna,
                "Una ruta apunta a un Path Item que no es local.", [referencia]);
        }
        if (!Resolver(raiz, partes, out var destino) || destino is not JsonObject resuelto || Texto(resuelto["$ref"]) is not null)
        {
            throw new PodaException(MotivoPoda.ReferenciaRota, "Una ruta apunta a un Path Item que no existe.", [referencia]);
        }
        interno = EsInterno(resuelto);
        return resuelto;
    }

    private static void PodarSeccion(JsonObject original, JsonObject trabajo, string seccion, HashSet<string> ubicaciones)
    {
        if (trabajo[seccion] is not JsonObject mapa)
        {
            return;
        }
        foreach (var (nombre, nodo) in mapa.ToList())
        {
            if (nombre.StartsWith("x-", StringComparison.Ordinal))
            {
                continue;
            }
            var elegidos = Metodos.Where(m => ubicaciones.Contains($"{seccion}|{nombre}|{m}")).ToHashSet(StringComparer.Ordinal);
            if (elegidos.Count == 0)
            {
                mapa.Remove(nombre);
                continue;
            }
            var item = (JsonObject)nodo!;
            if (Texto(item["$ref"]) is not null)
            {
                // Copia en línea del Path Item referido; los campos hermanos de la $ref (summary, description) mandan.
                var copia = (JsonObject)ItemResuelto(original, original[seccion]![nombre], out _).DeepClone();
                foreach (var (clave, valor) in item)
                {
                    if (clave != "$ref")
                    {
                        copia[clave] = valor?.DeepClone();
                    }
                }
                mapa[nombre] = copia;
                item = copia;
            }
            foreach (var metodo in Metodos.Where(m => !elegidos.Contains(m)))
            {
                item.Remove(metodo);
            }
        }
    }

    /// <summary>Los <c>servers</c> de la raíz pasan a ser solo las URL públicas (en su mismo lugar); sin URL, no hay.</summary>
    private static void Servidores(JsonObject trabajo, List<Uri> urls, string ambiente)
    {
        var posicion = trabajo.IndexOf("servers");
        trabajo.Remove("servers");
        if (urls.Count == 0)
        {
            return;
        }
        var servidores = new JsonArray([.. urls.Select(u => (JsonNode)new JsonObject
        {
            ["url"] = UrlBase(u),
            ["description"] = DescripcionAmbiente(ambiente),
        })]);
        if (posicion < 0)
        {
            posicion = trabajo.IndexOf("info") + 1;
        }
        trabajo.Insert(Math.Min(posicion, trabajo.Count), "servers", servidores);
    }

    /// <summary>Un link (OpenAPI) a una operación que no quedó en el spec revelaría su id o su ruta: se quita.</summary>
    private static void QuitarLinksNoCompartidos(JsonObject trabajo, List<string> advertencias)
    {
        // Las rutas ya quedaron en línea (PodarSeccion): sus operaciones son las compartidas.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seccion in new[] { "paths", "webhooks" })
        {
            foreach (var (_, item) in trabajo[seccion] as JsonObject ?? [])
            {
                foreach (var metodo in Metodos)
                {
                    if (Texto(Hijo(Hijo(item, metodo), "operationId")) is { } id)
                    {
                        ids.Add(id);
                    }
                }
            }
        }
        var pila = new Stack<JsonNode>([trabajo]);
        while (pila.Count > 0)
        {
            switch (pila.Pop())
            {
                case JsonObject objeto:
                    if (objeto["links"] is JsonObject links)
                    {
                        foreach (var (nombre, link) in links.ToList())
                        {
                            if (link is not JsonObject l || Texto(l["$ref"]) is not null)
                            {
                                continue;
                            }
                            var valido = Texto(l["operationId"]) is { } id
                                ? ids.Contains(id)
                                : Texto(l["operationRef"]) is { } r && Partes(r) is { } partes && Resolver(trabajo, partes, out var destino) && destino is not null;
                            if (!valido)
                            {
                                links.Remove(nombre);
                                advertencias.Add($"Se quitó el link «{nombre}»: apunta a una operación que no se comparte.");
                            }
                        }
                    }
                    foreach (var (_, hijo) in objeto)
                    {
                        if (hijo is not null)
                        {
                            pila.Push(hijo);
                        }
                    }
                    break;
                case JsonArray arreglo:
                    foreach (var hijo in arreglo)
                    {
                        if (hijo is not null)
                        {
                            pila.Push(hijo);
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>Nombres en algún <c>tags</c> de operación (arreglo de cadenas), fuera de la lista de la raíz.</summary>
    private static HashSet<string> EtiquetasUsadas(JsonObject trabajo)
    {
        var usadas = new HashSet<string>(StringComparer.Ordinal);
        var pila = new Stack<JsonNode>();
        foreach (var (clave, hijo) in trabajo)
        {
            if (clave != "tags" && hijo is not null)
            {
                pila.Push(hijo);
            }
        }
        while (pila.Count > 0)
        {
            switch (pila.Pop())
            {
                case JsonObject objeto:
                    if (objeto["tags"] is JsonArray lista && lista.All(t => Texto(t) is not null))
                    {
                        usadas.UnionWith(lista.Select(t => Texto(t)!));
                    }
                    foreach (var (_, hijo) in objeto)
                    {
                        if (hijo is not null)
                        {
                            pila.Push(hijo);
                        }
                    }
                    break;
                case JsonArray arreglo:
                    foreach (var hijo in arreglo)
                    {
                        if (hijo is not null)
                        {
                            pila.Push(hijo);
                        }
                    }
                    break;
            }
        }
        return usadas;
    }
}
