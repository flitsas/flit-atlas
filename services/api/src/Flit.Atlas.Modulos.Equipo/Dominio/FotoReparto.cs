using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flit.Atlas.Modulos.Equipo.Dominio;

/// <summary>
/// Una responsabilidad dentro de una foto del reparto. Los nombres (del aplicativo y de la persona) son los del momento
/// de la versión, para leer el historial aunque cambien después; la huella no los usa.
/// </summary>
/// <param name="TipoAplicativo"><c>producto</c>, <c>modulo</c> o <c>servicio</c>: define en qué tabla va (§3.1, §3.2).</param>
/// <param name="Tipo"><c>principal</c> o <c>secundario</c>.</param>
public sealed record ResponsabilidadFoto(
    Guid AplicativoId, string Slug, string Aplicativo, string TipoAplicativo, string Tipo,
    Guid PersonaId, string Persona, int? Dedicacion, DateOnly VigenteDesde, DateOnly? VigenteHasta,
    int OrdenAplicativo, int OrdenPersona, int OrdenEnPersona);

/// <summary>Definición de un rol tal como está en el §1 del documento de responsables.</summary>
/// <param name="Rol"><c>principal</c> o <c>secundario</c>.</param>
/// <param name="Titulo">El título en negrita («Responsable principal»).</param>
/// <param name="Puntos">Cada viñeta, sin el guion inicial y sin cambiar una letra.</param>
public sealed record DefinicionRol(string Rol, string Titulo, IReadOnlyList<string> Puntos);

/// <summary>
/// La foto completa del reparto que guarda cada versión (ATLAS-ADR-0031). Se compara por <see cref="Huella"/>: dos
/// fotos con la misma huella reparten igual (mismas personas, tipos, dedicaciones, vigencias y orden).
/// </summary>
public sealed record FotoReparto(IReadOnlyList<ResponsabilidadFoto> Responsabilidades)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string AJson() => JsonSerializer.Serialize(this, Json);

    public static FotoReparto DesdeJson(string json) =>
        JsonSerializer.Deserialize<FotoReparto>(json, Json) ?? new FotoReparto([]);

    /// <summary>Ordenada como las tablas: por aplicativo y, dentro de él, principal antes que secundario.</summary>
    public IEnumerable<ResponsabilidadFoto> Ordenadas() =>
        Responsabilidades.OrderBy(r => r.OrdenAplicativo).ThenBy(r => r.Tipo == "principal" ? 0 : 1).ThenBy(r => r.AplicativoId);

    /// <summary>SHA-256 (hex en minúsculas) del reparto sin nombres, en orden canónico.</summary>
    public string Huella()
    {
        var sb = new StringBuilder();
        foreach (var r in Responsabilidades.OrderBy(r => r.AplicativoId).ThenBy(r => r.Tipo, StringComparer.Ordinal))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{r.AplicativoId:D}|{r.Tipo}|{r.PersonaId:D}|{r.Dedicacion}|{r.VigenteDesde:yyyy-MM-dd}|{r.VigenteHasta:yyyy-MM-dd}|{r.OrdenAplicativo}|{r.OrdenPersona}|{r.OrdenEnPersona}\n");
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static string DefinicionesAJson(IReadOnlyList<DefinicionRol> definiciones) => JsonSerializer.Serialize(definiciones, Json);

    public static IReadOnlyList<DefinicionRol> DefinicionesDesdeJson(string json) =>
        JsonSerializer.Deserialize<List<DefinicionRol>>(json, Json) ?? [];
}

/// <summary>
/// Las tablas 2, 3.1 y 3.2 del documento de responsables, reconstruidas desde los datos con el mismo formato Markdown
/// (CA de la HU 2.3: se reproducen exactamente). Los servicios con responsables propios van en una tabla más.
/// </summary>
public static class TablasReparto
{
    public const string SinResponsable = "—";

    public sealed record Tablas(string Personas, string Productos, string Modulos, string? Servicios)
    {
        /// <summary>Las tablas con sus títulos, como en el documento.</summary>
        public string Completo()
        {
            var sb = new StringBuilder();
            sb.Append("## 2. Reparto por persona\n\n").Append(Personas).Append('\n');
            sb.Append("## 3. Reparto por producto y módulo\n\n### 3.1 Productos\n\n").Append(Productos).Append('\n');
            sb.Append("### 3.2 Módulos de FLIT 2\n\n").Append(Modulos);
            if (Servicios is not null)
            {
                sb.Append("\n### 3.3 Servicios\n\n").Append(Servicios);
            }
            return sb.ToString();
        }
    }

