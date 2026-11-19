namespace Flit.Atlas.Modulos.Soporte.Dominio;

/// <summary>
/// Estados de un turno (prompt maestro §8.3): <c>programado → confirmado → aplicado</c>, o <c>reemplazado</c> /
/// <c>cancelado</c>. En la base van como texto en snake_case.
/// </summary>
public enum EstadoTurno
{
    Programado,
    Confirmado,
    Aplicado,
    Reemplazado,
    Cancelado,
}

/// <summary>De dónde salió el turno: el Excel (HU 2.7), la generación automática (HU 2.8) o a mano.</summary>
public enum OrigenTurno
{
    Importado,
    Generado,
    Manual,
}

/// <summary>
/// Qué hace la generación con un sábado festivo. Se configura; por defecto, sin turno
/// (prompt maestro §8.3; en Colombia solo caen en sábado los festivos fijos, p. ej. 1 de mayo y 7 de agosto de 2027).
/// </summary>
public enum ReglaFestivos
{
    /// <summary>Ese sábado no lleva turno.</summary>
    SinTurno,

    /// <summary>Ese sábado lleva turno como cualquier otro.</summary>
    TurnoNormal,
}

/// <summary>Motivo de una ausencia. Una incapacidad es un dato sensible: solo la ven quienes gestionan el soporte.</summary>
public enum TipoAusencia
{
    Vacaciones,
    Incapacidad,
    Otra,
}

/// <summary>Estado de una compensación (prompt maestro §8.3). El flujo de registro y aprobación llega en la HU 2.9.</summary>
public enum EstadoCompensacion
{
    Pendiente,
    Tomada,
}

/// <summary>Reglas de los estados de un turno y textos estables para la API.</summary>
public static class ReglasTurno
{
    /// <summary>Estados que ocupan el sábado: a lo sumo un turno así por fecha (índice único parcial en la base).</summary>
    public static readonly IReadOnlySet<EstadoTurno> Vigentes = new HashSet<EstadoTurno> { EstadoTurno.Programado, EstadoTurno.Confirmado, EstadoTurno.Aplicado };

    /// <summary>
    /// Transiciones permitidas. <c>aplicado</c>, <c>reemplazado</c> y <c>cancelado</c> son finales: un turno aplicado no
    /// vuelve a cambiar (ni a mano ni por la generación).
    /// </summary>
    public static bool PuedePasar(EstadoTurno desde, EstadoTurno hacia) => (desde, hacia) switch
    {
        (EstadoTurno.Programado, EstadoTurno.Confirmado or EstadoTurno.Cancelado or EstadoTurno.Reemplazado) => true,
        (EstadoTurno.Confirmado, EstadoTurno.Aplicado or EstadoTurno.Cancelado or EstadoTurno.Reemplazado) => true,
        _ => false,
    };

    public static bool EsFinal(EstadoTurno estado) => estado is EstadoTurno.Aplicado or EstadoTurno.Reemplazado or EstadoTurno.Cancelado;

    /// <summary><c>EnAcompanamiento</c> → <c>en_acompanamiento</c>, el mismo formato que la base.</summary>
    public static string Texto<TEnum>(TEnum valor) where TEnum : struct, Enum
    {
        var nombre = valor.ToString();
        var sb = new System.Text.StringBuilder(nombre.Length + 4);
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

    /// <summary>El valor del enum para su texto en snake_case, o <c>null</c> si no existe.</summary>
    public static TEnum? Leer<TEnum>(string? texto) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }
        var limpio = texto.Trim();
        return Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(v => Texto(v!.Value) == limpio);
    }

    /// <summary>Los valores en texto, para los mensajes de validación y el contrato.</summary>
    public static string Valores<TEnum>() where TEnum : struct, Enum => string.Join(", ", Enum.GetValues<TEnum>().Select(v => Texto(v)));
}

/// <summary>Fechas y horas de Bogotá (UTC−5, sin horario de verano), donde vive el soporte.</summary>
public static class CalendarioBogota
{
    public static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    /// <summary>Fecha de hoy en Bogotá según el reloj de la aplicación.</summary>
    public static DateOnly Hoy(TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), Zona).DateTime);
    }

    /// <summary>La misma fecha si es sábado; si no, el sábado siguiente.</summary>
    public static DateOnly SabadoDesde(DateOnly fecha) =>
        fecha.AddDays(((int)DayOfWeek.Saturday - (int)fecha.DayOfWeek + 7) % 7);

    /// <summary>Sábados entre dos fechas: desde incluida, hasta excluida.</summary>
    public static IEnumerable<DateOnly> Sabados(DateOnly desde, DateOnly hastaExcluida)
    {
        for (var sabado = SabadoDesde(desde); sabado < hastaExcluida; sabado = sabado.AddDays(7))
        {
            yield return sabado;
        }
    }
}
