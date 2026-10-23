namespace Flit.Atlas.Modulos.Soporte.Dominio;

/// <summary>Cómo se fija la fecha de un festivo de Colombia.</summary>
public enum TipoFestivo
{
    /// <summary>Fecha fija que no se traslada (1 de enero, 1 de mayo, 20 de julio, 7 de agosto, 8 y 25 de diciembre).</summary>
    Fijo,

    /// <summary>Se celebra el lunes siguiente si no cae en lunes (Ley 51 de 1983, «Ley Emiliani»).</summary>
    TrasladableAlLunes,

    /// <summary>Relativo al domingo de Pascua y sin traslado (Jueves y Viernes Santo).</summary>
    SemanaSanta,

    /// <summary>Relativo al domingo de Pascua y celebrado en lunes (Ascensión, Corpus Christi y Sagrado Corazón).</summary>
    RelativoAPascua,
}

/// <param name="Fecha">Día en que se celebra (ya trasladado).</param>
/// <param name="Nombre">Nombre oficial del festivo.</param>
/// <param name="Tipo">Cómo se calcula.</param>
/// <param name="FechaOriginal">Fecha de la celebración antes del traslado (igual a <paramref name="Fecha"/> si no se movió).</param>
public sealed record Festivo(DateOnly Fecha, string Nombre, TipoFestivo Tipo, DateOnly FechaOriginal)
{
    public bool Trasladado => Fecha != FechaOriginal;
}

/// <summary>
/// Festivos nacionales de Colombia calculados en el propio proceso, sin servicios externos (prompt maestro §8.3): los
/// 18 días de la Ley 51 de 1983 («Ley Emiliani») y, desde 2026, el 9 de julio de la Ley 2578 de 2026 (19 en total). Es
/// un componente puro (sin reloj, base ni red) para que lo reutilicen la generación de turnos (HU 2.8), la
/// sincronización con Microsoft 365 (HU 2.10) y los recordatorios (HU 2.11).
/// <list type="bullet">
/// <item>Fijos: 1 de enero, 1 de mayo, 20 de julio, 7 de agosto, 8 de diciembre y 25 de diciembre.</item>
/// <item>Trasladables al lunes siguiente: 6 de enero, 19 de marzo, 29 de junio, 9 de julio (desde 2026), 15 de agosto,
/// 12 de octubre, 1 de noviembre y 11 de noviembre.</item>
/// <item>Semana Santa (sin traslado): Jueves Santo (Pascua − 3) y Viernes Santo (Pascua − 2).</item>
/// <item>Relativos a Pascua y en lunes: Ascensión (Pascua + 43), Corpus Christi (Pascua + 64) y Sagrado Corazón
/// (Pascua + 71).</item>
/// </list>
/// Los días cívicos que decrete el Gobierno para un año concreto no salen de aquí: quien gestiona el soporte los agrega como festivos
/// adicionales del módulo (<c>soporte_festivo</c>).
/// </summary>
public static class FestivosColombia
{
    /// <summary>Primer año con las reglas de la Ley 51 de 1983 (rige desde 1984).</summary>
    public const int AnioMinimo = 1984;

    /// <summary>Último año que cabe en <see cref="DateOnly"/>; el algoritmo de Pascua vale para todo el calendario gregoriano.</summary>
    public const int AnioMaximo = 9999;

    private static readonly (int Mes, int Dia, string Nombre)[] Fijos =
    [
        (1, 1, "Año Nuevo"),
        (5, 1, "Día del Trabajo"),
        (7, 20, "Día de la Independencia"),
        (8, 7, "Batalla de Boyacá"),
        (12, 8, "Inmaculada Concepción"),
        (12, 25, "Navidad"),
    ];

    /// <summary>
    /// Primer año del festivo de Nuestra Señora del Rosario de Chiquinquirá (9 de julio, trasladable), de la Ley 2578 del
    /// 1 de junio de 2026: en 2026 se celebró el lunes 13 de julio. Hay una demanda ante la Corte Constitucional
    /// [por verificar]: si la Corte la tumba, se cambia aquí y en sus pruebas.
    /// </summary>
    public const int AnioChiquinquira = 2026;

