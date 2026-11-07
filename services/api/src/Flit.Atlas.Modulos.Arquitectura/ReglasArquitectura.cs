using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Modulos.Arquitectura;

/// <summary>Un elemento leído de <c>likec4 export json</c>.</summary>
internal sealed record ElementoLeido(
    string Id, string Kind, string Titulo, string? Descripcion, string? Tecnologia, IReadOnlyList<string> Etiquetas,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Metadatos, IReadOnlyList<EnlaceElemento> Enlaces)
{
    public string? Metadato(string clave) => Metadatos.TryGetValue(clave, out var v) && v.Count > 0 ? v[0] : null;
}

internal sealed record EnlaceElemento(string Url, string? Titulo);

internal sealed record VistaLeida(string Id, string Tipo, string Categoria, string Titulo);

internal sealed record ModeloLeido(IReadOnlyList<ElementoLeido> Elementos, IReadOnlyList<VistaLeida> Vistas);

/// <summary>Un cambio entre dos publicaciones.</summary>
public sealed record CambioArquitectura(string Tipo, string Id, string? Detalle);

public sealed record DiffArquitectura(IReadOnlyList<CambioArquitectura> Altas, IReadOnlyList<CambioArquitectura> Bajas, IReadOnlyList<CambioArquitectura> Cambios);

/// <summary>
/// Reglas puras del módulo (ATLAS-ADR-0056 §4): qué se acepta de la CI, sin confiar en lo que ella ya revisó. El repo <c>flit</c> es
/// público y el modelo vive ahí, así que un modelo con forma de IP o de secreto se rechaza entero, nunca se limpia a escondidas.
/// </summary>
internal static partial class ReglasArquitectura
{
    public const int MaximoElementos = 5000;
    public const int MaximoVistas = 1000;
    public const int MaximoCambiosDiff = 500;

