using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Modulos.Soporte.Dominio.Importacion;
using Flit.Atlas.Pruebas.Soporte;
using static Flit.Atlas.Pruebas.Soporte.ExcelFicticio;

namespace Flit.Atlas.Modulos.Soporte.Tests;

/// <summary>
/// HU 2.7: lectura del .xlsx (con el SDK de Open XML) e interpretación de sus filas, sin base. Los archivos se generan en
/// la prueba con las mismas formas que la hoja real (fechas en texto y seriales, Compensado con fecha, texto y ambiguo,
/// nombres sin tildes) y datos ficticios.
/// </summary>
public class ImportadorRotacionTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 23);
    private static readonly DateOnly SabadoFuturo = new(2026, 10, 31);

    private static readonly PersonaConocida Ana = new(Guid.CreateVersion7(), Personas.Ana, true);
    private static readonly PersonaConocida Bruno = new(Guid.CreateVersion7(), Personas.Bruno, true);
    private static readonly PersonaConocida Carla = new(Guid.CreateVersion7(), Personas.Carla, true);
    private static readonly PersonaConocida Diego = new(Guid.CreateVersion7(), Personas.Diego, true);
    private static readonly PersonaConocida[] Directorio = [Ana, Bruno, Carla, Diego];

    private static string ClaveExterna => NombresPersona.Normalizar(Personas.Externa);

    [Theory]
    [InlineData("Pérez  Núñez-Ruiz ", "perez nunez ruiz")]
    [InlineData("ÁLVARO", "alvaro")]
    [InlineData("  ", "")]
    public void Normaliza_sin_tildes_mayusculas_ni_signos(string texto, string esperado) =>
        Assert.Equal(esperado, NombresPersona.Normalizar(texto));

    [Fact]
    public void Un_nombre_sin_tildes_o_parcial_unico_encaja_y_uno_repetido_es_ambiguo()
    {
        Assert.Equal((TipoCoincidencia.Exacta, Carla), Par(NombresPersona.Buscar("carla nunez demo", Directorio)));
        Assert.Equal((TipoCoincidencia.Parcial, Diego), Par(NombresPersona.Buscar("Diego", Directorio)));
        Assert.Equal(TipoCoincidencia.Ninguna, NombresPersona.Buscar("Persona Inexistente", Directorio).Tipo);
        var otraAna = new PersonaConocida(Guid.CreateVersion7(), "Ana Torres Demo", true);
        var ambigua = NombresPersona.Buscar("Ana", [.. Directorio, otraAna]);
        Assert.Equal(TipoCoincidencia.Ambigua, ambigua.Tipo);
        Assert.Equal(2, ambigua.Candidatas.Count);

        static (TipoCoincidencia, PersonaConocida?) Par(Coincidencia c) => (c.Tipo, c.Persona);
    }

    [Fact]
    public void Lee_la_hoja_con_fechas_en_texto_y_seriales_y_salta_filas_vacias()
    {
        var hoja = LectorExcelRotacion.Leer(HojaDeEjemplo(SabadoFuturo));

        Assert.Equal("Rotacion Soporte", hoja.Hoja);
        Assert.Equal(7, hoja.Filas.Count);
        Assert.Equal("2026-06-13", hoja.Filas[0].Fecha.Texto);
        Assert.Equal(2, hoja.Filas[0].Numero);
        Assert.True(hoja.Filas[2].Fecha.FormatoFecha);
        Assert.Equal(Serial(new DateOnly(2026, 6, 27)), hoja.Filas[2].Fecha.Numero);
        // La fecha de Compensado con formato mm-dd-yy también es un serial con formato de fecha.
        Assert.True(hoja.Filas[1].Compensado.FormatoFecha);
        Assert.Equal("Hizo el cierre de mes", hoja.Filas[1].Observacion.Texto);
        Assert.True(hoja.Filas[3].Estado.EstaVacia);
    }

    [Fact]
    public void Busca_los_encabezados_aunque_no_esten_en_la_primera_fila_ni_en_ese_orden()
    {
        var archivo = Crear([["Responsable", "Fecha"], ["Ana", "2026-06-13"]], hoja: "Otra hoja", encabezados: ["Rotación de soporte TI"]);

        var hoja = LectorExcelRotacion.Leer(archivo);

        Assert.Equal("Otra hoja", hoja.Hoja);
        var fila = Assert.Single(hoja.Filas);
        Assert.Equal(("2026-06-13", "Ana"), (fila.Fecha.Texto, fila.Responsable.Texto));
    }

    [Fact]
    public void Rechaza_archivos_vacios_grandes_invalidos_sin_encabezados_o_con_demasiadas_filas()
    {
        Assert.Equal("archivo_vacio", Assert.Throws<LecturaExcelException>(() => LectorExcelRotacion.Leer([])).Codigo);
        Assert.Equal("archivo_grande", Assert.Throws<LecturaExcelException>(() => LectorExcelRotacion.Leer(new byte[LectorExcelRotacion.TamanoMaximo + 1])).Codigo);
        Assert.Equal("archivo_invalido", Assert.Throws<LecturaExcelException>(() => LectorExcelRotacion.Leer("no es un xlsx"u8.ToArray())).Codigo);
        Assert.Equal("sin_encabezados", Assert.Throws<LecturaExcelException>(() => LectorExcelRotacion.Leer(Crear([["a", "b"]], encabezados: ["x", "y"]))).Codigo);
        var muchas = Enumerable.Range(0, LectorExcelRotacion.FilasMaximas + 1).Select(_ => new object?[] { "2026-06-13", "Ana" });
        Assert.Equal("demasiadas_filas", Assert.Throws<LecturaExcelException>(() => LectorExcelRotacion.Leer(Crear(muchas))).Codigo);
    }

    private static PlanRotacion Interpretar(bool crearExterna = true) =>
        InterpreteRotacion.Interpretar(LectorExcelRotacion.Leer(HojaDeEjemplo(SabadoFuturo)).Filas, Directorio, Hoy,
            crearExterna ? new HashSet<string>(StringComparer.Ordinal) { ClaveExterna } : null);

    [Fact]
    public void Interpreta_fechas_nombres_compensaciones_y_reemplazos_y_marca_lo_ambiguo()
    {
        var plan = Interpretar();
        var filas = plan.Filas;

        Assert.All(filas, f => Assert.Null(f.Omitida));
        Assert.Equal(
            [new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 27), new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 18), SabadoFuturo],
            filas.Select(f => f.Fecha!.Value));
        // Nombres: sin tildes y en minúsculas → la persona; «Diego» → la única que encaja.
        Assert.Equal([Ana.Id, Bruno.Id, Carla.Id, Ana.Id, null, Carla.Id, Diego.Id], filas.Select(f => f.Responsable!.Persona?.Id));
        Assert.Equal(TipoCoincidencia.Parcial, filas[6].Responsable!.Tipo);

        // Compensado con fecha → compensación del responsable, pendiente porque el día no ha llegado.
        Assert.Equal(new CompensacionLeida(false, new DateOnly(2026, 11, 8), EstadoCompensacion.Pendiente), filas[1].Compensacion);
        Assert.Null(filas[1].Reemplazo);
        Assert.Equal("Hizo el cierre de mes", filas[1].Observacion);

        // «Diego reemplazo y compensa» → reemplazo y compensación pendiente para Diego, sin fecha.
        Assert.Equal(Diego.Id, filas[2].Reemplazo!.Persona!.Id);
        Assert.Equal(new CompensacionLeida(true, null, EstadoCompensacion.Pendiente), filas[2].Compensacion);
        Assert.Equal(EstadoTurno.Aplicado, filas[2].Estado);
        Assert.False(filas[2].RequiereRevision);

        // «Bruno Reemplazo» sin estado y con la fecha pasada → reemplazo, pero programado y para revisión.
        Assert.Equal(Bruno.Id, filas[3].Reemplazo!.Persona!.Id);
        Assert.Null(filas[3].Compensacion);
        Assert.Equal(EstadoTurno.Programado, filas[3].Estado);
        Assert.True(filas[3].RequiereRevision);

        // Persona que no existe en Atlas: se propone crearla inactiva; confirmada, la fila se importa.
        Assert.True(filas[4].Responsable!.CrearInactiva);
        var nueva = Assert.Single(plan.PersonasNuevas);
        Assert.Equal((ClaveExterna, Personas.Externa, 1, (DateOnly?)new DateOnly(2026, 7, 11), true), (nueva.Clave, nueva.Nombre, nueva.Filas, nueva.UltimaFecha, nueva.Confirmada));

        // Texto que no es fecha ni reemplazo → a la observación, para revisión.
        Assert.True(filas[5].RequiereRevision);
        Assert.Null(filas[5].Compensacion);
        Assert.Equal("Compensado: lo cubrió alguien del equipo", filas[5].Observacion);
        Assert.Single(filas[5].Ambiguedades);

        // Sábado futuro sin estado → programado, sin revisión.
        Assert.Equal(EstadoTurno.Programado, filas[6].Estado);
        Assert.False(filas[6].RequiereRevision);

        Assert.Equal(12, filas.Sum(f => f.Interpretaciones.Count));
        Assert.Equal(2, filas.Count(f => f.RequiereRevision));
    }

    [Fact]
    public void Sin_confirmar_la_persona_nueva_su_fila_no_se_importa()
    {
        var plan = Interpretar(crearExterna: false);

        Assert.False(Assert.Single(plan.PersonasNuevas).Confirmada);
        Assert.Contains("no existe en Atlas", plan.Filas[4].Omitida, StringComparison.Ordinal);
        Assert.Single(plan.Filas, f => f.Omitida is not null);
    }

    [Fact]
    public void Una_compensacion_con_fecha_pasada_ya_esta_tomada_y_un_nombre_ambiguo_se_elige()
    {
        var otraAna = new PersonaConocida(Guid.CreateVersion7(), "Ana Torres Demo", true);
        var filas = LectorExcelRotacion.Leer(Crear([[new DateOnly(2026, 8, 1), "Ana", "Aplicado", "2026-08-10", null]])).Filas;

        var sinElegir = InterpreteRotacion.Interpretar(filas, [.. Directorio, otraAna], Hoy);
        Assert.Equal("El responsable es ambiguo.", sinElegir.Filas[0].Omitida);
        Assert.Equal(2, Assert.Single(sinElegir.NombresAmbiguos).Candidatas.Count);

        var elegida = InterpreteRotacion.Interpretar(filas, [.. Directorio, otraAna], Hoy, null,
            new Dictionary<string, Guid>(StringComparer.Ordinal) { ["ana"] = otraAna.Id });
        var fila = elegida.Filas[0];
        Assert.Null(fila.Omitida);
        Assert.Equal(otraAna.Id, fila.Responsable!.Persona!.Id);
        Assert.Equal(new CompensacionLeida(false, new DateOnly(2026, 8, 10), EstadoCompensacion.Tomada), fila.Compensacion);
    }

    [Theory]
    [InlineData("13/06/2026", 2026, 6, 13, true)]
    [InlineData("2026-06-13", 2026, 6, 13, false)]
    [InlineData("2026-06-13T00:00:00", 2026, 6, 13, true)]
    public void Unifica_las_fechas_en_texto(string texto, int a, int m, int d, bool interpretada)
    {
        var (fecha, nota, error) = InterpreteRotacion.LeerFecha(new CeldaExcel(texto, null, false));
        Assert.Equal(new DateOnly(a, m, d), fecha);
        Assert.Equal(interpretada, nota is not null);
        Assert.Null(error);
    }

    [Fact]
    public void Omite_fechas_invalidas_dias_que_no_son_sabado_filas_repetidas_y_responsables_vacios()
    {
        var filas = LectorExcelRotacion.Leer(Crear(
        [
            ["mañana", "Ana", null, null, null],
            ["2026-06-14", "Ana", null, null, null],
            ["2026-06-13", null, "Aplicado", null, null],
            ["2026-06-13", "Ana Perez Demo", "Aplicado", null, null],
            [new DateOnly(2026, 6, 13), "ana pérez demo", "Aplicado", null, null],
            [5.0, "Ana", null, null, null],
            ["2026-06-20", "Ana", "Inventado", "Bruno reemplazo", null],
        ])).Filas;

        var plan = InterpreteRotacion.Interpretar(filas, Directorio, Hoy);

        Assert.Contains("no es una fecha", plan.Filas[0].Omitida, StringComparison.Ordinal);
        Assert.Contains("no es sábado", plan.Filas[1].Omitida, StringComparison.Ordinal);
        Assert.Equal("La fila no tiene responsable.", plan.Filas[2].Omitida);
        Assert.Null(plan.Filas[3].Omitida);
        Assert.Contains("Fila repetida", plan.Filas[4].Omitida, StringComparison.Ordinal);
        Assert.Contains("no es una fecha de Excel válida", plan.Filas[5].Omitida, StringComparison.Ordinal);
        // Un estado que no se reconoce queda programado, en la observación y para revisión; el reemplazo sí se entiende.
        Assert.Equal(EstadoTurno.Programado, plan.Filas[6].Estado);
        Assert.True(plan.Filas[6].RequiereRevision);
        Assert.Equal(Bruno.Id, plan.Filas[6].Reemplazo!.Persona!.Id);
    }

    [Theory]
    [InlineData(@"yyyy\-mm\-dd;@", true)]
    [InlineData("mm-dd-yy", true)]
    [InlineData("0.00", false)]
    [InlineData("\"día\" 0", false)]
    [InlineData("[h]:mm", false)]
    public void Reconoce_los_formatos_de_fecha(string codigo, bool esFecha) =>
        Assert.Equal(esFecha, LectorExcelRotacion.EsCodigoDeFecha(codigo));
}
