using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>
/// Proveedor de la búsqueda global (§8.9) para el catálogo: la misma consulta del listado, solo si la persona tiene
/// <c>atlas.catalogo.ver</c>. El endpoint que combina los proveedores aún no existe (ATLAS-ADR-0026, pendiente): este
/// proveedor queda registrado para cuando llegue.
/// </summary>
internal sealed class BusquedaCatalogo(CatalogoDbContext contexto, IPermisosDeSesion permisosDeSesion) : IProveedorBusqueda
{
    public string Proveedor => ModuloCatalogo.IdModulo;

    public IReadOnlyList<string> Tipos { get; } = [.. Valores.Tipos.Keys];

    public async Task<IReadOnlyList<ResultadoBusqueda>> BuscarAsync(ConsultaBusqueda consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        if (string.IsNullOrWhiteSpace(consulta.Texto) || consulta.Limite <= 0)
        {
            return [];
        }
        var permisos = await permisosDeSesion.ObtenerAsync(consulta.Usuario, ct).ConfigureAwait(false);
        if (!permisos.Tiene(PermisosCatalogo.Ver))
        {
            return [];
        }
        var filas = await ConsultaCatalogo.Filtrar(contexto.Aplicativos.AsNoTracking(), new FiltroCatalogo(Texto: consulta.Texto))
            .OrderBy(a => a.Tipo).ThenBy(a => a.Nombre)
            .Take(consulta.Limite)
            .ToListAsync(ct).ConfigureAwait(false);
        var jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct).ConfigureAwait(false);
        return [.. filas.Select(a => new ResultadoBusqueda(
            Valores.Texto(a.Tipo),
            a.Id.ToString(),
            a.Nombre,
            Resumen(a, jerarquia),
            $"/catalogo/{a.Slug}"))];
    }

    /// <summary>La ruta en la jerarquía («FLIT 2.0 › Comparendos») o, en un producto, su descripción.</summary>
    private static string? Resumen(Aplicativo a, Jerarquia jerarquia)
    {
        var ancestros = jerarquia.Ancestros(a.Id);
        return ancestros.Count > 0 ? string.Join(" › ", ancestros.Select(n => n.Nombre)) : a.Descripcion;
    }
}
