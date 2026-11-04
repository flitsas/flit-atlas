using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Funciones de PostgreSQL que las consultas LINQ de Atlas usan y EF Core no trae, y la normalización del texto de las
/// búsquedas por nombre. Se registran en el modelo con <see cref="Registrar"/>; no cambian el esquema ni las migraciones.
/// <para>
/// Las búsquedas no distinguen tildes con <c>translate</c> (nativa de PostgreSQL) y no con la extensión <c>unaccent</c>:
/// esa la crea el administrador en cada ambiente (prompt maestro A-06) y aún no está verificada en las VPS
/// (<c>docs/preparacion/04-prerrequisitos.md</c>); una consulta que dependa de ella fallaría donde falte. Las letras de
/// <see cref="ConTildes"/> cubren el español y el portugués; cuando llegue la búsqueda full-text (F5, <c>tsvector</c> +
/// <c>unaccent</c>), se puede cambiar por ella.
/// </para>
/// </summary>
public static class FuncionesSql
{
    /// <summary>Letras con tilde, diéresis o cedilla que se igualan a su letra base (mayúsculas y minúsculas).</summary>
    public const string ConTildes = "áàäâãÁÀÄÂÃéèëêÉÈËÊíìïîÍÌÏÎóòöôõÓÒÖÔÕúùüûÚÙÜÛñÑçÇ";

    /// <summary>La letra base de cada una de <see cref="ConTildes"/>, en el mismo orden.</summary>
    public static readonly string SinTildes = string.Concat(ConTildes.Select(c => QuitarTildes(c.ToString())));

    /// <summary>
    /// <c>translate(texto, desde, hasta)</c> de PostgreSQL: cambia cada carácter de <paramref name="desde"/> por el de la
    /// misma posición en <paramref name="hasta"/>. Solo se traduce a SQL; llamarla en memoria falla.
    /// </summary>
    public static string Traducir(string texto, string desde, string hasta) =>
        throw new InvalidOperationException($"{nameof(Traducir)} solo se usa dentro de una consulta de EF Core.");

    /// <summary>
    /// <c>lower(texto)</c> de PostgreSQL. Se usa en lugar de <c>ToLowerInvariant</c>, que EF Core no traduce. Después de
    /// <see cref="Traducir"/> solo quedan letras ASCII por bajar, así que no depende de la configuración regional de la base.
    /// </summary>
    public static string Minusculas(string texto) =>
        throw new InvalidOperationException($"{nameof(Minusculas)} solo se usa dentro de una consulta de EF Core.");

    /// <summary>
    /// Texto de una búsqueda listo para comparar con <see cref="Minusculas"/>(<see cref="Traducir"/>(…)): sin tildes, en
    /// minúsculas y con los espacios colapsados.
    /// </summary>
    public static string NormalizarBusqueda(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return string.Join(' ', QuitarTildes(texto).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Patrón de <c>LIKE</c> que contiene <paramref name="termino"/>, con <c>%</c>, <c>_</c> y <c>\</c> escapados.</summary>
    public static string PatronContiene(string termino)
    {
        ArgumentNullException.ThrowIfNull(termino);
        return "%" + termino.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal) + "%";
    }

    /// <summary>Carácter de escape de <see cref="PatronContiene"/> para <c>EF.Functions.Like</c>.</summary>
    public const string EscapeLike = @"\";

    internal static void Registrar(ModelBuilder modelo)
    {
        modelo.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(Traducir))!).HasName("translate").IsBuiltIn();
        modelo.HasDbFunction(typeof(FuncionesSql).GetMethod(nameof(Minusculas))!).HasName("lower").IsBuiltIn();
    }

    private static string QuitarTildes(string texto)
    {
        var separado = texto.Normalize(NormalizationForm.FormD);
        var limpio = new StringBuilder(separado.Length);
        foreach (var c in separado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                limpio.Append(c);
            }
        }
        return limpio.ToString().Normalize(NormalizationForm.FormC);
    }
}