    public static Tablas Generar(FotoReparto foto)
    {
        ArgumentNullException.ThrowIfNull(foto);
        var servicios = PorAplicativo(foto, "servicio", "Servicio");
        return new Tablas(PorPersona(foto), PorAplicativo(foto, "producto", "Producto")!, PorAplicativo(foto, "modulo", "Módulo")!,
            foto.Responsabilidades.Any(r => r.TipoAplicativo == "servicio") ? servicios : null);
    }

    private static string PorPersona(FotoReparto foto)
    {
        var sb = new StringBuilder("| Persona | Principal de | Secundario de |\n|---|---|---|\n");
        foreach (var persona in foto.Responsabilidades.GroupBy(r => r.PersonaId)
                     .OrderBy(g => g.Min(r => r.OrdenPersona)).ThenBy(g => g.First().Persona, StringComparer.CurrentCulture))
        {
            sb.Append(CultureInfo.InvariantCulture, $"| **{persona.First().Persona}** | {Lista(persona, "principal")} | {Lista(persona, "secundario")} |\n");
        }
        return sb.ToString();
    }

    private static string Lista(IEnumerable<ResponsabilidadFoto> deLaPersona, string tipo)
    {
        var nombres = deLaPersona.Where(r => r.Tipo == tipo).OrderBy(r => r.OrdenEnPersona).ThenBy(r => r.OrdenAplicativo).Select(r => r.Aplicativo).ToList();
        return nombres.Count == 0 ? SinResponsable : string.Join(", ", nombres);
    }

    private static string PorAplicativo(FotoReparto foto, string tipoAplicativo, string encabezado)
    {
        var sb = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"| {encabezado} | Principal | Secundario |\n|---|---|---|\n"));
        foreach (var aplicativo in foto.Responsabilidades.Where(r => r.TipoAplicativo == tipoAplicativo).GroupBy(r => r.AplicativoId)
                     .OrderBy(g => g.Min(r => r.OrdenAplicativo)).ThenBy(g => g.First().Aplicativo, StringComparer.CurrentCulture))
        {
            var principal = aplicativo.FirstOrDefault(r => r.Tipo == "principal")?.Persona ?? SinResponsable;
            var secundario = aplicativo.FirstOrDefault(r => r.Tipo == "secundario")?.Persona ?? SinResponsable;
            sb.Append(CultureInfo.InvariantCulture, $"| {aplicativo.First().Aplicativo} | {principal} | {secundario} |\n");
        }
        return sb.ToString();
    }
}

/// <summary>Carga de una persona en el reparto (mapa de carga, §8.2).</summary>
/// <param name="Puntaje">Principales + la dedicación de cada secundaria (20 % ⇒ 0,2).</param>
/// <param name="Nivel"><c>alta</c>, <c>normal</c> o <c>baja</c> frente a la media del equipo.</param>
public sealed record CargaPersona(Guid PersonaId, string Persona, int Principales, int Secundarias, decimal Puntaje, string Nivel);

/// <summary>
/// Mapa de carga: cuántas responsabilidades tiene cada persona y quién se sale de la media. Es orientativo: un
/// principal cuenta 1 y un secundario su dedicación (por defecto 20 %).
/// </summary>
public static class MapaCarga
{
    /// <summary>Por encima de la media × (1 + margen) la carga es alta; por debajo de la media × (1 − margen), baja.</summary>
    public const decimal Margen = 0.3m;

    public const int DedicacionSecundarioPorDefecto = 20;

    public static (decimal Media, IReadOnlyList<CargaPersona> Personas) Calcular(FotoReparto foto)
    {
        ArgumentNullException.ThrowIfNull(foto);
        var personas = foto.Responsabilidades.GroupBy(r => r.PersonaId).Select(g =>
        {
            var principales = g.Count(r => r.Tipo == "principal");
            var secundarias = g.Where(r => r.Tipo == "secundario").ToList();
            var puntaje = principales + secundarias.Sum(r => (r.Dedicacion ?? DedicacionSecundarioPorDefecto) / 100m);
            return (Id: g.Key, Nombre: g.First().Persona, Orden: g.Min(r => r.OrdenPersona), principales, Secundarias: secundarias.Count, puntaje);
        }).ToList();
        if (personas.Count == 0)
        {
            return (0, []);
        }
        var media = Math.Round(personas.Average(p => p.puntaje), 2);
        return (media,
        [
            .. personas.OrderByDescending(p => p.puntaje).ThenBy(p => p.Orden).Select(p => new CargaPersona(p.Id, p.Nombre, p.principales, p.Secundarias,
                Math.Round(p.puntaje, 2),
                p.puntaje > media * (1 + Margen) ? "alta" : p.puntaje < media * (1 - Margen) ? "baja" : "normal")),
        ]);
    }
}
