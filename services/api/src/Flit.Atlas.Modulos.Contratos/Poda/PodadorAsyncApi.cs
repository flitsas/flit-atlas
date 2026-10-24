using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;
using static Flit.Atlas.Modulos.Contratos.Poda.NucleoPoda;

namespace Flit.Atlas.Modulos.Contratos.Poda;

/// <summary>
/// Poda de AsyncAPI 2.x y 3.x (plan 00 §8.6 punto 6, ATLAS-ADR-0038), con el mismo recorrido genérico de <c>$ref</c>:
/// <list type="bullet">
/// <item>2.x: la selección es <c>publish canal</c> / <c>subscribe canal</c>; se conservan esos canales con solo las
/// acciones elegidas.</item>
/// <item>3.x: la selección es <c>send canal</c> / <c>receive canal</c> (una operación de <c>operations</c>); los canales
/// son unidades del cierre transitivo (solo quedan los que alcanzan las operaciones) y, de un canal que usan las
/// operaciones elegidas, solo los mensajes que ellas nombran (todos, si alguna no los limita).</item>
/// <item>Los <c>servers</c> pasan a ser los públicos del destino (con la seguridad común de los originales, si la hay); los
/// canales pierden su lista de <c>servers</c> (aplican a todos).</item>
/// <item>Se quita lo interno, los componentes no alcanzados y las etiquetas sin uso, y se verifica el resultado.</item>
/// </list>
/// </summary>
internal sealed partial class PodadorAsyncApi : IPodadorSpec
{
    private const string Canales = "channels";

    private const string SeccionOperaciones = "operations";

    private static readonly string[] Acciones2 = ["publish", "subscribe"];

    public TipoContrato Tipo => TipoContrato.Asyncapi;

    public IReadOnlyList<OperacionPodable> Operaciones(string contenido)
    {
        var raiz = TextoSpec.Leer(contenido, out _);
        return [.. Indice(raiz, Mayor(raiz)).Select(e => new OperacionPodable(e.Clave, e.Interna))];
    }

    public SpecPodado Podar(SolicitudPoda solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var urls = ValidarDestino(solicitud.Destino, soloHttps: false);
        var raiz = TextoSpec.Leer(solicitud.Contenido, out var formato);
        var mayor = Mayor(raiz);
        var indice = Indice(raiz, mayor);
        var seleccion = Seleccionar(indice, solicitud.Claves);
        var ubicaciones = seleccion.Select(e => e.Ubicacion).ToHashSet(StringComparer.Ordinal);
        var esquemas = EsquemasDeSeguridad(raiz);
        var advertencias = new List<string>();
        var seguridad = SeguridadComun(raiz, advertencias);

        var trabajo = (JsonObject)raiz.DeepClone();
        if (mayor == 2)
        {
            PodarCanales2(trabajo, ubicaciones);
        }
        else
        {
            PodarOperaciones3(trabajo, ubicaciones);
            FiltrarMensajes3(trabajo);
            foreach (var mapa in new[] { trabajo[Canales], Hijo(trabajo[Componentes], Canales) })
            {
                foreach (var (_, canal) in mapa as JsonObject ?? [])
                {
                    (canal as JsonObject)?.Remove("servers");
                }
            }
        }
        Servidores(trabajo, urls, solicitud.Destino.Ambiente, mayor, seguridad);
        var internos = QuitarInternos(trabajo);
        ExtensionesPoda.Quitar(trabajo, cabecerasSonMapa: false);

        string[] contenedores = mayor == 3 ? [Canales] : [];
        var (_, eliminados) = Cerrar(trabajo, contenedores, esquemas);
        var listaEtiquetas = mayor == 2 ? trabajo["tags"] as JsonArray : Hijo(trabajo["info"], "tags") as JsonArray;
        var etiquetas = QuitarEtiquetas(listaEtiquetas, EtiquetasUsadas(trabajo, listaEtiquetas), t => NombreEtiqueta(trabajo, t));
        if (listaEtiquetas is { Count: 0 })
        {
            (listaEtiquetas.Parent as JsonObject)?.Remove("tags");
        }
        // Quitar etiquetas de info (AsyncAPI 3) puede dejar huérfanos en components/tags: segundo cierre.
        var (conservados, eliminadosDespues) = Cerrar(trabajo, contenedores, esquemas);
        eliminados.AddRange(eliminadosDespues);

        Verificar(trabajo, cabecerasSonMapa: false);
        var resultado = Indice(trabajo, mayor);
        if (resultado.Any(e => !ubicaciones.Contains(e.Ubicacion)) || resultado.Count != ubicaciones.Count)
        {
            throw new PodaException(MotivoPoda.VerificacionFallida, "El resultado no tiene exactamente las operaciones seleccionadas.");
        }
        var resumen = new ResumenPoda(
            [.. indice.Where(seleccion.Contains).Select(e => e.Clave)], conservados, eliminados, etiquetas, internos,
            [.. urls.Select(u => u.GetLeftPart(UriPartial.Path).TrimEnd('/'))], advertencias);
        return new SpecPodado(TextoSpec.Escribir(trabajo, formato), formato, resumen);
    }

