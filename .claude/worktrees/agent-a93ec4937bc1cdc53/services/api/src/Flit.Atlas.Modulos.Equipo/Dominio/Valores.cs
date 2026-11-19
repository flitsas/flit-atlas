using System.Globalization;
using System.Text;

namespace Flit.Atlas.Modulos.Equipo.Dominio;

public enum TipoResponsabilidad
{
    Principal,
    Secundario,
}

public enum EstadoTraspaso
{
    EnAcompanamiento,
    Habilitado,
}

public enum OrigenVersion
{
    Importacion,
    Cambio,
    Restauracion,
}

public enum TipoActividadTraspaso
{
    Despliegue,
    Guardia,
}

public enum EstadoRevision
{
    Pendiente,
    Hecha,
}

/// <summary>Los valores del módulo como texto snake_case (los de la API y la base son los mismos).</summary>
public static class Textos
{
    public static string De<TEnum>(TEnum valor) where TEnum : struct, Enum
    {
        var nombre = valor.ToString();
        var sb = new StringBuilder(nombre.Length + 4);
        for (var i = 0; i < nombre.Length; i++)
        {
            if (char.IsUpper(nombre[i]) && i > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(nombre[i]));
        }
        return sb.ToString();
    }

    /// <summary>El valor del enum cuyo texto es <paramref name="texto"/>, o <c>null</c>.</summary>
    public static TEnum? Leer<TEnum>(string? texto) where TEnum : struct, Enum
    {
        var limpio = texto?.Trim();
        return Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(v => De(v!.Value) == limpio);
    }

    public static string Lista<TEnum>() where TEnum : struct, Enum => string.Join(", ", Enum.GetValues<TEnum>().Select(De));

    /// <summary>
    /// Texto para comparar nombres de personas y de aplicativos: sin tildes, en minúsculas, sin negritas de Markdown y
    /// con un solo espacio entre palabras («Persona Demo Séptima» = «persona demo septima»).
    /// </summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }
        var sinMarcas = texto.Replace("**", string.Empty, StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(sinMarcas.Length);
        foreach (var c in sinMarcas)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Recorta y deja vacío en lugar de espacios.</summary>
    public static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Fecha dd/mm/aaaa (prompt maestro §1).</summary>
    public static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

/// <summary>Fechas de Bogotá (UTC−5, sin horario de verano).</summary>
public static class CalendarioBogota
{
    public static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    public static DateOnly Hoy(TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), Zona).DateTime);
    }
}