    private static readonly (int Mes, int Dia, string Nombre, int Desde)[] Trasladables =
    [
        (1, 6, "Día de los Reyes Magos", AnioMinimo),
        (3, 19, "Día de San José", AnioMinimo),
        (6, 29, "San Pedro y San Pablo", AnioMinimo),
        (7, 9, "Virgen del Rosario de Chiquinquirá", AnioChiquinquira),
        (8, 15, "Asunción de la Virgen", AnioMinimo),
        (10, 12, "Día de la Raza", AnioMinimo),
        (11, 1, "Día de Todos los Santos", AnioMinimo),
        (11, 11, "Independencia de Cartagena", AnioMinimo),
    ];

    /// <summary>
    /// Domingo de Pascua del calendario gregoriano con el algoritmo anónimo de Meeus/Jones/Butcher (aritmética entera,
    /// exacto para todo año gregoriano).
    /// </summary>
    public static DateOnly Pascua(int anio)
    {
        ValidarAnio(anio);
        var a = anio % 19;
        var b = anio / 100;
        var c = anio % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var mes = (h + l - (7 * m) + 114) / 31;
        var dia = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(anio, mes, dia);
    }

    /// <summary>Los festivos del año (18 hasta 2025, 19 desde 2026), ordenados por fecha.</summary>
    public static IReadOnlyList<Festivo> DelAnio(int anio)
    {
        ValidarAnio(anio);
        var festivos = new List<Festivo>(19);
        foreach (var (mes, dia, nombre) in Fijos)
        {
            var fecha = new DateOnly(anio, mes, dia);
            festivos.Add(new Festivo(fecha, nombre, TipoFestivo.Fijo, fecha));
        }
        foreach (var (mes, dia, nombre, _) in Trasladables.Where(t => anio >= t.Desde))
        {
            var original = new DateOnly(anio, mes, dia);
            festivos.Add(new Festivo(AlLunes(original), nombre, TipoFestivo.TrasladableAlLunes, original));
        }
        var pascua = Pascua(anio);
        festivos.Add(new Festivo(pascua.AddDays(-3), "Jueves Santo", TipoFestivo.SemanaSanta, pascua.AddDays(-3)));
        festivos.Add(new Festivo(pascua.AddDays(-2), "Viernes Santo", TipoFestivo.SemanaSanta, pascua.AddDays(-2)));
        // La celebración litúrgica cae en jueves (Ascensión y Corpus) o viernes (Sagrado Corazón); la ley la pasa al lunes.
        festivos.Add(new Festivo(pascua.AddDays(43), "Ascensión del Señor", TipoFestivo.RelativoAPascua, pascua.AddDays(39)));
        festivos.Add(new Festivo(pascua.AddDays(64), "Corpus Christi", TipoFestivo.RelativoAPascua, pascua.AddDays(60)));
        festivos.Add(new Festivo(pascua.AddDays(71), "Sagrado Corazón de Jesús", TipoFestivo.RelativoAPascua, pascua.AddDays(68)));
        festivos.Sort((x, y) => x.Fecha.CompareTo(y.Fecha));
        return festivos;
    }

    /// <summary>Festivos entre dos fechas (ambas incluidas), ordenados.</summary>
    public static IReadOnlyList<Festivo> Entre(DateOnly desde, DateOnly hasta)
    {
        if (hasta < desde)
        {
            return [];
        }
        return [.. Enumerable.Range(desde.Year, hasta.Year - desde.Year + 1)
            .SelectMany(DelAnio)
            .Where(f => f.Fecha >= desde && f.Fecha <= hasta)];
    }

    /// <summary>El festivo de esa fecha, o <c>null</c> si es un día hábil (los domingos no se cuentan como festivos).</summary>
    public static Festivo? De(DateOnly fecha) =>
        fecha.Year is < AnioMinimo or > AnioMaximo ? null : DelAnio(fecha.Year).FirstOrDefault(f => f.Fecha == fecha);

    public static bool EsFestivo(DateOnly fecha) => De(fecha) is not null;

    /// <summary>La misma fecha si es lunes; si no, el lunes siguiente.</summary>
    internal static DateOnly AlLunes(DateOnly fecha) =>
        fecha.AddDays(((int)DayOfWeek.Monday - (int)fecha.DayOfWeek + 7) % 7);

    private static void ValidarAnio(int anio)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(anio, AnioMinimo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(anio, AnioMaximo);
    }
}
