using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Contratos.Poda;
using static Flit.Atlas.Modulos.Contratos.Tests.Poda.OraculoPoda;

namespace Flit.Atlas.Modulos.Contratos.Tests.Poda;

/// <summary>
/// F4-SEC-09 (descripciones): la poda no reescribe textos (ATLAS-ADR-0038), así que el ICT enriquecido tiene que salir
/// del generador (<c>atlas_docs.ict</c>, ATLAS-ADR-0037) con <c>description</c>, <c>summary</c> y <c>title</c> en
/// lenguaje funcional. Se poda el contrato real con todas sus operaciones compartibles hacia pdn y se revisa cada texto
/// que lee el cliente. Complementa a <c>services/docs/tests/ict/test_specs_versionados.py</c>, que revisa el spec sin
/// podar con los mismos patrones (<c>atlas_docs/ict/lenguaje.py</c>).
/// </summary>
public class PodaIctDescripcionesTests
{
    private const string Ict = "contracts/ict/core-ict.enriquecido.v1.yaml";

    private static readonly DestinoPoda Pdn = new(AmbientesPoda.Pdn, [new Uri("https://api.ejemplo.com")]);

    private static readonly string[] ClavesTexto = ["description", "summary", "title"];

    /// <summary>Dato del autor (ejemplos, valores): sus textos no son descripciones.</summary>
    private static readonly string[] Datos = ["example", "default", "enum", "const", "value"];

    /// <summary>Los mismos patrones de <c>lenguaje.py</c>. «ICT» y «FLIT» son nombres del producto y sí valen.</summary>
    private static readonly (string Tipo, Regex Patron)[] Patrones =
    [
        ("ruta_repo", new Regex(@"(?i)(?<![\w.:/])(?:services|contracts|src|apps|deploy|docs|tests)/", RegexOptions.CultureInvariant)),
        ("archivo", new Regex(@"\b[\w-]+\.(?:cs|csproj|sln|py|ts|tsx|go|java|json|ya?ml|proto)\b", RegexOptions.CultureInvariant)),
        ("proyecto", new Regex(@"\bFlit\.[A-Z]\w*", RegexOptions.CultureInvariant)),
        ("ticket", new Regex(@"(?i)#\d{3,}\b|\b(?:feature|bug|pbi|task|issue|ticket)\s*#?\s*\d+", RegexOptions.CultureInvariant)),
        ("comando", new Regex(@"\b(?:python3?|uv|dotnet|npm|npx|pnpm|kubectl|helm|git|docker)\s+(?:-m|run|test|build|install|apply|exec)\b", RegexOptions.CultureInvariant)),
        ("servicio_interno", new Regex(@"(?i)\bcore-[a-z]+\b|\bpython-ml\b|\bflit-(?:gitops|atlas|scheduler)\b|\batlas_docs\b|\bBackApi\w*", RegexOptions.CultureInvariant)),
        ("infraestructura", new Regex(@"\b(?:YARP|gRPC|Hangfire|RabbitMQ|ASP\.NET|Kestrel|Argo CD|k3s|Kubernetes)\b", RegexOptions.CultureInvariant)),
        ("clase", new Regex(@"\b[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+(?:Job|Service|Controller|Handler|Endpoints|Input|Dto|Repository|Worker|Query|Command|Policy|Async)\b", RegexOptions.CultureInvariant)),
        ("host_interno", new Regex(@"(?i)\blocalhost\b|\.svc\b|\.cluster\.local\b|\.internal\b", RegexOptions.CultureInvariant)),
        ("ip", new Regex(@"(?<![\d.])\d{1,3}(?:\.\d{1,3}){3}(?!\.?\d)", RegexOptions.CultureInvariant)),
        ("nota_interna", new Regex(@"\b(?:ATLAS-)?ADR-\d+|\[por verificar\]|\bTODO\b|\bFIXME\b|(?i:\bexcel\b)", RegexOptions.CultureInvariant)),
    ];

