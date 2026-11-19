using Flit.Atlas.Modulos.Soporte.Dominio;

namespace Flit.Atlas.Modulos.Soporte.Tests;

/// <summary>
/// CA de la HU 2.8: festivos de Colombia calculados localmente (Ley 51 de 1983 y Ley 2578 de 2026). Las listas de 2026 y
/// 2027 son las del calendario oficial, completas y en orden.
/// </summary>
public class FestivosColombiaTests
{
    private static DateOnly D(int anio, int mes, int dia) => new(anio, mes, dia);

    public static TheoryData<int, DateOnly[]> Oficiales() => new()
    {
        {
            2026,
            [
                D(2026, 1, 1), D(2026, 1, 12), D(2026, 3, 23), D(2026, 4, 2), D(2026, 4, 3), D(2026, 5, 1), D(2026, 5, 18),
                D(2026, 6, 8), D(2026, 6, 15), D(2026, 6, 29), D(2026, 7, 13), D(2026, 7, 20), D(2026, 8, 7), D(2026, 8, 17),
                D(2026, 10, 12), D(2026, 11, 2), D(2026, 11, 16), D(2026, 12, 8), D(2026, 12, 25),
            ]
        },
        {
            2027,
            [
                D(2027, 1, 1), D(2027, 1, 11), D(2027, 3, 22), D(2027, 3, 25), D(2027, 3, 26), D(2027, 5, 1), D(2027, 5, 10),
                D(2027, 5, 31), D(2027, 6, 7), D(2027, 7, 5), D(2027, 7, 12), D(2027, 7, 20), D(2027, 8, 7), D(2027, 8, 16),
                D(2027, 10, 18), D(2027, 11, 1), D(2027, 11, 15), D(2027, 12, 8), D(2027, 12, 25),
            ]
        },
    };

    [Theory]
    [MemberData(nameof(Oficiales))]
    public void Los_festivos_del_anio_coinciden_con_el_calendario_oficial(int anio, DateOnly[] esperados)
    {
        Assert.Equal(esperados, FestivosColombia.DelAnio(anio).Select(f => f.Fecha));
    }

    [Fact]
    public void Los_nombres_y_traslados_de_2027_son_los_de_la_ley()
    {
        var festivos = FestivosColombia.DelAnio(2027).ToDictionary(f => f.Fecha);

        Assert.Equal("Día de los Reyes Magos", festivos[D(2027, 1, 11)].Nombre);
        Assert.Equal(D(2027, 1, 6), festivos[D(2027, 1, 11)].FechaOriginal);
        Assert.True(festivos[D(2027, 1, 11)].Trasladado);
        Assert.Equal(TipoFestivo.SemanaSanta, festivos[D(2027, 3, 25)].Tipo);
        Assert.Equal("Viernes Santo", festivos[D(2027, 3, 26)].Nombre);
        Assert.Equal("Ascensión del Señor", festivos[D(2027, 5, 10)].Nombre);
        Assert.Equal("Corpus Christi", festivos[D(2027, 5, 31)].Nombre);
        Assert.Equal("Sagrado Corazón de Jesús", festivos[D(2027, 6, 7)].Nombre);
        Assert.Equal("Virgen del Rosario de Chiquinquirá", festivos[D(2027, 7, 12)].Nombre);
        // Los fijos no se trasladan aunque caigan en sábado (1 de mayo, 7 de agosto y 25 de diciembre de 2027).
        Assert.All([D(2027, 5, 1), D(2027, 8, 7), D(2027, 12, 25)], f =>
        {
            Assert.Equal(DayOfWeek.Saturday, f.DayOfWeek);
            Assert.False(festivos[f].Trasladado);
        });
        // Todos los Santos 2027 cae en lunes: se queda.
        Assert.False(festivos[D(2027, 11, 1)].Trasladado);
    }

    [Theory]
    [InlineData(2000, 4, 23)]
    [InlineData(2019, 4, 21)]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2028, 4, 16)]
    [InlineData(2038, 4, 25)]
    [InlineData(2285, 3, 22)]
    public void La_pascua_sale_del_algoritmo(int anio, int mes, int dia) =>
        Assert.Equal(D(anio, mes, dia), FestivosColombia.Pascua(anio));

    [Fact]
    public void Cada_anio_tiene_sus_festivos_y_los_trasladados_caen_en_lunes()
    {
        for (var anio = FestivosColombia.AnioMinimo; anio <= 2100; anio++)
        {
            var festivos = FestivosColombia.DelAnio(anio);
            Assert.Equal(anio >= FestivosColombia.AnioChiquinquira ? 19 : 18, festivos.Count);
            Assert.All(festivos, f => Assert.Equal(anio, f.Fecha.Year));
            Assert.All(festivos.Where(f => f.Tipo is TipoFestivo.TrasladableAlLunes or TipoFestivo.RelativoAPascua),
                f => Assert.Equal(DayOfWeek.Monday, f.Fecha.DayOfWeek));
            Assert.All(festivos.Where(f => f.Tipo == TipoFestivo.TrasladableAlLunes),
                f => Assert.InRange(f.Fecha.DayNumber - f.FechaOriginal.DayNumber, 0, 6));
        }
        // Antes de la Ley 2578 no existía el 9 de julio.
        Assert.DoesNotContain(FestivosColombia.DelAnio(2025), f => f.Nombre.Contains("Chiquinquirá", StringComparison.Ordinal));
    }

    [Fact]
    public void Consultar_por_fecha_y_por_rango()
    {
        Assert.True(FestivosColombia.EsFestivo(D(2027, 8, 7)));
        Assert.False(FestivosColombia.EsFestivo(D(2027, 8, 14)));
        Assert.Equal("Navidad", FestivosColombia.De(D(2026, 12, 25))?.Nombre);
        Assert.Null(FestivosColombia.De(D(1900, 1, 1)));
        // Un rango que cruza el año.
        Assert.Equal([D(2026, 12, 8), D(2026, 12, 25), D(2027, 1, 1), D(2027, 1, 11)],
            FestivosColombia.Entre(D(2026, 12, 1), D(2027, 1, 31)).Select(f => f.Fecha));
        Assert.Empty(FestivosColombia.Entre(D(2027, 2, 1), D(2027, 1, 1)));
    }

    [Theory]
    [InlineData(1983)]
    [InlineData(10000)]
    public void Fuera_de_los_anios_de_la_ley_falla(int anio) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FestivosColombia.DelAnio(anio));
}
