using Flit.Atlas.Modulos.Soporte.Dominio;

namespace Flit.Atlas.Modulos.Soporte.Tests;

/// <summary>
/// CA de la HU 2.8: equidad (máximo un turno de diferencia por persona en la ventana) en un equipo de una persona por
/// sábado y en uno de dos, ausencias, festivos y turnos que no cambian. El generador es puro: no necesita base.
/// </summary>
public class GeneradorRotacionTests
{
    private static readonly DateOnly Desde = new(2026, 10, 24);

    private static List<Guid> Personas(int cuantas) => [.. Enumerable.Range(0, cuantas).Select(_ => Guid.NewGuid())];

    private static EntradaGeneracion Entrada(
        IReadOnlyList<Guid> participantes, int meses = 3, IReadOnlyList<TurnoFijo>? fijos = null, IReadOnlyList<RangoAusencia>? ausencias = null,
        IReadOnlyDictionary<DateOnly, string>? festivos = null, ReglaFestivos regla = ReglaFestivos.TurnoNormal, Guid? ultima = null,
        DateOnly? desde = null, int minimo = 1, int maximo = 1)
    {
        var inicio = desde ?? Desde;
        return new(inicio, inicio.AddMonths(meses), participantes, fijos ?? [], ausencias ?? [], festivos ?? new Dictionary<DateOnly, string>(),
            regla, ultima, minimo, maximo);
    }

    private static Guid Unica(SabadoPropuesto s) => Assert.Single(s.Personas.ToList());

    public static TheoryData<int, int, int> Ventanas() => new()
    {
        // participantes, meses, personas por sábado
        { 1, 1, 1 }, { 2, 1, 1 }, { 3, 3, 1 }, { 4, 6, 1 }, { 5, 12, 1 }, { 7, 5, 1 }, { 9, 12, 1 }, { 13, 2, 1 },
        { 2, 3, 2 }, { 3, 3, 2 }, { 4, 6, 2 }, { 5, 12, 2 }, { 7, 5, 2 }, { 8, 12, 2 }, { 11, 2, 2 },
    };

    [Theory]
    [MemberData(nameof(Ventanas))]
    public void Sin_ausencias_nadie_queda_con_mas_de_un_turno_de_diferencia(int participantes, int meses, int porSabado)
    {
        var personas = Personas(participantes);

        var resultado = GeneradorRotacion.Generar(Entrada(personas, meses, minimo: porSabado, maximo: porSabado));

        Assert.All(resultado.Sabados, s => Assert.Equal(DayOfWeek.Saturday, s.Fecha.DayOfWeek));
        Assert.All(resultado.Sabados, s =>
        {
            Assert.Equal(DecisionSabado.Asignado, s.Decision);
            Assert.Equal(porSabado, s.Nuevas.Count);
            Assert.Equal(porSabado, s.Nuevas.Distinct().Count());
        });
        Assert.InRange(resultado.Desbalance, 0, 1);
        Assert.Equal(resultado.Sabados.Count * porSabado, resultado.TurnosPorPersona.Values.Sum());
        Assert.Equal(personas.Order(), resultado.TurnosPorPersona.Keys.Order());
    }

    [Fact]
    public void La_rotacion_sigue_el_orden_y_continua_despues_del_ultimo_turno()
    {
        var personas = Personas(3);

        var desdeCero = GeneradorRotacion.Generar(Entrada(personas, meses: 1));
        var continuada = GeneradorRotacion.Generar(Entrada(personas, meses: 1, ultima: personas[0]));

        Assert.Equal(Esperado(personas, 0, desdeCero.Sabados.Count), desdeCero.Sabados.Select(Unica));
        Assert.Equal(Esperado(personas, 1, continuada.Sabados.Count), continuada.Sabados.Select(Unica));
    }

    [Fact]
    public void Con_dos_por_sabado_la_rotacion_avanza_de_dos_en_dos()
    {
        var personas = Personas(4);

        var resultado = GeneradorRotacion.Generar(Entrada(personas, meses: 1, minimo: 2, maximo: 2));

        Assert.Equal([personas[0], personas[1]], resultado.Sabados[0].Nuevas);
        Assert.Equal([personas[2], personas[3]], resultado.Sabados[1].Nuevas);
        Assert.Equal([personas[0], personas[1]], resultado.Sabados[2].Nuevas);
    }

