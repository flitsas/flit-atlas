using Flit.Atlas.Modulos.Gia.Datos;

namespace Flit.Atlas.Modulos.Gia.Tests;

/// <summary>Reglas del plan 02 §8 (ATLAS-ADR-0055 §5).</summary>
public class ReglasGiaTests
{
    private static EscenarioRiesgo E(string d, int p, int i, int? p2 = null, int? i2 = null) => new(d, "Escenario de prueba", p, i, null, null, p2, i2);

    private static CasoUso Caso(params string[] categorias) => new()
    {
        Codigo = "caso", Nombre = "Caso", Aplicacion = "App", Proposito = "Propósito", CategoriasDato = [.. categorias],
    };

    [Theory]
    [InlineData(1, "bajo")]
    [InlineData(4, "bajo")]
    [InlineData(5, "medio")]
    [InlineData(9, "medio")]
    [InlineData(10, "alto")]
    [InlineData(15, "alto")]
    [InlineData(16, "critico")]
    [InlineData(25, "critico")]
    public void Los_cortes_de_nivel_son_los_del_plan(int puntaje, string nivel) => Assert.Equal(nivel, ReglasGia.Nivel(puntaje));

    [Fact]
    public void El_nivel_es_el_de_la_peor_dimension_no_el_promedio()
    {
        var (inherente, residual) = ReglasGia.Puntajes([E("D1", 1, 1), E("D2", 1, 1), E("D3", 1, 1), E("D4", 3, 5, 1, 5), E("D5", 1, 1), E("D6", 1, 1)]);

        Assert.Equal(15, inherente);
        Assert.Equal(5, residual);
    }

    [Fact]
    public void El_ejemplo_del_plan_da_alto_inherente_y_medio_residual()
    {
        // Plan 02 §8.5, CU-01.
        var (inherente, residual) = ReglasGia.Puntajes([E("D1", 3, 5, 1, 5), E("D1", 2, 4, 1, 4), E("D2", 3, 4, 2, 3), E("D3", 2, 3, 1, 3), E("D5", 3, 3, 2, 2), E("D6", 2, 3, 1, 2)]);

        Assert.Equal((15, 6), (inherente, residual));
        Assert.Equal("alto", ReglasGia.Nivel(inherente));
        Assert.Equal("medio", ReglasGia.Nivel(residual));
    }

    [Fact]
    public void Sin_residual_se_toma_el_inherente() =>
        Assert.Equal(12, ReglasGia.Puntajes([E("D1", 3, 4)]).Residual);

    [Theory]
    [InlineData("bajo", false, new[] { "responsable_ia" }, 12)]
    [InlineData("bajo", true, new[] { "responsable_ia" }, 12)]
    [InlineData("medio", false, new[] { "responsable_ia" }, 12)]
    [InlineData("medio", true, new[] { "responsable_ia", "opd" }, 12)]
    [InlineData("alto", true, new[] { "comite" }, 6)]
    [InlineData("critico", false, new[] { "comite" }, 6)]
    public void Quien_aprueba_depende_del_nivel_y_de_los_datos(string nivel, bool pii, string[] roles, int meses)
    {
        var regla = ReglasGia.Regla(nivel, pii);

        Assert.Equal(roles, regla.Roles);
        Assert.Equal(meses, regla.MesesRevision);
    }

    [Fact]
    public void Medio_con_datos_personales_necesita_los_dos_votos()
    {
        var regla = ReglasGia.Regla("medio", true);

        Assert.False(ReglasGia.Completa(regla, ["responsable_ia"]));
        Assert.False(ReglasGia.Completa(regla, ["opd", "seguridad"]));
        Assert.True(ReglasGia.Completa(regla, ["opd", "responsable_ia"]));
    }

    [Fact]
    public void Con_residual_critico_no_se_aprueba()
    {
        Assert.True(ReglasGia.Bloqueada("critico"));
        Assert.False(ReglasGia.Bloqueada("alto"));
    }