    private static int Mayor(JsonObject raiz)
    {
        var version = Texto(raiz["asyncapi"]);
        if (version is not null && VersionAsyncApi().Match(version) is { Success: true } m)
        {
            return m.Groups[1].Value == "2" ? 2 : 3;
        }
        throw Invalido("Solo se podan documentos AsyncAPI 2.x y 3.x.");
    }

    [GeneratedRegex(@"^([23])\.\d+\.\d+$")]
    private static partial Regex VersionAsyncApi();

    private static string? NoVacio(JsonNode? nodo) => Texto(nodo) is { } t && !string.IsNullOrWhiteSpace(t) ? t : null;

    /// <summary>El mismo índice que <see cref="AnalizadorSpec"/>; además, la clave con el id para las repetidas.</summary>
    internal static List<EntradaIndice> Indice(JsonObject raiz, int mayor)
    {
        var crudas = new List<(string Base, string? OperationId, string Ubicacion, bool Interna)>();
        if (mayor == 2)
        {
            if (raiz[Canales] is not JsonObject canales)
            {
                throw Invalido("channels es obligatorio en AsyncAPI 2.x y debe ser un objeto.");
            }
            foreach (var (direccion, nodo) in canales)
            {
                if (nodo is not JsonObject canal)
                {
                    throw Invalido("Cada canal debe ser un objeto.");
                }
                foreach (var accion in Acciones2)
                {
                    if (canal[accion] is null)
                    {
                        continue;
                    }
                    if (canal[accion] is not JsonObject operacion)
                    {
                        throw Invalido("Cada operación debe ser un objeto.");
                    }
                    crudas.Add(($"{accion} {direccion}", AnalizadorSpec.Recortado(NoVacio(operacion["operationId"])),
                        $"{Canales}|{direccion}|{accion}", EsInterno(canal) || EsInterno(operacion)));
                }
            }
        }
        else if (raiz[SeccionOperaciones] is not null)
        {
            if (raiz[SeccionOperaciones] is not JsonObject operaciones)
            {
                throw Invalido("operations debe ser un objeto.");
            }
            foreach (var (id, nodo) in operaciones)
            {
                if (nodo is not JsonObject operacion)
                {
                    throw Invalido("Cada operación debe ser un objeto.");
                }
                var accion = NoVacio(operacion["action"]);
                var referencia = NoVacio(Hijo(operacion["channel"], "$ref"));
                if (accion is not ("send" or "receive") || referencia is null || !referencia.StartsWith("#/channels/", StringComparison.Ordinal))
                {
                    throw Invalido("Cada operación debe tener action send o receive y una $ref a #/channels/<canal>.");
                }
                var clave = Despuntar(referencia["#/channels/".Length..]);
                var canal = Hijo(raiz[Canales], clave) as JsonObject;
                var direccion = NoVacio(canal?["address"]) ?? clave;
                crudas.Add(($"{accion} {direccion}", AnalizadorSpec.Recortado(id), $"{SeccionOperaciones}|{id}", EsInterno(operacion) || EsInterno(canal)));
            }
        }
        var claves = AnalizadorSpec.ClavesUnicas(crudas.Select(c => (c.Base, c.OperationId)));
        return [.. crudas.Select((c, i) => new EntradaIndice(claves[i], AnalizadorSpec.ClaveConId(c.Base, c.OperationId), c.Ubicacion, c.Interna))];
    }

