using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Poda;
using Flit.Atlas.Modulos.Contratos.Specs;
using static Flit.Atlas.Modulos.Contratos.Tests.Poda.OraculoPoda;

namespace Flit.Atlas.Modulos.Contratos.Tests.Poda;

/// <summary>
/// Criterio de aceptación de la HU 4.2 (ATLAS-ADR-0038): pruebas de propiedades de la poda sobre specs reales, con
/// selecciones y destinos generados con semilla fija (reproducibles; la semilla sale en el mensaje de cada fallo).
/// Specs: los propios de Atlas (OpenAPI, AsyncAPI 3 y el ICT enriquecido) y copias de FLIT 2 (external-api, core-api y
/// dos AsyncAPI 2.6) en <c>Poda/Specs/</c>, revisadas para que no traigan secretos, IPs internas ni datos personales.
/// </summary>
public class PodaPropiedadesTests
{
    private const string Fixtures = "services/api/tests/Flit.Atlas.Modulos.Contratos.Tests/Poda/Specs/";

    /// <summary>Semilla base; cada spec la combina con su ruta.</summary>
    private const int Semilla = 4_202;

    private static readonly string[] UrlsHttp = ["https://api.ejemplo.com", "https://sandbox.ejemplo.com/ict", "https://qa.api.ejemplo.com/"];

    private static readonly string[] UrlsEventos = ["amqps://broker.ejemplo.com:5671", "wss://eventos.ejemplo.com/atlas"];

    public static TheoryData<string, TipoContrato> Specs => new()
    {
        { "contracts/openapi/atlas.v1.yaml", TipoContrato.Openapi },
        { "contracts/ict/core-ict.enriquecido.v1.yaml", TipoContrato.Openapi },
        { Fixtures + "external-api.v1.json", TipoContrato.Openapi },
        { Fixtures + "core-api.v1.yaml", TipoContrato.Openapi },
        { "contracts/asyncapi/atlas-events.v1.yaml", TipoContrato.Asyncapi },
        { Fixtures + "domain-events.v1.yaml", TipoContrato.Asyncapi },
    };

