using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Catalogo.Datos;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>
/// Los valores del catálogo como texto (los de la API, la semilla y la base son los mismos, en snake_case) y las reglas
/// de la jerarquía. Un solo lugar para que la API, la semilla y las pruebas no se contradigan.
/// </summary>
public static partial class Valores
{
    public static readonly IReadOnlyDictionary<string, TipoAplicativo> Tipos = new Dictionary<string, TipoAplicativo>(StringComparer.Ordinal)
    {
        ["producto"] = TipoAplicativo.Producto,
        ["modulo"] = TipoAplicativo.Modulo,
        ["servicio"] = TipoAplicativo.Servicio,
    };

    public static readonly IReadOnlyDictionary<string, GrupoAplicativo> Grupos = new Dictionary<string, GrupoAplicativo>(StringComparer.Ordinal)
    {
        ["flit2"] = GrupoAplicativo.Flit2,
        ["flito"] = GrupoAplicativo.Flito,
        ["satelite"] = GrupoAplicativo.Satelite,
        ["legado"] = GrupoAplicativo.Legado,
        ["infraestructura"] = GrupoAplicativo.Infraestructura,
    };

    public static readonly IReadOnlyDictionary<string, EstadoAplicativo> Estados = new Dictionary<string, EstadoAplicativo>(StringComparer.Ordinal)
    {
        ["planeado"] = EstadoAplicativo.Planeado,
        ["activo"] = EstadoAplicativo.Activo,
        ["en_retiro"] = EstadoAplicativo.EnRetiro,
        ["retirado"] = EstadoAplicativo.Retirado,
    };

    public static readonly IReadOnlyDictionary<string, Criticidad> Criticidades = new Dictionary<string, Criticidad>(StringComparer.Ordinal)
    {
        ["alta"] = Criticidad.Alta,
        ["media"] = Criticidad.Media,
        ["baja"] = Criticidad.Baja,
    };

    /// <summary>Ambientes de FLIT (FLIT 2.0 y FLITO tienen dev, qa y pdn; Atlas solo qa y pdn, ATLAS-ADR-0019).</summary>
    public static readonly IReadOnlyList<string> Ambientes = ["dev", "qa", "pdn"];

    /// <summary>Marca de lo que falta confirmar (prompt maestro §1, regla 1).</summary>
    public const string PorVerificar = "[por verificar]";

    public static string Texto(TipoAplicativo valor) => Tipos.First(p => p.Value == valor).Key;

    public static string Texto(GrupoAplicativo valor) => Grupos.First(p => p.Value == valor).Key;

    public static string Texto(EstadoAplicativo valor) => Estados.First(p => p.Value == valor).Key;

    public static string Texto(Criticidad valor) => Criticidades.First(p => p.Value == valor).Key;

    public static bool SlugValido(string? slug) => slug is { Length: > 0 and <= CatalogoDbContext.LargoSlug } && FormatoSlug().IsMatch(slug);

    public static bool RepoValido(string? repo) => repo is { Length: > 0 and <= CatalogoDbContext.LargoRepo } && FormatoRepo().IsMatch(repo);

    /// <summary>
    /// ¿Puede un aplicativo de <paramref name="tipo"/> colgar de un padre de <paramref name="tipoPadre"/>? Un producto va
    /// en la raíz; un módulo, bajo un producto; un servicio, bajo un producto o un módulo.
    /// </summary>
    public static bool JerarquiaValida(TipoAplicativo tipo, TipoAplicativo? tipoPadre) => (tipo, tipoPadre) switch
    {
        (TipoAplicativo.Producto, null) => true,
        (TipoAplicativo.Modulo, TipoAplicativo.Producto) => true,
        (TipoAplicativo.Servicio, TipoAplicativo.Producto or TipoAplicativo.Modulo) => true,
        _ => false,
    };

    public const string ReglaJerarquia = "Un producto no tiene padre; un módulo cuelga de un producto y un servicio de un producto o de un módulo.";

    /// <summary>
    /// Limpia una lista de textos cortos (tecnologías, etiquetas): sin espacios en los extremos, sin vacíos ni repetidos,
    /// en el orden en que llegaron. Las etiquetas van además en minúsculas.
    /// </summary>
    public static List<string> Limpiar(IEnumerable<string>? valores, bool minusculas) =>
    [
        .. (valores ?? [])
            .Select(v => (v ?? string.Empty).Trim())
            .Select(v => minusculas ? v.ToLowerInvariant() : v)
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex FormatoSlug();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex FormatoRepo();
}
