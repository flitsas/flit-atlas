using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Contratos.Datos;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Google.Protobuf.Reflection;
using ProtoBuf.Reflection;
using SharpYaml.Serialization;

namespace Flit.Atlas.Modulos.Contratos.Specs;

/// <summary>Una operación, webhook, canal o rpc del índice (<c>contratos_operacion</c>).</summary>
public sealed record OperacionIndexada(
    string Clave, string Clase, string? Metodo, string? Ruta, string? OperationId, string? Resumen, IReadOnlyList<string> Etiquetas, bool Publica);

/// <summary>Lo que se sacó de un spec: si es válido, sus datos y su índice.</summary>
/// <param name="VersionDelSpec"><c>info.version</c> en OpenAPI y AsyncAPI; en proto, la versión del paquete (<c>v1</c>) si la tiene.</param>
public sealed record SpecAnalizado(
    FormatoSpec Formato, string? Titulo, string? Descripcion, string? VersionDelSpec, IReadOnlyList<OperacionIndexada> Operaciones,
    IReadOnlyList<string> Errores, IReadOnlyList<string> Advertencias)
{
    public bool Valido => Errores.Count == 0;
}

/// <summary>
/// Valida un spec y arma su índice (HU 3.2, ATLAS-ADR-0035), sin red ni archivos:
/// <list type="bullet">
/// <item>OpenAPI 3.x (JSON o YAML) con <c>Microsoft.OpenApi</c> 2.x: errores del lector y las reglas por defecto de la
/// librería (campos obligatorios, referencias que no resuelven…).</item>
/// <item>AsyncAPI 2.x y 3.x: no hay una librería madura y libre para .NET, así que se revisa la estructura que Atlas
/// necesita (versión, <c>info</c>, canales, operaciones con su acción y su canal) y que toda <c>$ref</c> local resuelva. El
/// lint completo lo hace vacuum en la CI (HU 3.3, ATLAS-ADR-0010).</item>
/// <item>proto: el parser de protobuf-net. Como se publica un archivo a la vez, un <c>import</c> de otro archivo del repo
/// (y sus tipos) es una advertencia, no un error.</item>
/// </list>
/// </summary>
public static partial class AnalizadorSpec
{
    /// <summary>Tope del spec en bytes UTF-8 (los más grandes de FLIT pesan unos cientos de KB).</summary>
    public const int TamanoMaximo = 5 * 1024 * 1024;

    private const int MaximoMensajes = 50;

    private const int ProfundidadMaxima = 128;

    public static SpecAnalizado Analizar(TipoContrato tipo, string contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        return tipo switch
        {
            TipoContrato.Openapi => OpenApi(contenido),
            TipoContrato.Asyncapi => AsyncApi(contenido),
            _ => Proto(contenido),
        };
    }

    private static FormatoSpec FormatoDe(string contenido) => contenido.AsSpan().TrimStart().StartsWith("{") ? FormatoSpec.Json : FormatoSpec.Yaml;

    private static string? Corto(string? texto, int largo) =>
        string.IsNullOrWhiteSpace(texto) ? null : (texto.Trim() is var t && t.Length > largo ? t[..largo] : t);

    // --- OpenAPI ---

    private static SpecAnalizado OpenApi(string contenido)
    {
        var formato = FormatoDe(contenido);
        var errores = new List<string>();
        var advertencias = new List<string>();
        var ajustes = new OpenApiReaderSettings();
        ajustes.AddYamlReader(new OpenApiYamlReaderSettings { MaxInputByteCount = TamanoMaximo + 1024, MaxDepth = ProfundidadMaxima });
        ReadResult leido;
        try
        {
            leido = OpenApiDocument.Parse(contenido, formato == FormatoSpec.Json ? "json" : "yaml", ajustes);
        }
#pragma warning disable CA1031 // Cualquier fallo del lector es un spec inválido (422), no un error del servidor.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Invalido(formato, $"No se pudo leer el documento: {Corto(ex.Message, 300)}");
        }