    [Theory]
    [InlineData("personal", true)]
    [InlineData("sensible", true)]
    [InlineData("interno", false)]
    public void Los_datos_personales_salen_de_las_categorias(string categoria, bool esperado) =>
        Assert.Equal(esperado, ReglasGia.TieneDatosPersonales(Caso(categoria)));

    [Fact]
    public void Los_tipos_de_pii_tambien_cuentan_como_datos_personales()
    {
        var caso = Caso("interno");
        caso.TiposPii = ["placa"];

        Assert.True(ReglasGia.TieneDatosPersonales(caso));
    }

    [Fact]
    public void Alto_con_datos_personales_pide_eipd_humano_aviso_evals_y_rollback()
    {
        var faltan = ReglasGia.FaltantesNivel(Caso("personal"), "alto", null);

        Assert.Equal(["suite de evals", "evaluación de impacto en privacidad aprobada", "humano en el bucle", "aviso al usuario", "plan de rollback"], faltan);
    }

    [Theory]
    [InlineData("borrador")]
    [InlineData("en_revision")]
    [InlineData("rechazada")]
    public void Una_eipd_sin_aprobar_no_cumple(string estado) =>
        Assert.Contains("evaluación de impacto en privacidad aprobada", ReglasGia.FaltantesNivel(Caso("personal"), "alto", estado));

    [Fact]
    public void Un_caso_alto_con_todo_cumple()
    {
        var caso = Caso("personal");
        caso.EvalsRuta = "evals/x";
        caso.HumanoEnBucle = "revision_posterior";
        caso.AvisoUsuario = "Generado con IA";
        caso.PlanRollback = "Volver al prompt anterior";

        Assert.Empty(ReglasGia.FaltantesNivel(caso, "alto", ValoresGia.EipdAprobada));
    }

    [Fact]
    public void Humano_ninguno_no_cumple_en_alto()
    {
        var caso = Caso("interno");
        caso.HumanoEnBucle = "ninguno";

        Assert.Contains("humano en el bucle", ReglasGia.FaltantesNivel(caso, "alto", null));
    }

    [Fact]
    public void Bajo_no_pide_requisitos_extra() => Assert.Empty(ReglasGia.FaltantesNivel(Caso("personal"), "bajo", null));

    [Fact]
    public void Medio_pide_evals() => Assert.Equal(["suite de evals"], ReglasGia.FaltantesNivel(Caso("interno"), "medio", null));

    [Theory]
    [InlineData("aprobado", "en_produccion", true)]
    [InlineData("aprobado", "suspendido", true)]
    [InlineData("en_produccion", "suspendido", true)]
    [InlineData("en_revision", "borrador", true)]
    [InlineData("suspendido", "borrador", true)]
    [InlineData("borrador", "retirado", true)]
    [InlineData("borrador", "aprobado", false)]
    [InlineData("borrador", "en_produccion", false)]
    [InlineData("en_revision", "aprobado", false)]
    [InlineData("suspendido", "en_produccion", false)]
    [InlineData("retirado", "borrador", false)]
    [InlineData("retirado", "retirado", false)]
    public void Aprobar_solo_se_hace_votando(string actual, string destino, bool permitida) =>
        Assert.Equal(permitida, ReglasGia.TransicionPermitida(actual, destino));

    [Fact]
    public void El_codigo_de_incidente_sigue_la_secuencia_del_anio()
    {
        Assert.Equal("INC-IA-2026-001", ReglasGia.CodigoIncidente(2026, []));
        Assert.Equal("INC-IA-2026-003", ReglasGia.CodigoIncidente(2026, ["INC-IA-2026-001", "INC-IA-2026-002", "INC-IA-2025-009"]));
        Assert.Equal("INC-IA-2027-001", ReglasGia.CodigoIncidente(2027, ["INC-IA-2026-002"]));
    }
}
