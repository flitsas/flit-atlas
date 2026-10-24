using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Atlas.Modulos.Contratos.Datos;

namespace Flit.Atlas.Modulos.Contratos.Poda;

/// <summary>Una operación del índice y dónde vive en el spec (para podar por identidad, no por texto).</summary>
/// <param name="Clave">Clave de <c>contratos_operacion</c>.</param>
/// <param name="Alterna">Clave con el id (<c>send canal (id)</c>), para que podar lo podado dé lo mismo aunque la repetida ya no esté.</param>
/// <param name="Ubicacion">Identidad estable: <c>paths|/ruta|get</c>, <c>operations|id</c>…</param>
internal sealed record EntradaIndice(string Clave, string? Alterna, string Ubicacion, bool Interna);

/// <summary>
/// Piezas comunes de los dos podadores (ATLAS-ADR-0038): recorrido genérico de <c>$ref</c> en el árbol JSON, cierre
/// transitivo por unidades (<c>components/&lt;tipo&gt;/&lt;nombre&gt;</c> y, en AsyncAPI 3, <c>channels/&lt;nombre&gt;</c>),
/// eliminación de lo interno, validación de la selección y del destino, y la verificación final.
/// </summary>
internal static class NucleoPoda
{
    public const string Componentes = "components";

    // --- Punteros JSON ---

    /// <summary><c>#/components/schemas/A~1B</c> → <c>[components, schemas, A/B]</c>; null si no es local.</summary>
    public static List<string>? Partes(string referencia)
    {
        if (!referencia.StartsWith('#'))
        {
            return null;
        }
        var fragmento = referencia[1..];
        if (fragmento.Length == 0)
        {
            return [];
        }
        if (!fragmento.StartsWith('/'))
        {
            // #ancla de JSON Schema: no se resuelve sin $id; se trata como rota.
            return null;
        }
        return [.. fragmento[1..].Split('/').Select(p => Despuntar(Uri.UnescapeDataString(p)))];
    }

    public static string Puntero(params string[] partes) =>
        "#/" + string.Join('/', partes.Select(p => p.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));