        var version = leido.Diagnostic?.SpecificationVersion;
        var documento = leido.Document;
        if (leido.Diagnostic?.Errors.Any(e => e.Message.Contains("Version node not found", StringComparison.Ordinal)) == true)
        {
            return Invalido(formato, "No es un documento OpenAPI (falta el campo openapi).");
        }
        if (documento is null)
        {
            var detalle = string.Join(" ", (leido.Diagnostic?.Errors ?? []).Select(e => Mensaje(e.Pointer, e.Message)));
            return Invalido(formato, Corto($"No se pudo leer el documento: {detalle}", 500)!);
        }
        if (version is not (OpenApiSpecVersion.OpenApi3_0 or OpenApiSpecVersion.OpenApi3_1) && version?.ToString().StartsWith("OpenApi3", StringComparison.Ordinal) != true)
        {
            return Invalido(formato, "Solo se publican documentos OpenAPI 3.x; convierte el Swagger 2.0 a OpenAPI 3.");
        }
        errores.AddRange((leido.Diagnostic?.Errors ?? []).Select(e => Mensaje(e.Pointer, e.Message)));
        advertencias.AddRange((leido.Diagnostic?.Warnings ?? []).Select(e => Mensaje(e.Pointer, e.Message)));
        foreach (var e in documento.Validate(ValidationRuleSet.GetDefaultRuleSet()) ?? [])
        {
            errores.Add(Mensaje(e.Pointer, e.Message));
        }
        // Lo que el lector advierte y la validación rechaza (p. ej. una $ref rota) queda solo como error.
        errores = [.. errores.Distinct(StringComparer.Ordinal)];
        advertencias = [.. advertencias.Distinct(StringComparer.Ordinal).Except(errores, StringComparer.Ordinal)];

