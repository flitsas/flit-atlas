using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma.Datos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>Filtros del listado y de la búsqueda global, ya validados.</summary>
public sealed record FiltroCatalogo(
    string? Texto = null,
    IReadOnlyList<GrupoAplicativo>? Grupos = null,
    IReadOnlyList<EstadoAplicativo>? Estados = null,
    IReadOnlyList<Criticidad>? Criticidades = null,
    IReadOnlyList<TipoAplicativo>? Tipos = null,
    Guid? PadreId = null);

/// <summary>
/// Consulta del catálogo que comparten el listado y el proveedor de búsqueda global: la filtra PostgreSQL (texto sin
/// tildes ni mayúsculas con <c>translate</c> y <c>lower</c>, como la búsqueda de personas) y la ordena por grupo y por
/// la ruta en la jerarquía.
/// </summary>
internal static class ConsultaCatalogo
{
    /// <summary>Términos que se combinan como máximo (los demás se ignoran).</summary>
    public const int TerminosMaximos = 6;

    public static IQueryable<Aplicativo> Filtrar(IQueryable<Aplicativo> consulta, FiltroCatalogo filtro)
    {
        foreach (var termino in FuncionesSql.NormalizarBusqueda(filtro.Texto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Distinct(StringComparer.Ordinal).Take(TerminosMaximos))
        {
            var patron = FuncionesSql.PatronContiene(termino);
            consulta = consulta.Where(a =>
                EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(a.Nombre, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(a.Slug, patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(a.Descripcion ?? string.Empty, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), patron, FuncionesSql.EscapeLike)
                || EF.Functions.Like(FuncionesSql.Minusculas(a.Repo ?? string.Empty), patron, FuncionesSql.EscapeLike)
                || a.Etiquetas.Contains(termino));
        }
        if (filtro.Grupos is { Count: > 0 } grupos)
        {
            consulta = consulta.Where(a => grupos.Contains(a.Grupo));
        }
        if (filtro.Estados is { Count: > 0 } estados)
        {
            consulta = consulta.Where(a => estados.Contains(a.Estado));
        }
        if (filtro.Criticidades is { Count: > 0 } criticidades)
        {
            consulta = consulta.Where(a => criticidades.Contains(a.Criticidad));
        }
        if (filtro.Tipos is { Count: > 0 } tipos)
        {
            consulta = consulta.Where(a => tipos.Contains(a.Tipo));
        }
        if (filtro.PadreId is { } padre)
        {
            consulta = consulta.Where(a => a.PadreId == padre);
        }
        return consulta;
    }

    /// <summary>Nombre, slug y padre de todo el catálogo: para armar la ruta de cada aplicativo sin una consulta por fila.</summary>
    public static async Task<Jerarquia> JerarquiaAsync(CatalogoDbContext contexto, CancellationToken ct)
    {
        var nodos = await contexto.Aplicativos.AsNoTracking()
            .Select(a => new NodoJerarquia(a.Id, a.Slug, a.Nombre, a.PadreId))
            .ToListAsync(ct).ConfigureAwait(false);
        return new Jerarquia(nodos);
    }
}

internal sealed record NodoJerarquia(Guid Id, string Slug, string Nombre, Guid? PadreId);

/// <summary>El árbol del catálogo en memoria (unos cientos de filas como mucho).</summary>
internal sealed class Jerarquia(IReadOnlyList<NodoJerarquia> nodos)
{
    private readonly Dictionary<Guid, NodoJerarquia> _porId = nodos.ToDictionary(n => n.Id);
    private readonly ILookup<Guid?, NodoJerarquia> _hijos = nodos.ToLookup(n => n.PadreId);

    /// <summary>Del producto (raíz) al padre directo; vacío para un producto.</summary>
    public IReadOnlyList<NodoJerarquia> Ancestros(Guid id)
    {
        var lista = new List<NodoJerarquia>();
        var actual = _porId.GetValueOrDefault(id)?.PadreId;
        // Tope de profundidad: la jerarquía tiene tres niveles; el tope solo protege de datos corruptos.
        while (actual is { } padre && lista.Count < 10 && _porId.TryGetValue(padre, out var nodo))
        {
            lista.Insert(0, nodo);
            actual = nodo.PadreId;
        }
        return lista;
    }

    public int Hijos(Guid id) => _hijos[id].Count();

    /// <summary>Los descendientes de un aplicativo (hijos, nietos…).</summary>
    public IEnumerable<Guid> Descendientes(Guid id)
    {
        foreach (var hijo in _hijos[id])
        {
            yield return hijo.Id;
            foreach (var nieto in Descendientes(hijo.Id))
            {
                yield return nieto;
            }
        }
    }

    /// <summary>Clave para ordenar: los nombres de la raíz a la hoja, sin distinguir mayúsculas.</summary>
    public string Ruta(Guid id, string nombre) =>
        string.Join(" › ", Ancestros(id).Select(a => a.Nombre).Append(nombre)).ToLowerInvariant();
}
