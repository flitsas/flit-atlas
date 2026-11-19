using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>
/// Lectura del catálogo para los demás módulos (<see cref="ILectorAplicativos"/>, ATLAS-ADR-0031): los responsables
/// (HU 2.3) se enlazan por id y slug sin duplicar el catálogo ni leer sus tablas.
/// </summary>
internal sealed class LectorAplicativos(CatalogoDbContext contexto) : ILectorAplicativos
{
    public async Task<IReadOnlyList<AplicativoLeido>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await contexto.Aplicativos.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return [.. filas.Select(a => new AplicativoLeido(a.Id, a.Slug, a.Nombre, Valores.Texto(a.Tipo), a.PadreId,
            Valores.Texto(a.Grupo), Valores.Texto(a.Estado), a.Repo, a.RutaEnRepo))];
    }
}