        var operaciones = new List<OperacionIndexada>();
        foreach (var (ruta, item) in documento.Paths ?? [])
        {
            foreach (var (metodo, operacion) in item.Operations ?? [])
            {
                operaciones.Add(OperacionHttp($"{metodo.Method.ToUpperInvariant()} {ruta}", "operacion", metodo.Method.ToUpperInvariant(), ruta, operacion));
            }
        }
        foreach (var (nombre, item) in documento.Webhooks ?? new Dictionary<string, IOpenApiPathItem>())
        {
            foreach (var (metodo, operacion) in item.Operations ?? [])
            {
                operaciones.Add(OperacionHttp($"{metodo.Method.ToUpperInvariant()} webhook:{nombre}", "webhook", metodo.Method.ToUpperInvariant(), nombre, operacion));
            }
        }
        return new(formato, Corto(documento.Info?.Title, ContratosDbContext.LargoNombre), Corto(documento.Info?.Description, ContratosDbContext.LargoDescripcion),
            documento.Info?.Version, Unicas(operaciones), Recortar(errores), Recortar(advertencias));
    }

    private static OperacionIndexada OperacionHttp(string clave, string clase, string metodo, string ruta, OpenApiOperation operacion)
    {
        var etiquetas = (operacion.Tags ?? new HashSet<OpenApiTagReference>()).Select(t => t.Name).OfType<string>().ToList();
        var publica = etiquetas.Contains("public", StringComparer.OrdinalIgnoreCase)
            || (operacion.Extensions?.TryGetValue("x-public", out var extension) == true
                && extension is JsonNodeExtension { Node: JsonValue valor } && valor.TryGetValue<bool>(out var esPublica) && esPublica);
        return new OperacionIndexada(clave, clase, metodo, ruta, Corto(operacion.OperationId, ContratosDbContext.LargoResumen),
            Corto(operacion.Summary ?? operacion.Description, ContratosDbContext.LargoResumen), Etiquetas(etiquetas), publica);
    }

    private static string Mensaje(string? puntero, string mensaje) => string.IsNullOrEmpty(puntero) ? mensaje : $"{puntero}: {mensaje}";

    // --- AsyncAPI ---

    private static SpecAnalizado AsyncApi(string contenido)
    {
        var formato = FormatoDe(contenido);
        JsonNode? raiz;
        try
        {
            raiz = formato == FormatoSpec.Json
                ? JsonNode.Parse(contenido, documentOptions: new JsonDocumentOptions { MaxDepth = ProfundidadMaxima })
                : Yaml(contenido);
        }
#pragma warning disable CA1031 // Un documento que no se puede leer es un spec inválido (422).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Invalido(formato, $"No se pudo leer el documento: {Corto(ex.Message, 300)}");
        }
        if (raiz is not JsonObject documento)
        {
            return Invalido(formato, "El documento debe ser un objeto.");
        }

        var errores = new List<string>();
        var advertencias = new List<string>();
        var version = Texto(documento["asyncapi"]);
        var mayor = version is null ? null : VersionAsyncApi().Match(version) is { Success: true } m ? m.Groups[1].Value : null;
        if (mayor is null)
        {
            return Invalido(formato, "No es un documento AsyncAPI 2.x o 3.x (campo asyncapi).");
        }
        var info = documento["info"] as JsonObject;
        var titulo = Texto(info?["title"]);
        var versionInfo = Texto(info?["version"]);
        if (info is null)
        {
            errores.Add("/info: es obligatorio.");
        }
        if (titulo is null)
        {
            errores.Add("/info/title: es obligatorio.");
        }
        if (versionInfo is null)
        {
            errores.Add("/info/version: es obligatorio.");
        }
        RevisarReferencias(documento, documento, "", errores, advertencias);

        var operaciones = mayor == "2"
            ? AsyncApi2(documento, errores)
            : AsyncApi3(documento, errores);
        return new(formato, Corto(titulo, ContratosDbContext.LargoNombre), Corto(Texto(info?["description"]), ContratosDbContext.LargoDescripcion),
            versionInfo, Unicas(operaciones), Recortar(errores), Recortar(advertencias));
    }

    private static JsonNode? Yaml(string contenido)
    {
        var flujo = new YamlStream();
        flujo.Load(new StringReader(contenido));
        if (flujo.Documents.Count != 1)
        {
            throw new FormatException("El YAML debe tener un solo documento.");
        }
        return YamlConverter.ToJsonNode(flujo.Documents[0]);
    }

    private static List<OperacionIndexada> AsyncApi2(JsonObject documento, List<string> errores)
    {
        var operaciones = new List<OperacionIndexada>();
        if (documento["channels"] is not JsonObject canales)
        {
            errores.Add("/channels: es obligatorio en AsyncAPI 2.x y debe ser un objeto.");
            return operaciones;
        }
        foreach (var (direccion, nodo) in canales)
        {
            if (nodo is not JsonObject canal)
            {
                errores.Add($"/channels/{Puntero(direccion)}: debe ser un objeto.");
                continue;
            }
            foreach (var accion in new[] { "publish", "subscribe" })
            {
                if (canal[accion] is null)
                {
                    continue;
                }
                if (canal[accion] is not JsonObject operacion)
                {
                    errores.Add($"/channels/{Puntero(direccion)}/{accion}: debe ser un objeto.");
                    continue;
                }
                var etiquetas = EtiquetasAsync(operacion["tags"]);
                operaciones.Add(new OperacionIndexada($"{accion} {direccion}", "canal", accion, direccion, Corto(Texto(operacion["operationId"]), ContratosDbContext.LargoResumen),
                    Corto(Texto(operacion["summary"]) ?? Texto(operacion["description"]), ContratosDbContext.LargoResumen), etiquetas,
                    EsPublica(operacion, etiquetas) || EsPublica(canal, [])));
            }
        }
        return operaciones;
    }

    private static List<OperacionIndexada> AsyncApi3(JsonObject documento, List<string> errores)
    {
        var operaciones = new List<OperacionIndexada>();
        var canales = documento["channels"] as JsonObject;
        if (documento["channels"] is not null and not JsonObject)
        {
            errores.Add("/channels: debe ser un objeto.");
        }
        if (documento["operations"] is null)
        {
            return operaciones;
        }
        if (documento["operations"] is not JsonObject lista)
        {
            errores.Add("/operations: debe ser un objeto.");
            return operaciones;
        }
        foreach (var (id, nodo) in lista)
        {
            var ruta = $"/operations/{Puntero(id)}";
            if (nodo is not JsonObject operacion)
            {
                errores.Add($"{ruta}: debe ser un objeto.");
                continue;
            }
            var accion = Texto(operacion["action"]);
            if (accion is not ("send" or "receive"))
            {
                errores.Add($"{ruta}/action: debe ser send o receive.");
            }
            var referencia = Texto((operacion["channel"] as JsonObject)?["$ref"]);
            string? direccion = null;
            if (referencia is null || !referencia.StartsWith("#/channels/", StringComparison.Ordinal))
            {
                errores.Add($"{ruta}/channel: debe ser una $ref a #/channels/<canal>.");
            }
            else
            {
                var clave = Despuntar(referencia["#/channels/".Length..]);
                var canal = canales?[clave] as JsonObject;
                direccion = Texto(canal?["address"]) ?? clave;
            }
            var etiquetas = EtiquetasAsync(operacion["tags"]);
            operaciones.Add(new OperacionIndexada($"{accion} {direccion}", "canal", accion, direccion, Corto(id, ContratosDbContext.LargoResumen),
                Corto(Texto(operacion["summary"]) ?? Texto(operacion["description"]), ContratosDbContext.LargoResumen), etiquetas, EsPublica(operacion, etiquetas)));
        }
        return operaciones;
    }

    /// <summary>Toda <c>$ref</c> local (<c>#/...</c>) debe apuntar a un nodo que exista; las externas no se siguen.</summary>
    private static void RevisarReferencias(JsonNode raiz, JsonNode? nodo, string ruta, List<string> errores, List<string> advertencias)
    {
        switch (nodo)
        {
            case JsonObject objeto:
                foreach (var (clave, hijo) in objeto)
                {
                    if (clave == "$ref" && Texto(hijo) is { } referencia)
                    {
                        if (!referencia.StartsWith('#'))
                        {
                            advertencias.Add($"{ruta}/$ref: «{Corto(referencia, 200)}» es externa; Atlas no la sigue.");
                        }
                        else if (!Resuelve(raiz, referencia))
                        {
                            errores.Add($"{ruta}/$ref: «{Corto(referencia, 200)}» no apunta a nada del documento.");
                        }
                        continue;
                    }
                    RevisarReferencias(raiz, hijo, $"{ruta}/{Puntero(clave)}", errores, advertencias);
                }
                break;
            case JsonArray arreglo:
                for (var i = 0; i < arreglo.Count; i++)
                {
                    RevisarReferencias(raiz, arreglo[i], string.Create(CultureInfo.InvariantCulture, $"{ruta}/{i}"), errores, advertencias);
                }
                break;
        }
    }

    private static bool Resuelve(JsonNode raiz, string referencia)
    {
        JsonNode? actual = raiz;
        foreach (var parte in referencia.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var clave = Despuntar(Uri.UnescapeDataString(parte));
            actual = actual switch
            {
                JsonObject o => o[clave],
                JsonArray a when int.TryParse(clave, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < a.Count => a[i],
                _ => null,
            };
            if (actual is null)
            {
                return false;
            }
        }
        return true;
    }

    private static string Puntero(string clave) => clave.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string Despuntar(string parte) => parte.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    private static string? Texto(JsonNode? nodo) =>
        nodo is JsonValue valor && valor.TryGetValue<string>(out var texto) && !string.IsNullOrWhiteSpace(texto) ? texto : null;

    private static List<string> EtiquetasAsync(JsonNode? nodo) =>
        Etiquetas((nodo as JsonArray ?? []).Select(t => Texto((t as JsonObject)?["name"])).OfType<string>());

    private static bool EsPublica(JsonObject objeto, IReadOnlyList<string> etiquetas) =>
        etiquetas.Contains("public", StringComparer.OrdinalIgnoreCase)
        || (objeto["x-public"] is JsonValue valor && valor.TryGetValue<bool>(out var publica) && publica);

    [GeneratedRegex(@"^([23])\.\d+\.\d+$")]
    private static partial Regex VersionAsyncApi();

    // --- proto ---

    private static SpecAnalizado Proto(string contenido)
    {
        var errores = new List<string>();
        var advertencias = new List<string>();
        var conjunto = new FileDescriptorSet { AllowNameOnlyImport = true };
        try
        {
            conjunto.Add("contrato.proto", includeInOutput: true, new StringReader(contenido));
            conjunto.Process();
        }
#pragma warning disable CA1031 // Un .proto que el parser no puede procesar es un spec inválido (422).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Invalido(FormatoSpec.Proto, $"No se pudo leer el .proto: {Corto(ex.Message, 300)}");
        }
        foreach (var e in conjunto.GetErrors())
        {
            var texto = string.Create(CultureInfo.InvariantCulture, $"línea {e.LineNumber}, columna {e.ColumnNumber}: {e.Message}");
            // Se publica un archivo a la vez: los import de otros archivos del repo no se pueden resolver aquí.
            var deOtroArchivo = e.Message.StartsWith("unable to find", StringComparison.Ordinal)
                || e.Message.StartsWith("type not found", StringComparison.Ordinal);
            (e.IsError && !deOtroArchivo ? errores : advertencias).Add(texto);
        }
        var archivo = conjunto.Files.FirstOrDefault(f => f.IncludeInOutput);
        var paquete = string.IsNullOrWhiteSpace(archivo?.Package) ? null : archivo.Package;
        var operaciones = new List<OperacionIndexada>();
        foreach (var servicio in archivo?.Services ?? [])
        {
            var nombreServicio = paquete is null ? servicio.Name : $"{paquete}.{servicio.Name}";
            foreach (var metodo in servicio.Methods)
            {
                var entrada = (metodo.ClientStreaming ? "stream " : string.Empty) + metodo.InputType.TrimStart('.');
                var salida = (metodo.ServerStreaming ? "stream " : string.Empty) + metodo.OutputType.TrimStart('.');
                operaciones.Add(new OperacionIndexada($"rpc:{nombreServicio}/{metodo.Name}", "rpc", "rpc", nombreServicio, metodo.Name,
                    Corto($"{entrada} → {salida}", ContratosDbContext.LargoResumen), [servicio.Name], false));
            }
        }
        if (errores.Count == 0 && archivo is null)
        {
            errores.Add("El archivo no tiene contenido proto.");
        }
        var versionPaquete = paquete is null ? null : VersionPaquete().Match(paquete) is { Success: true } m ? m.Groups[1].Value : null;
        return new(FormatoSpec.Proto, paquete ?? "contrato.proto", null, versionPaquete, Unicas(operaciones), Recortar(errores), Recortar(advertencias));
    }

    /// <summary><c>flit.tramites.v1</c> → <c>1</c>.</summary>
    [GeneratedRegex(@"\.v(\d{1,6})(?:[a-z]+\d*)?$")]
    private static partial Regex VersionPaquete();

    // --- Comunes ---

    private static SpecAnalizado Invalido(FormatoSpec formato, string error) => new(formato, null, null, null, [], [error], []);

    private static List<string> Etiquetas(IEnumerable<string> etiquetas) =>
        [.. etiquetas.Select(e => e.Trim()).Where(e => e.Length > 0).Select(e => e.Length > ContratosDbContext.LargoEtiqueta ? e[..ContratosDbContext.LargoEtiqueta] : e)
            .Distinct(StringComparer.Ordinal).Take(20)];

    /// <summary>Claves únicas y que caben en la columna: una repetida (dos operaciones send al mismo canal) lleva su id.</summary>
    private static List<OperacionIndexada> Unicas(List<OperacionIndexada> operaciones)
    {
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        var resultado = new List<OperacionIndexada>(operaciones.Count);
        foreach (var o in operaciones)
        {
            var clave = Limitar(o.Clave);
            if (!vistas.Add(clave))
            {
                var alterna = Limitar($"{o.Clave} ({o.OperationId})");
                for (var n = 2; !vistas.Add(alterna); n++)
                {
                    alterna = Limitar(string.Create(CultureInfo.InvariantCulture, $"{o.Clave} #{n}"));
                }
                clave = alterna;
            }
            resultado.Add(o with
            {
                Clave = clave,
                Ruta = o.Ruta is { Length: > ContratosDbContext.LargoRuta } r ? r[..ContratosDbContext.LargoRuta] : o.Ruta,
            });
        }
        return resultado;
    }

    private static string Limitar(string texto) => texto.Length <= ContratosDbContext.LargoClave ? texto : texto[..ContratosDbContext.LargoClave];

    private static List<string> Recortar(List<string> mensajes) =>
        mensajes.Count <= MaximoMensajes ? mensajes : [.. mensajes.Take(MaximoMensajes), $"… y {mensajes.Count - MaximoMensajes} más."];

    /// <summary>SHA-256 en hexadecimal (minúsculas) de los bytes UTF-8 del texto.</summary>
    public static string Sha256(string contenido) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(contenido)));
}