    private static void PodarCanales2(JsonObject trabajo, HashSet<string> ubicaciones)
    {
        var canales = (JsonObject)trabajo[Canales]!;
        foreach (var (direccion, nodo) in canales.ToList())
        {
            var canal = (JsonObject)nodo!;
            var elegidas = Acciones2.Where(a => ubicaciones.Contains($"{Canales}|{direccion}|{a}")).ToList();
            if (elegidas.Count == 0)
            {
                canales.Remove(direccion);
                continue;
            }
            foreach (var accion in Acciones2.Except(elegidas))
            {
                canal.Remove(accion);
            }
            canal.Remove("servers");
        }
    }

    private static void PodarOperaciones3(JsonObject trabajo, HashSet<string> ubicaciones)
    {
        var operaciones = (JsonObject)trabajo[SeccionOperaciones]!;
        foreach (var (id, _) in operaciones.ToList())
        {
            if (!ubicaciones.Contains($"{SeccionOperaciones}|{id}"))
            {
                operaciones.Remove(id);
            }
        }
    }

    /// <summary>
    /// De cada canal que usan las operaciones elegidas (o sus <c>reply</c> en línea), quita los mensajes que nada fuera de
    /// <c>channels</c> nombra, salvo que alguna de esas operaciones no limite sus mensajes (entonces van todos).
    /// </summary>
    private static void FiltrarMensajes3(JsonObject trabajo)
    {
        var usadosPorOperacion = new HashSet<string>(StringComparer.Ordinal);
        var todos = new HashSet<string>(StringComparer.Ordinal);
        void Usar(JsonNode? canalRef, JsonNode? mensajes)
        {
            if (Partes(NoVacio(Hijo(canalRef, "$ref")) ?? string.Empty) is not [Canales, var canal])
            {
                return;
            }
            usadosPorOperacion.Add(canal);
            if (mensajes is not JsonArray)
            {
                todos.Add(canal);
            }
        }
        foreach (var (_, operacion) in trabajo[SeccionOperaciones] as JsonObject ?? [])
        {
            Usar(Hijo(operacion, "channel"), Hijo(operacion, "messages"));
            if (Hijo(operacion, "reply") is JsonObject respuesta && Texto(respuesta["$ref"]) is null && respuesta["channel"] is not null)
            {
                Usar(respuesta["channel"], respuesta["messages"]);
            }
        }

        // Mensajes de canal nombrados fuera de channels: los de las operaciones elegidas y los de componentes.
        var nombrados = new HashSet<(string, string)>();
        var pila = new Stack<JsonNode>();
        foreach (var (clave, hijo) in trabajo)
        {
            if (clave != Canales && hijo is not null)
            {
                pila.Push(hijo);
            }
        }
        while (pila.Count > 0)
        {
            var nodo = pila.Pop();
            if (nodo is JsonObject objeto)
            {
                if (Partes(Texto(objeto["$ref"]) ?? string.Empty) is [Canales, var canal, "messages", var mensaje, ..])
                {
                    nombrados.Add((canal, mensaje));
                }
                foreach (var (_, hijo) in objeto)
                {
                    if (hijo is not null)
                    {
                        pila.Push(hijo);
                    }
                }
            }
            else if (nodo is JsonArray arreglo)
            {
                foreach (var hijo in arreglo)
                {
                    if (hijo is not null)
                    {
                        pila.Push(hijo);
                    }
                }
            }
        }

        foreach (var canal in usadosPorOperacion.Except(todos))
        {
            if (Hijo(Hijo(trabajo[Canales], canal), "messages") is not JsonObject mensajes)
            {
                continue;
            }
            foreach (var (nombre, _) in mensajes.ToList())
            {
                if (!nombre.StartsWith("x-", StringComparison.Ordinal) && !nombrados.Contains((canal, nombre)))
                {
                    mensajes.Remove(nombre);
                }
            }
        }
    }

