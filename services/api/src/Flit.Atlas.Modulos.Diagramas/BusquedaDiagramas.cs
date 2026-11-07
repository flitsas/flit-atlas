using Flit.Atlas.Modulos.Diagramas.Datos;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Diagramas;

/// <summary>Búsqueda global (§8.9): por título o código, con el predicado de visibilidad en la consulta. Tipo <c>diagrama</c>.</summary>
internal sealed class BusquedaDiagramas(
    DiagramasDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector) : IProveedorBusqueda
{
    public string Proveedor => ModuloDiagramas.IdModulo;

    public IReadOnlyList<string> Tipos { get; } = ["diagrama"];

    public async Task<IReadOnlyList<ResultadoBusqueda>> BuscarAsync(ConsultaBusqueda consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        if (string.IsNullOrWhiteSpace(consulta.Texto) || consulta.Limite <= 0)
        {
            return [];
        }
        var permisos = await permisosDeSesion.ObtenerAsync(consulta.Usuario, ct).ConfigureAwait(false);
        if (!permisos.Tiene(PermisosDiagramas.Ver))
        {
            return [];
        }
        var acceso = await AccesoDiagramas.CalcularAsync(consulta.Usuario, permisosDeSesion, lector, ct).ConfigureAwait(false);
        var filas = await PorTexto(acceso.Filtrar(contexto.Diagramas.AsNoTracking()), consulta.Texto)
            .OrderBy(d => d.Titulo).Take(consulta.Limite)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. filas.Select(d => new ResultadoBusqueda("diagrama", d.Id.ToString(), d.Titulo, ReglasDiagramas.Texto(d.Tipo), $"/diagramas/{d.Id}"))];
    }

    internal static IQueryable<Diagrama> PorTexto(IQueryable<Diagrama> consulta, string texto)
    {
        var termino = FuncionesSql.PatronContiene(FuncionesSql.NormalizarBusqueda(texto));
        return consulta.Where(d => EF.Functions.Like(FuncionesSql.Minusculas(d.Codigo), termino, FuncionesSql.EscapeLike)
            || EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(d.Titulo, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), termino, FuncionesSql.EscapeLike));
    }
}
