using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>
/// Búsqueda full-text en español (HU 5.5, ATLAS-ADR-0053 §7): solo sobre la versión vigente de cada documento y con el
/// predicado de visibilidad en la consulta. Tipo <c>documento</c>, como declara el manifiesto de la PWA.
/// </summary>
internal sealed class BusquedaDocumentacion(
    DocumentacionDbContext contexto, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector) : IProveedorBusqueda
{
    public string Proveedor => ModuloDocumentacion.IdModulo;

    public IReadOnlyList<string> Tipos { get; } = ["documento"];

    public async Task<IReadOnlyList<ResultadoBusqueda>> BuscarAsync(ConsultaBusqueda consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        if (string.IsNullOrWhiteSpace(consulta.Texto) || consulta.Limite <= 0)
        {
            return [];
        }
        var permisos = await permisosDeSesion.ObtenerAsync(consulta.Usuario, ct).ConfigureAwait(false);
        if (!permisos.Tiene(PermisosDocumentacion.Ver))
        {
            return [];
        }
        var acceso = await AccesoDocumentos.CalcularAsync(consulta.Usuario, permisosDeSesion, lector, ct).ConfigureAwait(false);
        var filas = await Vigentes(contexto, acceso, consulta.Texto)
            .Select(x => new { x.Documento.Id, x.Documento.Titulo, x.Documento.Tipo, x.Rango })
            .OrderByDescending(x => x.Rango).ThenBy(x => x.Titulo).Take(consulta.Limite)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. filas.Select(f => new ResultadoBusqueda("documento", f.Id.ToString(), f.Titulo, f.Tipo, $"/documentacion/{f.Id}"))];
    }

    /// <summary>Documentos visibles cuya versión vigente, también visible, coincide con el texto.</summary>
    internal static IQueryable<ResultadoTexto> Vigentes(DocumentacionDbContext contexto, AccesoDocumentos acceso, string texto)
    {
        var termino = FuncionesSql.NormalizarBusqueda(texto);
        var niveles = acceso.Niveles;
        var propios = acceso.Propios;
        var borradores = acceso.VeBorradores;
        return acceso.Filtrar(contexto, contexto.Documentos.AsNoTracking())
            .Where(d => d.Estado != EstadoDocumento.Archivado)
            .Join(contexto.Versiones.AsNoTracking(), d => new { Id = d.Id, Numero = d.VersionVigente }, v => new { Id = v.DocumentoId, Numero = (int?)v.Numero }, (d, v) => new { d, v })
            .Where(x => propios.Contains(x.d.AplicativoId) || (niveles.Contains(x.v.Visibilidad) && (!x.v.EsBorrador || borradores)))
            .Where(x => EF.Property<NpgsqlTsVector>(x.v, DocumentacionDbContext.ColumnaBusqueda).Matches(EF.Functions.PlainToTsQuery("spanish", termino)))
            .Select(x => new ResultadoTexto
            {
                Documento = x.d,
                Rango = EF.Property<NpgsqlTsVector>(x.v, DocumentacionDbContext.ColumnaBusqueda).Rank(EF.Functions.PlainToTsQuery("spanish", termino)),
            });
    }

    /// <summary>Con propiedades y no con constructor: EF Core solo sigue componiendo la consulta sobre una proyección así.</summary>
    internal sealed class ResultadoTexto
    {
        public required Documento Documento { get; init; }

        public required float Rango { get; init; }
    }
}