    /// <summary>La <c>security</c> de los servidores originales si todos declaran la misma; si difieren, ninguna.</summary>
    private static JsonNode? SeguridadComun(JsonObject raiz, List<string> advertencias)
    {
        if (raiz["servers"] is not JsonObject servidores || servidores.Count == 0)
        {
            return null;
        }
        var seguridades = servidores.Select(s => Hijo(s.Value, "security")).ToList();
        var primera = seguridades[0];
        if (seguridades.All(s => JsonNode.DeepEquals(s, primera)))
        {
            return primera?.DeepClone();
        }
        advertencias.Add("Los servidores del spec declaran seguridad distinta: los servidores públicos quedan sin security.");
        return null;
    }

    /// <summary>
    /// Servidores públicos con nombre del ambiente (<c>pdn</c>, <c>pdn-2</c>…). 3.x: <c>host</c>, <c>protocol</c> y
    /// <c>pathname</c>; 2.x: <c>url</c> (host y ruta) y <c>protocol</c>. El protocolo es el esquema de la URL.
    /// </summary>
    private static void Servidores(JsonObject trabajo, List<Uri> urls, string ambiente, int mayor, JsonNode? seguridad)
    {
        var posicion = trabajo.IndexOf("servers");
        trabajo.Remove("servers");
        if (urls.Count == 0)
        {
            return;
        }
        var servidores = new JsonObject();
        for (var i = 0; i < urls.Count; i++)
        {
            var url = urls[i];
            var ruta = url.AbsolutePath.TrimEnd('/');
            var servidor = mayor == 3
                ? new JsonObject { ["host"] = url.Authority, ["protocol"] = url.Scheme }
                : new JsonObject { ["url"] = url.Authority + ruta, ["protocol"] = url.Scheme };
            if (mayor == 3 && ruta.Length > 0)
            {
                servidor["pathname"] = ruta;
            }
            servidor["description"] = DescripcionAmbiente(ambiente);
            if (seguridad is not null)
            {
                servidor["security"] = seguridad.DeepClone();
            }
            servidores[i == 0 ? ambiente : $"{ambiente}-{i + 1}"] = servidor;
        }
        if (posicion < 0)
        {
            posicion = trabajo.IndexOf("info") + 1;
        }
        trabajo.Insert(Math.Min(posicion, trabajo.Count), "servers", servidores);
    }

    private static string? NombreEtiqueta(JsonObject trabajo, JsonNode? etiqueta)
    {
        if (Texto(Hijo(etiqueta, "$ref")) is { } referencia && Partes(referencia) is { } partes && Resolver(trabajo, partes, out var destino))
        {
            return Texto(Hijo(destino, "name"));
        }
        return Texto(Hijo(etiqueta, "name"));
    }

    /// <summary>Nombres de las etiquetas que usa algo (operaciones, canales, mensajes, traits), fuera de la lista raíz.</summary>
    private static HashSet<string> EtiquetasUsadas(JsonObject trabajo, JsonArray? listaRaiz)
    {
        var usadas = new HashSet<string>(StringComparer.Ordinal);
        var pila = new Stack<JsonNode>([trabajo]);
        while (pila.Count > 0)
        {
            switch (pila.Pop())
            {
                case JsonObject objeto:
                    foreach (var (clave, hijo) in objeto)
                    {
                        if (clave == "tags" && hijo is JsonArray lista && !ReferenceEquals(lista, listaRaiz))
                        {
                            usadas.UnionWith(lista.Select(t => NombreEtiqueta(trabajo, t)).OfType<string>());
                        }
                        // components/tags no cuenta como uso: se conserva solo si alguien la referencia.
                        if (hijo is not null && !(clave == "tags" && ReferenceEquals(objeto, trabajo[Componentes])))
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