    [Theory]
    [MemberData(nameof(Specs))]
    public void El_indice_del_podador_es_el_mismo_de_contratos_operacion(string ruta, TipoContrato tipo)
    {
        var contenido = Spec(ruta);

        var claves = Podadores.Para(tipo)!.Operaciones(contenido).Select(o => o.Clave);

        Assert.Equal(ClavesAnalizador(tipo, contenido, ErroresAnalizador(tipo, contenido)).Order(StringComparer.Ordinal), claves.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Specs))]
    public void Toda_poda_cumple_las_propiedades(string ruta, TipoContrato tipo)
    {
        var contenido = Spec(ruta);
        var podador = Podadores.Para(tipo)!;
        var original = Leer(contenido);
        var compartibles = podador.Operaciones(contenido).Where(o => !o.Interna).Select(o => o.Clave).ToList();
        Assert.NotEmpty(compartibles);
        var etiquetas = AnalizadorSpec.Analizar(tipo, contenido).Operaciones.Where(o => compartibles.Contains(o.Clave)).ToList();
        var formato = TextoSpec.Formato(contenido);
        var errores = ErroresAnalizador(tipo, contenido);
        var semilla = Semilla ^ Huella(ruta);
        var azar = new Random(semilla);
        var iteraciones = contenido.Length > 400_000 ? 12 : 40;

        for (var i = 0; i < iteraciones; i++)
        {
            var seleccion = Seleccion(azar, i, compartibles, etiquetas);
            var destino = Destino(azar, tipo);
            var contexto = $"semilla {semilla}, iteración {i}, {seleccion.Count} claves";

            var podado = podador.Podar(new SolicitudPoda(contenido, seleccion, destino));

            Revisar(tipo, original, formato, errores, podado, seleccion, destino, contexto);
            if (i == 0)
            {
                Assert.Equal(compartibles.Order(StringComparer.Ordinal), podado.Resumen.Operaciones.Order(StringComparer.Ordinal));
            }
            var otraVez = podador.Podar(new SolicitudPoda(podado.Contenido, seleccion, destino));
            Assert.True(podado.Contenido == otraVez.Contenido, $"No es idempotente ({contexto}).");
        }
    }

    /// <summary>La primera es la selección completa, luego una sola, luego por etiqueta y luego al azar.</summary>
    private static List<string> Seleccion(Random azar, int i, List<string> compartibles, List<OperacionIndexada> operaciones)
    {
        if (i == 0)
        {
            return [.. compartibles];
        }
        if (i == 1)
        {
            return [compartibles[azar.Next(compartibles.Count)]];
        }
        if (i % 4 == 2 && operaciones.SelectMany(o => o.Etiquetas).Distinct().ToList() is { Count: > 0 } todas)
        {
            var etiqueta = todas[azar.Next(todas.Count)];
            return [.. operaciones.Where(o => o.Etiquetas.Contains(etiqueta)).Select(o => o.Clave)];
        }
        var tope = i % 5 == 0 ? compartibles.Count : Math.Min(compartibles.Count, 8);
        var cantidad = azar.Next(1, tope + 1);
        return [.. compartibles.OrderBy(_ => azar.Next()).Take(cantidad)];
    }

    private static DestinoPoda Destino(Random azar, TipoContrato tipo)
    {
        var ambientes = new[] { AmbientesPoda.Sandbox, AmbientesPoda.Qa, AmbientesPoda.Pdn };
        var pool = tipo == TipoContrato.Openapi ? UrlsHttp : UrlsEventos;
        var urls = pool.OrderBy(_ => azar.Next()).Take(azar.Next(0, pool.Length + 1)).Select(u => new Uri(u)).ToList();
        return new DestinoPoda(ambientes[azar.Next(ambientes.Length)], urls);
    }

    private static void Revisar(TipoContrato tipo, JsonObject original, FormatoSpec formato, IReadOnlyList<string> errores, SpecPodado podado, List<string> seleccion, DestinoPoda destino, string contexto)
    {
        var resultado = Leer(podado.Contenido);

        // Ninguna operación fuera de la selección (según el índice de AnalizadorSpec, que también exige que sea válido).
        var claves = ClavesAnalizador(tipo, podado.Contenido, errores);
        Assert.True(claves.Count == seleccion.Distinct().Count(), $"Operaciones: {claves.Count} de {seleccion.Distinct().Count()} ({contexto}).");
        Assert.All(claves, c => Assert.True(seleccion.Contains(c) || seleccion.Any(s => s.StartsWith(c + " (", StringComparison.Ordinal)), $"«{c}» no estaba en la selección ({contexto})."));

        Assert.True(ReferenciasRotas(resultado) is { Count: 0 } rotas, $"$ref rotas ({contexto}): {string.Join(", ", ReferenciasRotas(resultado))}");
        Assert.True(EsquemasFaltantes(resultado) is { Count: 0 }, $"security sin esquema ({contexto}): {string.Join(", ", EsquemasFaltantes(resultado))}");
        var huerfanos = Huerfanos(resultado, tipo == TipoContrato.Asyncapi && Texto(resultado["asyncapi"])!.StartsWith('3'));
        Assert.True(huerfanos.Count == 0, $"Componentes huérfanos ({contexto}): {string.Join(", ", huerfanos)}");
        Assert.True(Internos(resultado) is { Count: 0 }, $"Nodos internos ({contexto}): {string.Join(", ", Internos(resultado))}");

        // Servidores: solo los públicos dados (y ninguno anidado en rutas u operaciones).
        var esperadas = destino.Urls.Select(u => u.GetLeftPart(UriPartial.Path).TrimEnd('/')).ToList();
        if (tipo == TipoContrato.Openapi)
        {
            var urls = (resultado["servers"] as JsonArray ?? []).Select(s => Texto(s!["url"]) ?? "(sin url)").ToList();
            Assert.Equal(esperadas, urls);
            Assert.True(esperadas.Count > 0 || !resultado.ContainsKey("servers"));
            foreach (var seccion in new[] { "paths", "webhooks", "components" })
            {
                Recorrer(resultado[seccion], (o, ruta) => Assert.False(o["servers"] is JsonArray s && s.All(x => x is JsonObject xo && xo.ContainsKey("url")), $"servers anidado en {seccion}{ruta} ({contexto})."));
            }
            var usadas = new HashSet<string>(StringComparer.Ordinal);
            Recorrer(resultado["paths"], (o, _) => usadas.UnionWith((o["tags"] as JsonArray ?? []).Select(Texto).OfType<string>()));
            Recorrer(resultado["webhooks"], (o, _) => usadas.UnionWith((o["tags"] as JsonArray ?? []).Select(Texto).OfType<string>()));
            Assert.All(resultado["tags"] as JsonArray ?? [], t => Assert.Contains(Texto(t!["name"])!, usadas));
        }
        else
        {
            var servidores = resultado["servers"] as JsonObject ?? [];
            Assert.Equal(esperadas.Count, servidores.Count);
            Assert.All(servidores, s => Assert.StartsWith(destino.Ambiente, s.Key, StringComparison.Ordinal));
            Recorrer(resultado["channels"], (o, ruta) => Assert.False(ruta.Count(c => c == '/') == 1 && o.ContainsKey("servers"), $"Canal con servers ({contexto})."));
        }

        // Todo lo que queda (salvo servers) ya estaba en el original.
        var sinServidores = (JsonObject)resultado.DeepClone();
        sinServidores.Remove("servers");
        var originalSinServidores = (JsonObject)original.DeepClone();
        originalSinServidores.Remove("servers");
        Assert.True(Subconjunto(sinServidores, originalSinServidores), $"El resultado tiene algo que no está en el original ({contexto}).");
        Assert.Equal((formato, formato), (podado.Formato, TextoSpec.Formato(podado.Contenido)));
    }

    private static string? Texto(JsonNode? nodo) => nodo is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>FNV-1a de la ruta: estable entre ejecuciones (string.GetHashCode no lo es).</summary>
    private static int Huella(string texto)
    {
        var h = 2_166_136_261u;
        foreach (var c in texto)
        {
            h = (h ^ c) * 16_777_619u;
        }
        return (int)(h & 0x7FFF_FFFF);
    }
}