    private const int LargoTextoMaximo = 200_000;

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,59}$", RegexOptions.CultureInvariant)]
    public static partial Regex FormatoCodigo();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,59}$", RegexOptions.CultureInvariant)]
    public static partial Regex FormatoCluster();

    [GeneratedRegex(@"^arq-v\d{1,5}\.\d{1,5}\.\d{1,5}$", RegexOptions.CultureInvariant)]
    public static partial Regex FormatoVersion();

    [GeneratedRegex(@"^[0-9A-Za-z.+_-]{1,40}$", RegexOptions.CultureInvariant)]
    public static partial Regex FormatoVersionHerramienta();

    /// <summary>Forma de IPv4 con octetos válidos, sin ser parte de un número o versión más larga.</summary>
    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\d.])", RegexOptions.CultureInvariant)]
    private static partial Regex FormaIpv4();

    /// <summary>Lo que se ve como un secreto: JWT, llaves privadas, tokens de GitHub, llaves de AWS y asignaciones de credenciales.</summary>
    [GeneratedRegex(
        @"eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.|-----BEGIN [A-Z ]*PRIVATE KEY-----|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}|\bAKIA[0-9A-Z]{16}\b|\bxox[baprs]-[A-Za-z0-9-]{10,}|\b(?:password|passwd|contrase(?:ñ|n)a|secret|token|api[_-]?key)\s*[=:]\s*\S",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FormaSecreto();

    /// <summary>Host de una red interna: lo que el generador quita y el servidor rechaza si llega.</summary>
    [GeneratedRegex(@"(?:\.svc(?:\.cluster\.local)?|\.cluster\.local|\.local|\.internal|\.intranet|\.lan|\.corp|\.home)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SufijoInterno();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoHost();

    public static string Sha256(string texto) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    public static string VisibilidadTexto(VisibilidadArquitectura v) => v == VisibilidadArquitectura.Restringido ? "restringido" : "interno_ti";

    public static VisibilidadArquitectura? LeerVisibilidad(string? texto) => texto switch
    {
        null or "" or "interno_ti" => VisibilidadArquitectura.InternoTi,
        "restringido" => VisibilidadArquitectura.Restringido,
        _ => null,
    };

    public static string Texto<TEnum>(TEnum valor) where TEnum : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(valor.ToString());

    /// <summary>¿Se ve como una IP o un secreto? <c>null</c> si no; si sí, de qué tipo (nunca el valor: el error llega a los logs de la CI).</summary>
    public static string? FormaSensible(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        if (FormaIpv4().IsMatch(texto))
        {
            return "una dirección IP";
        }
        if (TieneIpv6(texto))
        {
            return "una dirección IP";
        }
        return FormaSecreto().IsMatch(texto) ? "un secreto" : null;
    }

    /// <summary>Solo se revisa lo que podría ser IPv6 completa: la forma <c>a:b:c:d:e:f:g:h</c> o con <c>::</c>; un <c>sha256:…</c> no lo es.</summary>
    private static bool TieneIpv6(string texto)
    {
        foreach (var parte in texto.Split([' ', '\t', '\n', '/', '[', ']', ',', ';', '"', '\''], StringSplitOptions.RemoveEmptyEntries))
        {
            if (parte.Count(c => c == ':') >= 2 && (parte.Contains("::", StringComparison.Ordinal) || parte.Count(c => c == ':') == 7)
                && IPAddress.TryParse(parte, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Un host que puede quedar en un snapshot: público, con dominio y sin forma de IP.</summary>
    public static bool HostPublico(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var sinComodin = host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host;
        return sinComodin.Length <= 253 && FormatoHost().IsMatch(sinComodin) && !SufijoInterno().IsMatch(sinComodin) && !FormaIpv4().IsMatch(sinComodin);
    }

    /// <summary>
    /// Lee y valida la salida de <c>likec4 export json</c>: un solo proyecto, ya con su layout, con topes de tamaño, sin formas sensibles
    /// en ninguna clave ni valor y con enlaces solo https. Devuelve el error para el 422, sin citar el valor.
    /// </summary>
    public static (ModeloLeido? Modelo, string? Codigo, IReadOnlyList<string> Errores) LeerModelo(JsonElement modelo)
    {
        if (modelo.ValueKind != JsonValueKind.Object)
        {
            return (null, "modelo_invalido", ["El modelo debe ser un objeto: la salida de «likec4 export json» de un solo proyecto (usa --project)."]);
        }
        if (!modelo.TryGetProperty("_stage", out var etapa) || etapa.ValueKind != JsonValueKind.String || etapa.GetString() != "layouted")
        {
            return (null, "modelo_invalido", ["El modelo no trae su layout: «_stage» debe ser «layouted» (salida de «likec4 export json»)."]);
        }
        if (!modelo.TryGetProperty("elements", out var elementos) || elementos.ValueKind != JsonValueKind.Object
            || !modelo.TryGetProperty("views", out var vistas) || vistas.ValueKind != JsonValueKind.Object)
        {
            return (null, "modelo_invalido", ["Al modelo le faltan «elements» o «views»."]);
        }

        var sensibles = new List<string>();
        Recorrer(modelo, "$", sensibles);
        if (sensibles.Count > 0)
        {
            return (null, "contenido_sensible", [.. sensibles.Take(20)]);
        }

        var leidos = new List<ElementoLeido>();
        foreach (var propiedad in elementos.EnumerateObject())
        {
            if (propiedad.Value.ValueKind != JsonValueKind.Object)
            {
                return (null, "modelo_invalido", [$"El elemento «{propiedad.Name}» no es un objeto."]);
            }
            leidos.Add(LeerElemento(propiedad.Name, propiedad.Value));
        }
        var vistasLeidas = new List<VistaLeida>();
        foreach (var propiedad in vistas.EnumerateObject())
        {
            if (propiedad.Value.ValueKind != JsonValueKind.Object)
            {
                return (null, "modelo_invalido", [$"La vista «{propiedad.Name}» no es un objeto."]);
            }
            vistasLeidas.Add(LeerVista(propiedad.Name, propiedad.Value));
        }
        var errores = new List<string>();
        if (leidos.Count > MaximoElementos)
        {
            errores.Add($"El modelo pasa de {MaximoElementos} elementos.");
        }
        if (vistasLeidas.Count is 0 or > MaximoVistas)
        {
            errores.Add($"El modelo debe tener entre 1 y {MaximoVistas} vistas.");
        }
        foreach (var elemento in leidos)
        {
            if (elemento.Id.Length > ArquitecturaDbContext.LargoElemento || elemento.Kind.Length > ArquitecturaDbContext.LargoKind
                || elemento.Titulo.Length > ArquitecturaDbContext.LargoTitulo || (elemento.Tecnologia?.Length ?? 0) > ArquitecturaDbContext.LargoTitulo)
            {
                errores.Add($"El elemento «{Recortar(elemento.Id, 60)}» pasa de los largos permitidos.");
            }
            foreach (var enlace in elemento.Enlaces.Where(e => !EnlaceSeguro(e.Url)))
            {
                errores.Add($"El elemento «{Recortar(elemento.Id, 60)}» tiene un enlace que no es https.");
            }
        }
        foreach (var vista in vistasLeidas.Where(v => v.Id.Length > ArquitecturaDbContext.LargoElemento || v.Titulo.Length > ArquitecturaDbContext.LargoTitulo))
        {
            errores.Add($"La vista «{Recortar(vista.Id, 60)}» pasa de los largos permitidos.");
        }
        return errores.Count > 0 ? (null, "modelo_invalido", errores) : (new ModeloLeido(leidos, vistasLeidas), null, []);
    }

    private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];

    private static void Recorrer(JsonElement nodo, string ruta, List<string> sensibles)
    {
        switch (nodo.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var propiedad in nodo.EnumerateObject())
                {
                    var rutaHija = ruta.Length < 120 ? $"{ruta}.{propiedad.Name}" : ruta;
                    Revisar(propiedad.Name, rutaHija, sensibles);
                    Recorrer(propiedad.Value, rutaHija, sensibles);
                }
                break;
            case JsonValueKind.Array:
                foreach (var hijo in nodo.EnumerateArray())
                {
                    Recorrer(hijo, ruta, sensibles);
                }
                break;
            case JsonValueKind.String:
                Revisar(nodo.GetString()!, ruta, sensibles);
                break;
            default:
                break;
        }
    }

    private static void Revisar(string texto, string ruta, List<string> sensibles)
    {
        if (texto.Length > LargoTextoMaximo)
        {
            sensibles.Add($"{Recortar(ruta, 160)}: el texto es demasiado largo.");
            return;
        }
        if (FormaSensible(texto) is { } forma)
        {
            // Nunca el valor: este mensaje queda en los logs del workflow.
            sensibles.Add($"{Recortar(ruta, 160)}: parece {forma}. Los modelos no llevan IP ni secretos (plan 01 §1).");
        }
    }

    private static ElementoLeido LeerElemento(string id, JsonElement e)
    {
        var metadatos = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (e.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in m.EnumerateObject())
            {
                metadatos[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => [p.Value.GetString()!],
                    JsonValueKind.Array => [.. p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)],
                    _ => [],
                };
            }
        }
        var enlaces = new List<EnlaceElemento>();
        if (e.TryGetProperty("links", out var l) && l.ValueKind == JsonValueKind.Array)
        {
            foreach (var enlace in l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                enlaces.Add(new EnlaceElemento(CadenaDe(enlace, "url") ?? string.Empty, CadenaDe(enlace, "title")));
            }
        }
        var etiquetas = e.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
        var descripcion = e.TryGetProperty("description", out var d) ? TextoDe(d) : null;
        return new ElementoLeido(id, CadenaDe(e, "kind") ?? "desconocido", CadenaDe(e, "title") ?? id,
            descripcion is { Length: > ArquitecturaDbContext.LargoDescripcion } ? descripcion[..ArquitecturaDbContext.LargoDescripcion] : descripcion,
            e.TryGetProperty("technology", out var tec) ? TextoDe(tec) : null, etiquetas, metadatos, enlaces);
    }

    private static VistaLeida LeerVista(string id, JsonElement v)
    {
        var tipo = CadenaDe(v, "_type") switch
        {
            "dynamic" => "dinamica",
            "deployment" => "despliegue",
            _ => "estatica",
        };
        return new VistaLeida(id, tipo, Categoria(id, tipo), CadenaDe(v, "title") ?? id);
    }

    /// <summary>La pestaña de la vista, por su tipo y la convención de nombres del plan 01 §5.</summary>
    public static string Categoria(string id, string tipo)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (tipo == "dinamica" || tipo == "despliegue")
        {
            return tipo;
        }
        var minusculas = id.ToLowerInvariant();
        return minusculas switch
        {
            _ when minusculas.Contains("landscape", StringComparison.Ordinal) => "landscape",
            _ when minusculas.Contains("contexto", StringComparison.Ordinal) => "contexto",
            _ when minusculas.Contains("contenedores", StringComparison.Ordinal) => "contenedores",
            _ when minusculas.Contains("componentes", StringComparison.Ordinal) => "componentes",
            _ => "otra",
        };
    }

    private static string? CadenaDe(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>LikeC4 escribe los textos como cadena o como <c>{ txt }</c> / <c>{ md }</c>.</summary>
    private static string? TextoDe(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Object => CadenaDe(v, "txt") ?? CadenaDe(v, "md"),
        _ => null,
    };

    public static bool EnlaceSeguro(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>
    /// El modelo que recibe quien no tiene <c>ver_despliegue</c>: sin las vistas de despliegue ni los nodos de <c>deployments</c>, que
    /// no se pueden deducir de lo demás (ATLAS-ADR-0056 §3). No toca el modelo guardado.
    /// </summary>
    public static string SinDespliegue(string contenido)
    {
        var raiz = JsonNode.Parse(contenido)?.AsObject() ?? throw new InvalidOperationException("Modelo guardado inválido.");
        if (raiz["views"] is JsonObject vistas)
        {
            foreach (var id in vistas.Where(v => v.Value?["_type"]?.GetValue<string>() == "deployment").Select(v => v.Key).ToList())
            {
                vistas.Remove(id);
            }
        }
        if (raiz.ContainsKey("deployments"))
        {
            raiz["deployments"] = new JsonObject { ["elements"] = new JsonObject(), ["relations"] = new JsonObject() };
        }
        return raiz.ToJsonString();
    }

    /// <summary>Diff estructural de dos modelos: elementos, relaciones y vistas (plan 01 §9.3).</summary>
    public static DiffArquitectura DiffModelos(string antes, string despues, bool conDespliegue)
    {
        using var a = JsonDocument.Parse(conDespliegue ? antes : SinDespliegue(antes));
        using var b = JsonDocument.Parse(conDespliegue ? despues : SinDespliegue(despues));
        var altas = new List<CambioArquitectura>();
        var bajas = new List<CambioArquitectura>();
        var cambios = new List<CambioArquitectura>();
        foreach (var (seccion, tipo) in new[] { ("elements", "elemento"), ("relations", "relacion"), ("views", "vista") })
        {
            Comparar(Seccion(a.RootElement, seccion), Seccion(b.RootElement, seccion), tipo, seccion == "views", altas, bajas, cambios);
        }
        if (conDespliegue && a.RootElement.TryGetProperty("deployments", out var da) && b.RootElement.TryGetProperty("deployments", out var db))
        {
            Comparar(Seccion(da, "elements"), Seccion(db, "elements"), "despliegue", false, altas, bajas, cambios);
        }
        return Topar(altas, bajas, cambios);
    }

    private static DiffArquitectura Topar(List<CambioArquitectura> altas, List<CambioArquitectura> bajas, List<CambioArquitectura> cambios) =>
        new([.. altas.Take(MaximoCambiosDiff)], [.. bajas.Take(MaximoCambiosDiff)], [.. cambios.Take(MaximoCambiosDiff)]);

    private static Dictionary<string, JsonElement> Seccion(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var s) && s.ValueKind == JsonValueKind.Object
            ? s.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    private static void Comparar(
        Dictionary<string, JsonElement> antes, Dictionary<string, JsonElement> despues, string tipo, bool soloDefinicion,
        List<CambioArquitectura> altas, List<CambioArquitectura> bajas, List<CambioArquitectura> cambios)
    {
        foreach (var (id, valor) in despues.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!antes.TryGetValue(id, out var anterior))
            {
                altas.Add(new CambioArquitectura(tipo, id, CadenaDe(valor, "title")));
            }
            else if (!Iguales(anterior, valor, soloDefinicion))
            {
                cambios.Add(new CambioArquitectura(tipo, id, CadenaDe(valor, "title")));
            }
        }
        bajas.AddRange(antes.Keys.Except(despues.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => new CambioArquitectura(tipo, id, CadenaDe(antes[id], "title"))));
    }

    /// <summary>En una vista cuenta su definición (<c>hash</c>), no las coordenadas que acomodó el layout.</summary>
    private static bool Iguales(JsonElement a, JsonElement b, bool soloDefinicion)
    {
        if (soloDefinicion && a.TryGetProperty("hash", out var ha) && b.TryGetProperty("hash", out var hb))
        {
            return ha.GetRawText() == hb.GetRawText();
        }
        return JsonElement.DeepEquals(a, b);
    }

    /// <summary>
    /// El aplicativo de la ficha de un elemento (plan 01 §8.4 no define una clave para esto): <c>metadata.aplicativo</c> (slug), luego
    /// <c>repo</c> más <c>repo_path</c> contra el catálogo, luego <c>k8s_workload</c> como slug, luego un <c>repo</c> propio contra el producto de ese
    /// repo y, al final, el aplicativo del proyecto, pero solo si el elemento es de ese repo. Un actor o un sistema externo no tiene ficha: un
    /// enlace equivocado es peor que ninguno.
    /// </summary>
    public static AplicativoLeido? ResolverAplicativo(
        ElementoLeido elemento, string? repoHeredado, string repoDelProyecto, Guid aplicativoDelProyecto, IReadOnlyList<AplicativoLeido> aplicativos)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        ArgumentNullException.ThrowIfNull(aplicativos);
        if (elemento.Metadato("aplicativo") is { } slug && aplicativos.FirstOrDefault(a => a.Slug == slug) is { } porSlug)
        {
            return porSlug;
        }
        var repo = elemento.Metadato("repo") ?? repoHeredado;
        if (repo is not null && elemento.Metadato("repo_path") is { } ruta)
        {
            var limpia = ruta.Trim('/');
            if (aplicativos.FirstOrDefault(a => string.Equals(a.Repo, repo, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.RutaEnRepo?.Trim('/'), limpia, StringComparison.OrdinalIgnoreCase)) is { } porRuta)
            {
                return porRuta;
            }
        }
        if (elemento.Metadato("k8s_workload") is { } carga && aplicativos.FirstOrDefault(a => a.Slug == carga) is { } porCarga)
        {
            return porCarga;
        }
        if (elemento.Metadato("repo") is { } propio
            && aplicativos.FirstOrDefault(a => a.Tipo == "producto" && string.Equals(a.Repo, propio, StringComparison.OrdinalIgnoreCase)) is { } porRepo)
        {
            return porRepo;
        }
        return string.Equals(repo, repoDelProyecto, StringComparison.OrdinalIgnoreCase) ? aplicativos.FirstOrDefault(a => a.Id == aplicativoDelProyecto) : null;
    }

    /// <summary>Código del contrato a partir de su ruta: <c>contracts/openapi/external-api.v1.json</c> → <c>external-api</c>.</summary>
    public static string? CodigoContrato(string ruta)
    {
        ArgumentNullException.ThrowIfNull(ruta);
        var archivo = ruta.Replace('\\', '/').Split('/')[^1];
        var codigo = VersionYExtension().Replace(archivo, string.Empty);
        return FormatoCodigo().IsMatch(codigo) ? codigo : null;
    }

    [GeneratedRegex(@"(\.v\d+)?(\.[a-z0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionYExtension();

    public static string FechaUtc(DateTimeOffset fecha) => fecha.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
