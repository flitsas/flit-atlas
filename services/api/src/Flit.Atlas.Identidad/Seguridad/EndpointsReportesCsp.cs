using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>
/// Informes de violaciones de la Content-Security-Policy (HU 4.10, ATLAS-ADR-0048): <c>POST /api/v1/plataforma/csp-reportes</c>,
/// adonde apuntan <c>report-uri</c> y <c>report-to</c> de la PWA, del visor de contratos y de la API. El navegador los
/// manda sin la cabecera antifalsificación y, desde el visor (origen opaco), con <c>Origin: null</c>: por eso no exige
/// sesión ni esa cabecera. Solo escribe en el log estructurado (Loki); no va a la base ni a la auditoría.
/// <para>
/// Sin datos personales (prompt maestro §13): del informe quedan la directiva, el origen de lo bloqueado (esquema y host,
/// o la palabra clave: <c>inline</c>, <c>eval</c>, <c>trusted-types-sink</c>…), la ruta del documento y la de la fuente
/// sin consulta ni fragmento, la línea, la columna y si la política bloquea o solo informa. Nunca la muestra del código
/// (<c>script-sample</c>), el referer, el navegador ni la IP. Tiene tope de cuerpo, de informes por petición y límites
/// de tasa por IP y en total (<see cref="OpcionesLimiteTasa.ReportesCspPorIp"/>, <see cref="OpcionesLimiteTasa.ReportesCspTotal"/>).
/// </para>
/// </summary>
public static partial class EndpointsReportesCsp
{
    public const string Ruta = "/api/v1/plataforma/csp-reportes";

    /// <summary>Nombre del endpoint de <c>Reporting-Endpoints</c> que usa <c>report-to</c>.</summary>
    public const string GrupoReportes = "csp";

    /// <summary>Tope del cuerpo en bytes: un informe pesa menos de 2 KB; el Reporting API puede juntar varios.</summary>
    public const int TamanoMaximo = 16 * 1024;

    /// <summary>Informes que se registran por petición (el resto se descarta).</summary>
    public const int InformesMaximos = 10;

    private const int LargoRuta = 200;

