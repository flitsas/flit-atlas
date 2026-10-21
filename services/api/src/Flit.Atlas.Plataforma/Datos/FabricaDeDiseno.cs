using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Atlas.Plataforma.Datos;

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>: generar una migración no necesita una base real.
/// <code>dotnet ef migrations add &lt;Nombre&gt; -p services/api/src/Flit.Atlas.Plataforma -o Datos/Migraciones</code>
/// </summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<PlataformaDbContext>
{
    public PlataformaDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, "Host=localhost;Database=atlas_diseno;Username=diseno;Password=diseno");
        return new PlataformaDbContext(opciones.Options);
    }
}