    [Fact]
    public void El_ict_podado_hacia_pdn_no_nombra_piezas_internas_en_sus_textos()
    {
        var spec = Spec(Ict);
        var compartibles = Podadores.OpenApi.Operaciones(spec).Where(o => !o.Interna).Select(o => o.Clave).ToList();
        Assert.NotEmpty(compartibles);

        var podado = Podadores.OpenApi.Podar(new SolicitudPoda(spec, compartibles, Pdn));
        var textos = Textos(Leer(podado.Contenido)).ToList();

        Assert.Equal(compartibles.Order(StringComparer.Ordinal), podado.Resumen.Operaciones.Order(StringComparer.Ordinal));
        Assert.True(textos.Count > 50, $"el recorrido tiene que ver los textos del spec podado ({textos.Count})");
        var hallazgos = textos
            .SelectMany(t => Patrones.Where(p => p.Patron.IsMatch(t.Texto)).Select(p => $"{t.Ruta} ({p.Tipo}: {p.Patron.Match(t.Texto).Value})"))
            .ToList();
        Assert.Empty(hallazgos);
    }

    [Fact]
    public void El_ict_sin_podar_guarda_la_procedencia_en_extensiones_que_la_poda_quita()
    {
        var spec = Spec(Ict);
        var compartibles = Podadores.OpenApi.Operaciones(spec).Where(o => !o.Interna).Select(o => o.Clave).ToList();

        var podado = Podadores.OpenApi.Podar(new SolicitudPoda(spec, compartibles, Pdn));

        Assert.Contains("descripcion_contrato:", spec, StringComparison.Ordinal);
        Assert.Contains("x-flit-descripcion-contrato", spec, StringComparison.Ordinal);
        Assert.Contains("WebhookNotificationJob", spec, StringComparison.Ordinal);
        Assert.DoesNotContain("x-flit-descripcion-contrato", podado.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("descripcion_contrato", podado.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("WebhookNotificationJob", podado.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("10888", podado.Contenido, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Contrato público (services/core-ict, Feature #10888).")]
    [InlineData("Fuentes: Flit.Ict.Infrastructure/Jobs/WebhookNotificationJob.cs (TryDeliverAsync).")]
    [InlineData("Esquema `IctLoginRequest` de core-ict.")]
    [InlineData("Archivo generado con python -m atlas_docs.ict.")]
    [InlineData("Todas las rutas pasan por el Gateway YARP.")]
    public void Los_patrones_marcan_los_textos_que_tenia_el_contrato(string texto) =>
        Assert.Contains(Patrones, p => p.Patron.IsMatch(texto));

    [Theory]
    [InlineData("API de Integración con Terceros (ICT) de FLIT.")]
    [InlineData("Token propio del cliente de integración (aud=flit-ict, scope ict.transactions.write).")]
    [InlineData("409 `core_api_not_found` si FLIT no encuentra el trámite; responde `application/json`.")]
    [InlineData("Adjunte el documento.pdf del trámite (versión 1.1.0).")]
    public void Los_patrones_no_marcan_el_lenguaje_del_negocio(string texto) =>
        Assert.DoesNotContain(Patrones, p => p.Patron.IsMatch(texto));

    /// <summary>(ruta, texto) de cada <c>description</c>, <c>summary</c> y <c>title</c> y de cada <c>x-enum-descriptions</c>.</summary>
    private static IEnumerable<(string Ruta, string Texto)> Textos(JsonNode? nodo, string ruta = "")
    {
        switch (nodo)
        {
            case JsonObject objeto:
                foreach (var (clave, hijo) in objeto)
                {
                    var sub = ruta + "/" + clave;
                    if (clave == "x-enum-descriptions")
                    {
                        foreach (var texto in Cadenas(hijo))
                        {
                            yield return (sub, texto);
                        }
                        continue;
                    }
                    if (clave.StartsWith("x-", StringComparison.Ordinal) || Datos.Contains(clave) || (clave == "examples" && hijo is JsonArray))
                    {
                        continue;
                    }
                    if (ClavesTexto.Contains(clave) && hijo is JsonValue valor && valor.GetValueKind() == JsonValueKind.String)
                    {
                        yield return (sub, valor.GetValue<string>());
                        continue;
                    }
                    foreach (var texto in Textos(hijo, sub))
                    {
                        yield return texto;
                    }
                }
                break;
            case JsonArray lista:
                for (var i = 0; i < lista.Count; i++)
                {
                    foreach (var texto in Textos(lista[i], ruta + "/" + i.ToString(CultureInfo.InvariantCulture)))
                    {
                        yield return texto;
                    }
                }
                break;
        }
    }

    private static IEnumerable<string> Cadenas(JsonNode? nodo) => nodo switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.String => [v.GetValue<string>()],
        JsonArray a => a.SelectMany(Cadenas),
        JsonObject o => o.Select(p => p.Value).SelectMany(Cadenas),
        _ => [],
    };
}