    /// <summary>La rotación en orden desde la posición <paramref name="inicio"/>.</summary>
    private static List<Guid> Esperado(List<Guid> personas, int inicio, int cuantos) =>
        [.. Enumerable.Range(0, cuantos).Select(i => personas[(inicio + i) % personas.Count])];

    [Fact]
    public void Es_determinista_la_misma_entrada_da_la_misma_propuesta()
    {
        var personas = Personas(4);
        var ausencias = new[] { new RangoAusencia(personas[1], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20)) };

        var a = GeneradorRotacion.Generar(Entrada(personas, 6, ausencias: ausencias, maximo: 2));
        var b = GeneradorRotacion.Generar(Entrada(personas, 6, ausencias: ausencias, maximo: 2));

        Assert.Equal(a.Sabados.Select(s => (s.Fecha, s.Decision, string.Join(',', s.Nuevas))), b.Sabados.Select(s => (s.Fecha, s.Decision, string.Join(',', s.Nuevas))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Una_persona_ausente_no_recibe_turno_en_su_ausencia_y_se_pone_al_dia_al_volver(int porSabado)
    {
        var personas = Personas(5);
        // Vacaciones de tres semanas en noviembre de 2026: cubre los sábados 7, 14 y 21.
        var vacaciones = new RangoAusencia(personas[2], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 22));

        var resultado = GeneradorRotacion.Generar(Entrada(personas, 6, ausencias: [vacaciones], minimo: porSabado, maximo: porSabado));

        Assert.DoesNotContain(resultado.Sabados, s => s.Nuevas.Contains(personas[2]) && vacaciones.Cubre(s.Fecha));
        Assert.Contains(resultado.Sabados, s => s.Nuevas.Contains(personas[2]) && s.Fecha > vacaciones.Hasta);
        // En una ventana de seis meses alcanza a ponerse al día.
        Assert.InRange(resultado.Desbalance, 0, 1);
    }

    [Fact]
    public void Una_incapacidad_que_cubre_un_solo_sabado_lo_pasa_al_siguiente_de_la_rotacion()
    {
        var personas = Personas(3);
        var incapacidad = new RangoAusencia(personas[0], Desde, Desde); // 24/10/2026 es sábado.

        var resultado = GeneradorRotacion.Generar(Entrada(personas, 3, ausencias: [incapacidad]));

        Assert.Equal(personas[1], Unica(resultado.Sabados[0]));
        Assert.InRange(resultado.Desbalance, 0, 1);
    }

    [Fact]
    public void Si_no_se_llega_al_minimo_el_sabado_queda_sin_disponibles_con_los_que_haya()
    {
        var personas = Personas(2);

        var nadie = GeneradorRotacion.Generar(Entrada(personas, 1, ausencias: [.. personas.Select(p => new RangoAusencia(p, Desde, Desde))]));
        var unoDeDos = GeneradorRotacion.Generar(Entrada(personas, 1, ausencias: [new RangoAusencia(personas[0], Desde, Desde)], minimo: 2, maximo: 2));

        Assert.Equal(DecisionSabado.SinDisponibles, nadie.Sabados[0].Decision);
        Assert.Empty(nadie.Sabados[0].Nuevas);
        Assert.All(nadie.Sabados.Skip(1), s => Assert.Equal(DecisionSabado.Asignado, s.Decision));
        Assert.Equal(DecisionSabado.SinDisponibles, unoDeDos.Sabados[0].Decision);
        Assert.Equal([personas[1]], unoDeDos.Sabados[0].Nuevas);
        Assert.Equal(1, unoDeDos.Sabados[0].Faltan);
    }

    [Fact]
    public void Un_equipo_de_uno_a_dos_asigna_hasta_dos_y_con_uno_disponible_cumple_el_minimo()
    {
        var personas = Personas(3);

        var resultado = GeneradorRotacion.Generar(Entrada(personas, 1, ausencias: [new RangoAusencia(personas[0], Desde, Desde), new RangoAusencia(personas[1], Desde, Desde)], minimo: 1, maximo: 2));

        Assert.Equal(DecisionSabado.Asignado, resultado.Sabados[0].Decision);
        Assert.Equal([personas[2]], resultado.Sabados[0].Nuevas);
        Assert.All(resultado.Sabados.Skip(1), s => Assert.Equal(2, s.Nuevas.Count));
    }

    [Fact]
    public void Los_turnos_fijos_no_cambian_y_cuentan_para_la_equidad()
    {
        var personas = Personas(3);
        // Dos turnos ya aplicados o confirmados de la primera persona.
        var fijos = new[] { new TurnoFijo(Desde, personas[0]), new TurnoFijo(Desde.AddDays(7), personas[0]) };

        var resultado = GeneradorRotacion.Generar(Entrada(personas, 3, fijos: fijos));

        Assert.Equal(DecisionSabado.Fijo, resultado.Sabados[0].Decision);
        Assert.Equal([personas[0]], resultado.Sabados[0].Fijas);
        Assert.Equal(DecisionSabado.Fijo, resultado.Sabados[1].Decision);
        Assert.InRange(resultado.Desbalance, 0, 1);
        Assert.Equal(resultado.Sabados.Count, resultado.TurnosPorPersona.Values.Sum());
    }

    [Fact]
    public void Con_dos_por_sabado_un_fijo_ocupa_un_puesto_y_se_completa_el_otro()
    {
        var personas = Personas(4);

        var resultado = GeneradorRotacion.Generar(Entrada(personas, 2, fijos: [new TurnoFijo(Desde, personas[3])], minimo: 2, maximo: 2));

        var primero = resultado.Sabados[0];
        Assert.Equal(DecisionSabado.Asignado, primero.Decision);
        Assert.Equal([personas[3]], primero.Fijas);
        Assert.Single(primero.Nuevas);
        Assert.DoesNotContain(personas[3], primero.Nuevas);
        Assert.InRange(resultado.Desbalance, 0, 1);
    }

    [Fact]
    public void Un_fijo_de_alguien_ausente_se_marca_para_revisar()
    {
        var personas = Personas(2);

        var resultado = GeneradorRotacion.Generar(Entrada(personas, 1,
            fijos: [new TurnoFijo(Desde, personas[0])], ausencias: [new RangoAusencia(personas[0], Desde, Desde)]));

        Assert.True(resultado.Sabados[0].AusenteEnFijo);
    }

    [Fact]
    public void Un_sabado_festivo_se_salta_o_se_asigna_segun_la_regla()
    {
        var personas = Personas(3);
        var desde = new DateOnly(2027, 4, 15);
        var festivos = FestivosColombia.Entre(desde, desde.AddMonths(1)).ToDictionary(f => f.Fecha, f => f.Nombre);
        Assert.Contains(new DateOnly(2027, 5, 1), festivos.Keys); // Día del Trabajo, sábado.

        var sinTurno = GeneradorRotacion.Generar(Entrada(personas, 1, festivos: festivos, regla: ReglaFestivos.SinTurno, desde: desde, maximo: 2));
        var normal = GeneradorRotacion.Generar(Entrada(personas, 1, festivos: festivos, regla: ReglaFestivos.TurnoNormal, desde: desde));

        var primeroDeMayo = sinTurno.Sabados.Single(s => s.Fecha == new DateOnly(2027, 5, 1));
        Assert.Equal(DecisionSabado.Festivo, primeroDeMayo.Decision);
        Assert.Equal("Día del Trabajo", primeroDeMayo.Festivo);
        Assert.Empty(primeroDeMayo.Personas);
        var asignado = normal.Sabados.Single(s => s.Fecha == new DateOnly(2027, 5, 1));
        Assert.Equal(DecisionSabado.Asignado, asignado.Decision);
        Assert.Equal("Día del Trabajo", asignado.Festivo);
        Assert.InRange(sinTurno.Desbalance, 0, 1);
    }

    [Fact]
    public void Sin_participantes_no_asigna_a_nadie()
    {
        var resultado = GeneradorRotacion.Generar(Entrada([], 1));

        Assert.All(resultado.Sabados, s => Assert.Equal(DecisionSabado.SinDisponibles, s.Decision));
        Assert.Equal(0, resultado.Desbalance);
    }
}