    private static readonly HashSet<string> TiposAceptados = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/csp-report", "application/reports+json", "application/json",
    };

    /// <summary>Lo que el navegador pone en <c>blocked-uri</c> cuando no es una URL.</summary>
    private static readonly HashSet<string> PalabrasClave = new(StringComparer.Ordinal)
    {
        "inline", "eval", "wasm-eval", "self", "data", "blob", "trusted-types-policy", "trusted-types-sink",
    };

    [Description("Informe de una violación de la Content-Security-Policy con el formato de report-uri (application/csp-report). Con report-to llega, en cambio, un arreglo del Reporting API (application/reports+json) de elementos {type: csp-violation, body} con los mismos datos en camelCase; también se acepta.")]
    public sealed record InformeCsp(
        [property: JsonPropertyName("csp-report"), Description("La violación.")] DetalleInformeCsp? Informe);

    [Description("Datos de la violación tal como los manda el navegador. El servidor solo registra la directiva, el origen de lo bloqueado, las rutas sin consulta, la línea y la columna.")]
    public sealed record DetalleInformeCsp(
        [property: JsonPropertyName("document-uri"), Description("Documento donde ocurrió; se registra solo su ruta.")] string? DocumentoUri,
        [property: JsonPropertyName("violated-directive"), Description("Directiva violada (navegadores antiguos).")] string? DirectivaViolada,
        [property: JsonPropertyName("effective-directive"), Description("Directiva que se aplicó (script-src-elem, style-src-attr…).")] string? DirectivaEfectiva,
        [property: JsonPropertyName("blocked-uri"), Description("Lo bloqueado: una URL (se registra solo su origen) o una palabra clave (inline, eval…).")] string? Bloqueado,
        [property: JsonPropertyName("source-file"), Description("Archivo que hizo la petición o tiene el código; se registra sin consulta.")] string? Fuente,
        [property: JsonPropertyName("line-number"), Description("Línea en la fuente.")] long? Linea,
        [property: JsonPropertyName("column-number"), Description("Columna en la fuente.")] long? Columna,
        [property: JsonPropertyName("disposition"), Description("enforce (la política bloqueó) o report (solo informa, Content-Security-Policy-Report-Only).")] string? Disposicion);

    /// <summary>Una violación ya normalizada: lo único que llega al log.</summary>
    internal sealed record ViolacionCsp(string Directiva, string Bloqueado, string? Documento, string? Fuente, long Linea, long Columna, bool SoloInforme);

    private sealed class LimiteCuerpo : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => TamanoMaximo;
    }

    internal static void MapReportesCsp(this IEndpointRouteBuilder rutas)
    {
        rutas.MapPost(Ruta, Registrar)
            .AllowAnonymous()
            .WithMetadata(new MetadatoLimiteTasa(), new LimiteCuerpo())
            .Accepts<InformeCsp>("application/csp-report", "application/reports+json", "application/json")
            .WithName("ReportarViolacionCsp")
            .WithTags("Plataforma")
            .WithSummary("Informar una violación de la CSP")
            .WithDescription(
                "Destino de report-uri y report-to de la Content-Security-Policy de la PWA, del visor de contratos y de la API " +
                "(ATLAS-ADR-0048). Lo llama el navegador, no la PWA: no requiere sesión ni la cabecera antifalsificación. Acepta " +
                "el formato de report-uri (application/csp-report) y el del Reporting API (application/reports+json, un arreglo; " +
                $"solo se registran los primeros {InformesMaximos}). Escribe cada violación en el log estructurado sin datos " +
                "personales: directiva, origen de lo bloqueado, rutas sin consulta, línea y columna (advertencia si la política " +
                "bloqueó; informativo si solo informa). Cuerpo de máximo " + $"{TamanoMaximo / 1024} KB y límites de tasa por IP y en total.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async Task<IResult> Registrar(HttpContext http, ILoggerFactory logs, CancellationToken ct)
    {
        var tipo = http.Request.ContentType?.Split(';')[0].Trim();
        if (tipo is null || !TiposAceptados.Contains(tipo))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }
        if (http.Request.ContentLength > TamanoMaximo)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Lectura acotada: aunque no venga Content-Length (chunked), nunca se lee más del tope.
        var bytes = new byte[TamanoMaximo + 1];
        var leidos = 0;
        int n;
        while (leidos < bytes.Length && (n = await http.Request.Body.ReadAsync(bytes.AsMemory(leidos), ct)) > 0)
        {
            leidos += n;
        }
        if (leidos > TamanoMaximo)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        IReadOnlyList<ViolacionCsp> violaciones;
        try
        {
            violaciones = Leer(bytes.AsMemory(0, leidos));
        }
        catch (JsonException)
        {
            violaciones = [];
        }
        if (violaciones.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["informe"] = ["El cuerpo debe ser un informe de CSP en JSON."] });
        }

        var log = logs.CreateLogger("Flit.Atlas.Csp");
        foreach (var v in violaciones)
        {
            if (v.SoloInforme)
            {
                ViolacionInformada(log, v.Directiva, v.Bloqueado, v.Documento, v.Fuente, v.Linea, v.Columna);
            }
            else
            {
                ViolacionBloqueada(log, v.Directiva, v.Bloqueado, v.Documento, v.Fuente, v.Linea, v.Columna);
            }
        }
        return Results.NoContent();
    }

    /// <summary>Lee un informe de report-uri o un arreglo del Reporting API y lo normaliza (a lo sumo <see cref="InformesMaximos"/>).</summary>
    internal static IReadOnlyList<ViolacionCsp> Leer(ReadOnlyMemory<byte> cuerpo)
    {
        using var documento = JsonDocument.Parse(cuerpo, new JsonDocumentOptions { MaxDepth = 8 });
        var raiz = documento.RootElement;
        var resultado = new List<ViolacionCsp>();
        if (raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("csp-report", out var informe) && informe.ValueKind == JsonValueKind.Object)
        {
            resultado.Add(Normalizar(informe, "document-uri", "effective-directive", "violated-directive", "blocked-uri", "source-file", "line-number", "column-number"));
        }
        else if (raiz.ValueKind == JsonValueKind.Array)
        {
            foreach (var elemento in raiz.EnumerateArray())
            {
                if (resultado.Count >= InformesMaximos)
                {
                    break;
                }
                if (elemento.ValueKind == JsonValueKind.Object
                    && Texto(elemento, "type") == "csp-violation"
                    && elemento.TryGetProperty("body", out var cuerpoInforme) && cuerpoInforme.ValueKind == JsonValueKind.Object)
                {
                    resultado.Add(Normalizar(cuerpoInforme, "documentURL", "effectiveDirective", null, "blockedURL", "sourceFile", "lineNumber", "columnNumber"));
                }
            }
        }
        return resultado;
    }

    private static ViolacionCsp Normalizar(
        JsonElement e, string documento, string efectiva, string? violada, string bloqueado, string fuente, string linea, string columna)
    {
        var directiva = Texto(e, efectiva) ?? (violada is null ? null : Texto(e, violada)?.Split(' ')[0]);
        return new ViolacionCsp(
            directiva is not null && Directiva().IsMatch(directiva) ? directiva : "desconocida",
            Bloqueado(Texto(e, bloqueado)),
            RutaDe(Texto(e, documento)),
            Fuente(Texto(e, fuente)),
            Numero(e, linea),
            Numero(e, columna),
            string.Equals(Texto(e, "disposition"), "report", StringComparison.Ordinal));
    }

    /// <summary>Una URL queda en su origen (esquema y host); una palabra clave, tal cual; lo demás, «otro».</summary>
    internal static string Bloqueado(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return "ninguno";
        }
        if (PalabrasClave.Contains(valor))
        {
            return valor;
        }
        if (Uri.TryCreate(valor, UriKind.Absolute, out var url))
        {
            return url.Scheme is "http" or "https" or "ws" or "wss" ? url.GetLeftPart(UriPartial.Authority) : url.Scheme is "data" or "blob" ? url.Scheme : "otro";
        }
        return "otro";
    }

    /// <summary>La ruta de una URL, sin esquema, host, consulta ni fragmento (con los tachados de los errores de la PWA).</summary>
    internal static string? RutaDe(string? valor) =>
        Uri.TryCreate(valor, UriKind.Absolute, out var url) && url.Scheme is "http" or "https"
            ? EndpointsErroresCliente.LimpiarRuta(url.AbsolutePath) is { } ruta && ruta.Length <= LargoRuta ? ruta : null
            : null;

    /// <summary>El origen y la ruta de la fuente, sin consulta; una palabra clave, tal cual.</summary>
    internal static string? Fuente(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }
        if (PalabrasClave.Contains(valor))
        {
            return valor;
        }
        return Uri.TryCreate(valor, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && RutaDe(valor) is { } ruta
            ? url.GetLeftPart(UriPartial.Authority) + ruta
            : "otro";
    }

    private static string? Texto(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static long Numero(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.Number && valor.TryGetInt64(out var n)
            ? Math.Clamp(n, 0, 10_000_000)
            : 0;

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning,
        Message = "Violación de CSP (bloqueada): {Directiva} bloqueó {Bloqueado} en {Documento} (fuente {Fuente}:{Linea}:{Columna})")]
    private static partial void ViolacionBloqueada(ILogger log, string directiva, string bloqueado, string? documento, string? fuente, long linea, long columna);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information,
        Message = "Violación de CSP (solo informe): {Directiva} informó {Bloqueado} en {Documento} (fuente {Fuente}:{Linea}:{Columna})")]
    private static partial void ViolacionInformada(ILogger log, string directiva, string bloqueado, string? documento, string? fuente, long linea, long columna);

    [GeneratedRegex("^[a-z][a-z-]{0,39}$")]
    private static partial Regex Directiva();
}
