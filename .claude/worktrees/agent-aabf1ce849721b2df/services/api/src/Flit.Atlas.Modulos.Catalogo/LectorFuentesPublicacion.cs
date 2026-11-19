using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>
/// La lista blanca de publicación para los demás módulos (<see cref="IFuentesPublicacion"/>, ATLAS-ADR-0035): el módulo
/// de contratos (y luego documentación y arquitectura) la consulta sin leer las tablas del catálogo.
/// </summary>
internal sealed class LectorFuentesPublicacion(CatalogoDbContext contexto) : IFuentesPublicacion
{
    public async Task<IReadOnlyList<FuentePublicacionLeida>> DelRepositorioAsync(string repositorio, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repositorio);
        // Se guardan en minúsculas: la comparación sin distinguir mayúsculas es una igualdad.
        var clave = repositorio.Trim().ToLowerInvariant();
        var filas = await contexto.FuentesPublicacion.AsNoTracking()
            .Where(f => f.Repositorio == clave)
            .Join(contexto.Aplicativos.AsNoTracking(), f => f.AplicativoId, a => a.Id, (f, a) => new { Fuente = f, a.Slug })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. filas.Select(f => new FuentePublicacionLeida(
            f.Fuente.Id, f.Fuente.AplicativoId, f.Slug, f.Fuente.Repositorio, f.Fuente.Ramas, f.Fuente.Workflow, f.Fuente.Tipos))];
    }
}
