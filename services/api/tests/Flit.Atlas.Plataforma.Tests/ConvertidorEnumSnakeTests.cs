using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;

namespace Flit.Atlas.Plataforma.Tests;

public class ConvertidorEnumSnakeTests
{
    [Theory]
    [InlineData(OrigenAsignacion.GrupoEntra, "grupo_entra")]
    [InlineData(OrigenAsignacion.Manual, "manual")]
    public void Convierte_en_los_dos_sentidos(OrigenAsignacion valor, string texto)
    {
        var convertidor = new ConvertidorEnumSnake<OrigenAsignacion>();

        Assert.Equal(texto, convertidor.ConvertToProvider(valor));
        Assert.Equal(valor, convertidor.ConvertFromProvider(texto));
    }

    [Fact]
    public void El_largo_es_el_del_valor_mas_largo() =>
        Assert.Equal("organizacion".Length, ConvertidorEnumSnake<TipoAlcance>.Largo);
}
