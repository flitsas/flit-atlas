using System.Globalization;
using System.Text;

namespace Flit.Atlas.Modulos.Soporte.Dominio;

/// <summary>Persona de Atlas contra la que se compara un nombre escrito a mano (Excel de la rotación, HU 2.7).</summary>
public sealed record PersonaConocida(Guid Id, string Nombre, bool Activa);

/// <summary>Cómo quedó un nombre escrito a mano frente a las personas de Atlas.</summary>
public enum TipoCoincidencia
{
    /// <summary>El mismo nombre sin tildes ni mayúsculas.</summary>
    Exacta,

    /// <summary>Todas las palabras del texto están en el nombre de una sola persona (p. ej. solo el nombre de pila).</summary>
    Parcial,

    /// <summary>Varias personas encajan: lo decide una persona.</summary>
    Ambigua,

    /// <summary>Ninguna persona de Atlas encaja.</summary>
    Ninguna,
}

/// <summary>Resultado de buscar un nombre: la persona (si es única) y las candidatas (si es ambiguo).</summary>
public sealed record Coincidencia(TipoCoincidencia Tipo, PersonaConocida? Persona, IReadOnlyList<PersonaConocida> Candidatas)
{
    public bool Unica => Persona is not null;
}

/// <summary>
/// Normalización de nombres de personas para el importador (HU 2.7): sin tildes, sin mayúsculas, sin signos y con un
/// solo espacio, así «Ana Perez» encaja con «Ana Pérez». Puro: sin base ni reloj.
/// </summary>
public static class NombresPersona
{
    /// <summary>«  José   Pérez-Gómez » → «jose perez gomez».</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        var espacio = true;
        foreach (var c in descompuesto)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                espacio = false;
            }
            else if (!espacio)
            {
                sb.Append(' ');
                espacio = true;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Palabras del nombre normalizado.</summary>
    public static string[] Palabras(string? texto) => Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Busca el texto entre las personas: primero el nombre completo igual; si no, las personas cuyo nombre contiene todas
    /// las palabras del texto (un nombre parcial). Una sola → esa persona; varias → ambiguo; ninguna → ninguna.
    /// </summary>
    public static Coincidencia Buscar(string? texto, IReadOnlyList<PersonaConocida> personas)
    {
        ArgumentNullException.ThrowIfNull(personas);
        var normal = Normalizar(texto);
        if (normal.Length == 0)
        {
            return new Coincidencia(TipoCoincidencia.Ninguna, null, []);
        }
        var exactas = personas.Where(p => Normalizar(p.Nombre) == normal).ToList();
        if (exactas.Count == 1)
        {
            return new Coincidencia(TipoCoincidencia.Exacta, exactas[0], []);
        }
        if (exactas.Count > 1)
        {
            return new Coincidencia(TipoCoincidencia.Ambigua, null, exactas);
        }
        var palabras = normal.Split(' ');
        var parciales = personas.Where(p =>
        {
            var suyas = Palabras(p.Nombre);
            return palabras.All(w => suyas.Contains(w, StringComparer.Ordinal));
        }).ToList();
        return parciales.Count switch
        {
            1 => new Coincidencia(TipoCoincidencia.Parcial, parciales[0], []),
            0 => new Coincidencia(TipoCoincidencia.Ninguna, null, []),
            _ => new Coincidencia(TipoCoincidencia.Ambigua, null, parciales),
        };
    }
}
