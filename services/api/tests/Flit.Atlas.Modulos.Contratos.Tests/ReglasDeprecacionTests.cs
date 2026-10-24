using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Deprecacion;

namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>HU 4.8 (ATLAS-ADR-0046): qué recordatorio toca, qué versión reemplaza y los textos sin datos ajenos.</summary>
public sealed class ReglasDeprecacionTests
{
    private static readonly DateOnly Retiro = new(2026, 12, 31);
    private static readonly int[] Dias = [30, 7];
    private static readonly int[] Normalizados = [14, 3];

    [Theory]
    // Avisada con tiempo: 30 días antes sale el de 30; 7 antes, el de 7; entre medio, ninguno nuevo (el de 30 ya salió).
    [InlineData(45, 60, null)]
    [InlineData(30, 60, 30)]
    [InlineData(20, 60, 30)]
    [InlineData(7, 60, 7)]
    [InlineData(0, 60, 7)]
    // Avisada ya dentro de la ventana de 30: no repite con el de 30, pero sí el de 7.
    [InlineData(25, 28, null)]
    [InlineData(6, 28, 7)]
    // Avisada a 5 días: un solo aviso, ningún recordatorio.
    [InlineData(5, 5, null)]
    [InlineData(1, 5, null)]
    public void El_recordatorio_de_hoy_es_el_de_la_ventana_mas_cercana_que_empezo_despues_del_aviso(int faltan, int avisadaAntes, int? esperado)
    {
        var hoy = Retiro.AddDays(-faltan);
        Assert.Equal(esperado, ReglasDeprecacion.RecordatorioDeHoy(Retiro, hoy, Retiro.AddDays(-avisadaAntes), Dias));
    }

    [Fact]
    public void El_reemplazo_es_la_estable_vigente_mas_alta_posterior()
    {
        (string, bool, EstadoVersion)[] versiones =
        [
            ("1.0.0", true, EstadoVersion.Deprecada), ("1.1.0", true, EstadoVersion.Vigente), ("2.0.0", true, EstadoVersion.Vigente),
            ("3.0.0-rc.1", false, EstadoVersion.Vigente), ("2.5.0", true, EstadoVersion.Deprecada), ("0.9.0", true, EstadoVersion.Vigente),
        ];
        Assert.Equal("2.0.0", ReglasDeprecacion.Reemplazo("1.0.0", versiones));
        Assert.Null(ReglasDeprecacion.Reemplazo("2.0.0", versiones));
        Assert.Null(ReglasDeprecacion.Reemplazo("no-semver", versiones));
    }

    [Fact]
    public void Los_textos_llevan_la_fecha_dd_mm_aaaa_y_solo_los_paquetes_de_la_persona()
    {
        var hoy = Retiro.AddDays(-30);
        var (titulo, cuerpo) = ReglasDeprecacion.TextoExterno(MomentoAviso.Recordatorio, "1.0.0", "API Demo", Retiro, hoy, "2.0.0", ["Paquete Uno", "Paquete Dos"]);
        Assert.StartsWith("Recordatorio:", titulo, StringComparison.Ordinal);
        Assert.Contains("31/12/2026", cuerpo, StringComparison.Ordinal);
        Assert.Contains("faltan 30 días", cuerpo, StringComparison.Ordinal);
        Assert.Contains("los paquetes «Paquete Uno» y «Paquete Dos»", cuerpo, StringComparison.Ordinal);
        Assert.Contains("La reemplaza la versión 2.0.0.", cuerpo, StringComparison.Ordinal);
        var (_, interno) = ReglasDeprecacion.TextoInterno(MomentoAviso.Deprecacion, "1.0.0", "API Demo", Retiro, hoy, null, 3);
        Assert.Contains("Atlas les avisa a 3 organizaciones", interno, StringComparison.Ordinal);
        Assert.Contains("Todavía no hay una versión estable que la reemplace.", interno, StringComparison.Ordinal);
        Assert.Equal(
            "La versión 1.0.0 de API Demo está deprecada y se retira el 31/12/2026. La reemplaza la versión 2.0.0.",
            ReglasDeprecacion.MensajePortal("1.0.0", "API Demo", Retiro, "2.0.0"));
    }

    [Fact]
    public void Los_dias_de_recordatorio_se_normalizan()
    {
        Assert.Equal(Dias, new OpcionesAvisosDeprecacion().Dias);
        Assert.Equal(Normalizados, new OpcionesAvisosDeprecacion { DiasRecordatorio = [3, 14, 3, 0, -1] }.Dias);
    }
}