    public static string Despuntar(string parte) => parte.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    public static bool Resolver(JsonNode raiz, IReadOnlyList<string> partes, out JsonNode? destino)
    {
        destino = raiz;
        foreach (var parte in partes)
        {
            switch (destino)
            {
                case JsonObject objeto when objeto.TryGetPropertyValue(parte, out var hijo):
                    destino = hijo;
                    break;
                case JsonArray arreglo when int.TryParse(parte, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < arreglo.Count:
                    destino = arreglo[i];
                    break;
                default:
                    destino = null;
                    return false;
            }
        }
        return true;
    }

    public static string? Texto(JsonNode? nodo) =>
        nodo is JsonValue valor && valor.GetValueKind() == JsonValueKind.String ? valor.GetValue<string>() : null;

    /// <summary>El hijo <paramref name="clave"/> si <paramref name="nodo"/> es un objeto; si no, null (nunca lanza).</summary>
    public static JsonNode? Hijo(JsonNode? nodo, string clave) => nodo is JsonObject objeto && objeto.TryGetPropertyValue(clave, out var hijo) ? hijo : null;

    public static bool Verdadero(JsonNode? nodo) => nodo is JsonValue valor && valor.GetValueKind() == JsonValueKind.True;

    /// <summary>Marcado como interno: <c>x-internal: true</c> o <c>x-interno: true</c>.</summary>
    public static bool EsInterno(JsonNode? nodo) => nodo is JsonObject o && (Verdadero(o["x-internal"]) || Verdadero(o["x-interno"]));

    // --- Selección y destino ---

    /// <summary>Las entradas seleccionadas. Toda clave debe existir y ninguna puede ser interna.</summary>
    public static HashSet<EntradaIndice> Seleccionar(IReadOnlyList<EntradaIndice> indice, IReadOnlyCollection<string> claves)
    {
        ArgumentNullException.ThrowIfNull(claves);
        if (claves.Count == 0)
        {
            throw new PodaException(MotivoPoda.SeleccionVacia, "La selección no tiene operaciones.");
        }
        var porClave = new Dictionary<string, EntradaIndice>(StringComparer.Ordinal);
        foreach (var e in indice)
        {
            porClave.TryAdd(e.Clave, e);
        }
        foreach (var e in indice.Where(e => e.Alterna is not null))
        {
            porClave.TryAdd(e.Alterna!, e);
        }
        var desconocidas = claves.Where(c => c is null || !porClave.ContainsKey(c)).Select(c => c ?? "(nula)").Distinct(StringComparer.Ordinal).ToList();
        if (desconocidas.Count > 0)
        {
            throw new PodaException(MotivoPoda.ClavesDesconocidas,
                string.Create(CultureInfo.InvariantCulture, $"{desconocidas.Count} clave(s) de la selección no están en el spec."), desconocidas);
        }
        var seleccion = claves.Select(c => porClave[c]).ToHashSet();
        var internas = seleccion.Where(e => e.Interna).Select(e => e.Clave).ToList();
        if (internas.Count > 0)
        {
            throw new PodaException(MotivoPoda.OperacionInterna, "La selección incluye operaciones marcadas como internas (x-internal).", internas);
        }
        return seleccion;
    }

    private static readonly string[] SufijosInternos = [".local", ".localhost", ".internal", ".svc", ".lan", ".intranet", ".corp", ".home.arpa"];

    /// <summary>
    /// El ambiente debe ser sandbox, qa o pdn, y cada URL absoluta, sin credenciales, consulta ni fragmento, con un nombre
    /// de host público (nada de IP, <c>localhost</c> ni dominios internos del clúster).
    /// </summary>
    public static List<Uri> ValidarDestino(DestinoPoda destino, bool soloHttps)
    {
        if (destino is null || destino.Ambiente is null || !AmbientesPoda.Todos.Contains(destino.Ambiente))
        {
            throw new PodaException(MotivoPoda.DestinoInvalido, "El ambiente destino debe ser sandbox, qa o pdn.");
        }
        var urls = new List<Uri>();
        foreach (var url in destino.Urls ?? [])
        {
            var host = url?.IsAbsoluteUri == true ? url.IdnHost.TrimEnd('.') : string.Empty;
            var publica = url is { IsAbsoluteUri: true }
                && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0
                && url.HostNameType == UriHostNameType.Dns && !IPAddress.TryParse(host, out _)
                && host.Contains('.', StringComparison.Ordinal)
                && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && !SufijosInternos.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                && (!soloHttps || url.Scheme == Uri.UriSchemeHttps);
            if (!publica)
            {
                throw new PodaException(MotivoPoda.DestinoInvalido,
                    soloHttps ? "Las URL públicas deben ser https con un host público, sin credenciales ni consulta." : "Las URL públicas deben tener un host público, sin credenciales ni consulta.",
                    [url?.ToString() ?? "(nula)"]);
            }
            if (!urls.Contains(url!))
            {
                urls.Add(url!);
            }
        }
        return urls;
    }

    public static string DescripcionAmbiente(string ambiente) => ambiente switch
    {
        AmbientesPoda.Sandbox => "Sandbox (respuestas simuladas)",
        AmbientesPoda.Qa => "Pruebas (qa)",
        _ => "Producción (pdn)",
    };

    /// <summary><c>https://api.ejemplo.com/</c> → <c>https://api.ejemplo.com</c>; conserva la ruta.</summary>
    public static string UrlBase(Uri url) => url.GetLeftPart(UriPartial.Path).TrimEnd('/');

    // --- Lo interno ---

    /// <summary>
    /// Quita todo nodo con <c>x-internal: true</c> o <c>x-interno: true</c> (salvo la raíz). Si el nodo era una propiedad
    /// de un schema, también la quita de su <c>required</c>. Devuelve cuántos quitó.
    /// </summary>
    public static int QuitarInternos(JsonObject raiz)
    {
        var quitados = 0;
        var pila = new Stack<(JsonNode Nodo, JsonObject? Dueno, string? Clave)>();
        pila.Push((raiz, null, null));
        while (pila.Count > 0)
        {
            var (nodo, dueno, claveEnDueno) = pila.Pop();
            switch (nodo)
            {
                case JsonObject objeto:
                    foreach (var (clave, hijo) in objeto.ToList())
                    {
                        if (EsInterno(hijo))
                        {
                            objeto.Remove(clave);
                            quitados++;
                            if (claveEnDueno == "properties" && dueno?["required"] is JsonArray requeridos)
                            {
                                foreach (var r in requeridos.Where(r => Texto(r) == clave).ToList())
                                {
                                    requeridos.Remove(r);
                                }
                                if (requeridos.Count == 0)
                                {
                                    dueno.Remove("required");
                                }
                            }
                        }
                        else if (hijo is not null)
                        {
                            pila.Push((hijo, objeto, clave));
                        }
                    }
                    break;
                case JsonArray arreglo:
                    for (var i = arreglo.Count - 1; i >= 0; i--)
                    {
                        if (EsInterno(arreglo[i]))
                        {
                            arreglo.RemoveAt(i);
                            quitados++;
                        }
                        else if (arreglo[i] is { } hijo)
                        {
                            pila.Push((hijo, null, null));
                        }
                    }
                    break;
            }
        }
        return quitados;
    }

    // --- Cierre transitivo ---

    /// <summary>
    /// Conserva solo las unidades (componentes y, en AsyncAPI 3, canales) alcanzables desde la raíz y quita el resto.
    /// Toda <c>$ref</c> alcanzable debe ser local y resolver; también se siguen los nombres de <c>discriminator.mapping</c>
    /// y los esquemas de seguridad nombrados en <c>security</c>.
    /// </summary>
    /// <param name="contenedores">Claves de la raíz cuyos hijos son unidades: <c>channels</c> en AsyncAPI 3.</param>
    /// <param name="esquemasOriginales">securitySchemes del spec original, para distinguir uno quitado por interno de uno que nunca existió.</param>
    public static (List<string> Conservados, List<string> Eliminados) Cerrar(
        JsonObject raiz, IReadOnlyCollection<string> contenedores, IReadOnlySet<string> esquemasOriginales)
    {
        var marcadas = new HashSet<(string, string)>();
        var pendientes = new Stack<JsonNode>();
        foreach (var (clave, hijo) in raiz)
        {
            if (clave != Componentes && !contenedores.Contains(clave) && hijo is not null)
            {
                pendientes.Push(hijo);
            }
        }

        void Marcar(string contenedor, string nombre, string referencia)
        {
            if (!marcadas.Add((contenedor, nombre)))
            {
                return;
            }
            if (raiz[contenedor] is not JsonObject mapa || !mapa.TryGetPropertyValue(nombre, out var unidad) || unidad is null)
            {
                throw new PodaException(MotivoPoda.ReferenciaRota, "Una referencia alcanzable no apunta a nada del spec podado (o apunta a algo interno).", [referencia]);
            }
            pendientes.Push(unidad);
        }

        void Componente(string tipo, string nombre, string referencia)
        {
            if (!marcadas.Add(($"{Componentes}/{tipo}", nombre)))
            {
                return;
            }
            if (Hijo(raiz[Componentes], tipo) is not JsonObject mapa || !mapa.TryGetPropertyValue(nombre, out var unidad) || unidad is null)
            {
                throw new PodaException(MotivoPoda.ReferenciaRota, "Una referencia alcanzable no apunta a nada del spec podado (o apunta a algo interno).", [referencia]);
            }
            pendientes.Push(unidad);
        }

        void Referencia(string referencia)
        {
            var partes = Partes(referencia);
            if (partes is null)
            {
                throw referencia.StartsWith('#')
                    ? new PodaException(MotivoPoda.ReferenciaRota, "Una referencia alcanzable no es un puntero JSON local.", [referencia])
                    : new PodaException(MotivoPoda.ReferenciaExterna, "Una referencia alcanzable es externa; la poda solo admite referencias locales (#/...).", [referencia]);
            }
            if (partes.Count >= 3 && partes[0] == Componentes)
            {
                Componente(partes[1], partes[2], referencia);
            }
            else if (partes.Count >= 2 && contenedores.Contains(partes[0]))
            {
                Marcar(partes[0], partes[1], referencia);
            }
            if (!Resolver(raiz, partes, out _))
            {
                throw new PodaException(MotivoPoda.ReferenciaRota, "Una referencia alcanzable no apunta a nada del spec podado (o apunta a algo interno o no compartido).", [referencia]);
            }
        }

        void Seguridad(JsonArray requisitos)
        {
            foreach (var requisito in requisitos.OfType<JsonObject>())
            {
                foreach (var (nombre, _) in requisito)
                {
                    if (Hijo(raiz[Componentes], "securitySchemes") is JsonObject esquemas && esquemas.ContainsKey(nombre))
                    {
                        Componente("securitySchemes", nombre, $"security: {nombre}");
                    }
                    else if (esquemasOriginales.Contains(nombre))
                    {
                        throw new PodaException(MotivoPoda.ReferenciaRota, "Una operación usa un esquema de seguridad interno.", [nombre]);
                    }
                }
            }
        }

        // La security global vive en la raíz, que no se recorre como objeto.
        if (raiz["security"] is JsonArray global)
        {
            Seguridad(global);
        }
        while (pendientes.Count > 0)
        {
            switch (pendientes.Pop())
            {
                case JsonObject objeto:
                    if (Texto(objeto["$ref"]) is { } referencia)
                    {
                        Referencia(referencia);
                    }
                    if (Hijo(objeto["discriminator"], "mapping") is JsonObject mapeo)
                    {
                        foreach (var (_, valor) in mapeo)
                        {
                            if (Texto(valor) is not { } destino)
                            {
                                continue;
                            }
                            var esNombre = !destino.StartsWith('#') && Hijo(raiz[Componentes], "schemas") is JsonObject schemas && schemas.ContainsKey(destino);
                            Referencia(esNombre ? Puntero(Componentes, "schemas", destino) : destino);
                        }
                    }
                    if (objeto["security"] is JsonArray requisitos)
                    {
                        Seguridad(requisitos);
                    }
                    foreach (var (_, hijo) in objeto)
                    {
                        if (hijo is not null)
                        {
                            pendientes.Push(hijo);
                        }
                    }
                    break;
                case JsonArray arreglo:
                    foreach (var hijo in arreglo)
                    {
                        if (hijo is not null)
                        {
                            pendientes.Push(hijo);
                        }
                    }
                    break;
            }
        }

        var conservados = new List<string>();
        var eliminados = new List<string>();
        if (raiz[Componentes] is JsonObject componentes)
        {
            foreach (var (tipo, mapa) in componentes.ToList())
            {
                if (mapa is not JsonObject unidades)
                {
                    continue;
                }
                Quitar(unidades, $"{Componentes}/{tipo}", tipo);
                if (unidades.Count == 0)
                {
                    componentes.Remove(tipo);
                }
            }
            if (componentes.Count == 0)
            {
                raiz.Remove(Componentes);
            }
        }
        foreach (var contenedor in contenedores)
        {
            if (raiz[contenedor] is JsonObject unidades)
            {
                Quitar(unidades, contenedor, contenedor);
            }
        }
        return (conservados, eliminados);

        void Quitar(JsonObject unidades, string tipo, string etiqueta)
        {
            foreach (var (nombre, _) in unidades.ToList())
            {
                if (nombre.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }
                if (marcadas.Contains((tipo, nombre)))
                {
                    conservados.Add($"{etiqueta}/{nombre}");
                }
                else
                {
                    unidades.Remove(nombre);
                    eliminados.Add($"{etiqueta}/{nombre}");
                }
            }
        }
    }

    /// <summary>Nombres de los esquemas de seguridad del spec original.</summary>
    public static HashSet<string> EsquemasDeSeguridad(JsonObject raiz) =>
        Hijo(raiz[Componentes], "securitySchemes") is JsonObject esquemas ? [.. esquemas.Select(e => e.Key)] : [];

    // --- Verificación final ---

    /// <summary>
    /// Revisión independiente del resultado antes de devolverlo: toda <c>$ref</c> es local y resuelve y no queda nada
    /// interno. Si algo falla es un error del podador, no del spec: se lanza <see cref="MotivoPoda.VerificacionFallida"/>.
    /// </summary>
    public static void Verificar(JsonObject resultado)
    {
        var pila = new Stack<JsonNode>();
        pila.Push(resultado);
        while (pila.Count > 0)
        {
            var nodo = pila.Pop();
            if (!ReferenceEquals(nodo, resultado) && EsInterno(nodo))
            {
                throw new PodaException(MotivoPoda.VerificacionFallida, "El resultado aún tiene un nodo interno.");
            }
            switch (nodo)
            {
                case JsonObject objeto:
                    if (Texto(objeto["$ref"]) is { } referencia && (Partes(referencia) is not { } partes || !Resolver(resultado, partes, out _)))
                    {
                        throw new PodaException(MotivoPoda.VerificacionFallida, "El resultado tiene una referencia que no resuelve.", [referencia]);
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

    public static PodaException Invalido(string mensaje) => new(MotivoPoda.SpecInvalida, mensaje);

    /// <summary>Quita las etiquetas de la lista raíz que ya nadie usa; devuelve sus nombres.</summary>
    public static List<string> QuitarEtiquetas(JsonArray? etiquetas, IReadOnlySet<string> usadas, Func<JsonNode?, string?> nombre)
    {
        var quitadas = new List<string>();
        if (etiquetas is null)
        {
            return quitadas;
        }
        for (var i = etiquetas.Count - 1; i >= 0; i--)
        {
            if (nombre(etiquetas[i]) is { } n && !usadas.Contains(n))
            {
                etiquetas.RemoveAt(i);
                quitadas.Insert(0, n);
            }
        }
        return quitadas;
    }

    /// <summary>
    /// Quita los <c>servers</c> (arreglo de objetos con <c>url</c>) y el <c>server</c> de un link que no sean los de la raíz:
    /// los de rutas, operaciones y links son direcciones internas que el cliente no debe ver.
    /// </summary>
    public static void QuitarServidoresAnidados(JsonObject raiz)
    {
        var pila = new Stack<JsonNode>();
        foreach (var (clave, hijo) in raiz)
        {
            if (clave != "servers" && hijo is not null)
            {
                pila.Push(hijo);
            }
        }
        while (pila.Count > 0)
        {
            switch (pila.Pop())
            {
                case JsonObject objeto:
                    if (objeto["servers"] is JsonArray servidores && servidores.Count > 0 && servidores.All(s => Texto(Hijo(s, "url")) is not null))
                    {
                        objeto.Remove("servers");
                    }
                    if (objeto["server"] is JsonObject servidor && Texto(servidor["url"]) is not null && (objeto.ContainsKey("operationId") || objeto.ContainsKey("operationRef")))
                    {
                        objeto.Remove("server");
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
}
