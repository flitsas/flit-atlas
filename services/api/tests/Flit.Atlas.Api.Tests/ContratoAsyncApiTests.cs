using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Plataforma.Mensajeria;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// <c>contracts/asyncapi/atlas-events.v1.yaml</c> se escribe a mano; estas pruebas lo atan al código (HU 1.7,
/// ATLAS-ADR-0021): cada <see cref="TipoEvento"/> declarado está en el AsyncAPI (y al revés), el código de producción
/// solo encola tipos declarados, y el sobre documentado es el que serializa <see cref="SobreEvento"/>.
/// </summary>
public partial class ContratoAsyncApiTests
{
    private static string RutaContrato => Path.Combine(Repositorio.Raiz, "contracts", "asyncapi", "atlas-events.v1.yaml");

    private static string Contrato() => File.ReadAllText(RutaContrato).ReplaceLineEndings("\n");

    /// <summary>
    /// Los ensamblados de Atlas que carga atlas-api (host, núcleo y módulos registrados), por referencias desde el host.
    /// </summary>
    private static IReadOnlyCollection<Assembly> EnsambladosAtlas()
    {
        var vistos = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pendientes = new Stack<Assembly>([typeof(Program).Assembly]);
        while (pendientes.Count > 0)
        {
            var ensamblado = pendientes.Pop();
            if (!vistos.TryAdd(ensamblado.GetName().Name!, ensamblado))
            {
                continue;
            }
            foreach (var referencia in ensamblado.GetReferencedAssemblies()
                         .Where(r => r.Name!.StartsWith("Flit.Atlas.", StringComparison.Ordinal)))
            {
                pendientes.Push(Assembly.Load(referencia));
            }
        }
        return [.. vistos.Values];
    }

    /// <summary>Campos <c>static readonly TipoEvento</c> de los ensamblados de Atlas: el catálogo de eventos publicados.</summary>
    private static List<(string Campo, TipoEvento Tipo)> CatalogoDelCodigo() =>
    [
        .. EnsambladosAtlas()
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f.FieldType == typeof(TipoEvento) && f.IsInitOnly)
            .Select(f => (f.Name, (TipoEvento)f.GetValue(null)!)),
    ];

    /// <summary>Eventos del AsyncAPI: cada mensaje lleva <c>x-atlas-tipo</c> y, en la línea siguiente, <c>x-atlas-version</c>.</summary>
    private static List<TipoEvento> CatalogoDelContrato() =>
    [
        .. EventoDelContrato().Matches(Contrato())
            .Select(m => new TipoEvento(m.Groups["tipo"].Value, int.Parse(m.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture))),
    ];

    [GeneratedRegex(@"^\s+x-atlas-tipo:\s*(?<tipo>\S+)\s*\n\s+x-atlas-version:\s*(?<version>\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex EventoDelContrato();

    [Fact]
    public void Cada_evento_declarado_en_el_codigo_esta_en_el_asyncapi_y_al_reves()
    {
        var codigo = CatalogoDelCodigo().Select(c => c.Tipo).Distinct().OrderBy(t => t.ToString(), StringComparer.Ordinal).ToList();
        var contrato = CatalogoDelContrato().OrderBy(t => t.ToString(), StringComparer.Ordinal).ToList();

        var faltan = codigo.Except(contrato).ToList();
        var sobran = contrato.Except(codigo).ToList();

        Assert.True(faltan.Count == 0,
            $"Eventos del código que no están en contracts/asyncapi/atlas-events.v1.yaml: {string.Join(", ", faltan)}");
        Assert.True(sobran.Count == 0,
            $"Eventos del AsyncAPI que el código no declara como TipoEvento: {string.Join(", ", sobran)}");
    }

    [GeneratedRegex(@"\.EncolarEvento\s*(<[^>]*>)?\s*\(\s*(?<tipo>[^,]+),")]
    private static partial Regex LlamadaEncolar();

    [Fact]
    public void El_codigo_de_produccion_solo_encola_eventos_declarados_en_el_catalogo()
    {
        var campos = CatalogoDelCodigo().Select(c => c.Campo).ToHashSet(StringComparer.Ordinal);
        var fuentes = Path.Combine(Repositorio.Raiz, "services", "api", "src");
        var definicion = Path.Combine("Flit.Atlas.Plataforma", "Mensajeria", "Mensajes.cs");
        var malas = new List<string>();

        foreach (var archivo in Directory.EnumerateFiles(fuentes, "*.cs", SearchOption.AllDirectories)
                     .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !a.EndsWith(definicion, StringComparison.Ordinal)))
        {
            foreach (Match llamada in LlamadaEncolar().Matches(File.ReadAllText(archivo)))
            {
                // El primer argumento debe ser un campo TipoEvento del catálogo (p. ej. EventosSoporte.TurnoCambiado).
                var argumento = llamada.Groups["tipo"].Value.Trim();
                var campo = argumento.Split('.').Last();
                if (!campos.Contains(campo))
                {
                    malas.Add($"{Path.GetRelativePath(fuentes, archivo)}: EncolarEvento({argumento}, …)");
                }
            }
        }

        Assert.True(malas.Count == 0,
            "Encola con un campo static readonly TipoEvento, no con un texto suelto: " + string.Join("; ", malas));
    }

    [Fact]
    public void El_sobre_documentado_tiene_los_campos_que_serializa_SobreEvento()
    {
        var sobre = new SobreEvento(Guid.CreateVersion7(), "atlas.ejemplo.algo_ocurrido", 1, DateTimeOffset.UtcNow,
            SobreEvento.SinOrganizacion, SobreEvento.Productor, string.Empty, JsonSerializer.SerializeToElement(new { }));
        var campos = JsonDocument.Parse(sobre.AJson()).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        var contrato = Contrato();

        var esquema = contrato[contrato.IndexOf("\n    SobreEvento:\n", StringComparison.Ordinal)..];
        var requeridos = Regex.Matches(esquema[..esquema.IndexOf("      properties:", StringComparison.Ordinal)], @"^\s+- (\w+)$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(campos, requeridos);
        Assert.Contains($"address: {SobreEvento.Exchange}\n", contrato, StringComparison.Ordinal);
        Assert.Contains($"const: {SobreEvento.Productor}\n", contrato, StringComparison.Ordinal);
    }

    [Fact]
    public void La_lectura_del_contrato_reconoce_los_eventos_declarados()
    {
        const string fragmento = """
              messages:
                organizacionCreadaV1:
                  name: atlas.plataforma.organizacion_creada
                  x-atlas-tipo: atlas.plataforma.organizacion_creada
                  x-atlas-version: 1
            #     x-atlas-tipo: atlas.comentado.no_cuenta
            #     x-atlas-version: 1
            """;

        var eventos = EventoDelContrato().Matches(fragmento.ReplaceLineEndings("\n")).Select(m => m.Groups["tipo"].Value);

        Assert.Equal(["atlas.plataforma.organizacion_creada"], eventos);
    }

    [Theory]
    [InlineData("organizacion_creada")]
    [InlineData("atlas.Plataforma.creada")]
    [InlineData("atlas.plataforma")]
    [InlineData("flit.plataforma.organizacion_creada")]
    public void Un_tipo_de_evento_fuera_del_formato_se_rechaza(string nombre) =>
        Assert.Throws<ArgumentException>(() => new TipoEvento(nombre, 1));
}
