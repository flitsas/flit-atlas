using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Busqueda;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Contratos;

/// <summary>
/// Proveedor de la búsqueda global (§8.9) para los contratos: contratos por nombre, código o descripción, y operaciones
/// del índice (<c>contratos_operacion</c>) por clave, operationId o resumen, solo con <c>atlas.contratos.ver_internos</c>.
/// Tipos <c>contrato</c> y <c>operacion</c>, como declara el manifiesto de la PWA. Queda registrado para cuando llegue el
/// endpoint que combina los proveedores (ATLAS-ADR-0026, pendiente).
/// </summary>
internal sealed class BusquedaContratos(ContratosDbContext contexto, IPermisosDeSesion permisosDeSesion) : IProveedorBusqueda
{
    public string Proveedor => ModuloContratos.IdModulo;

    public IReadOnlyList<string> Tipos { get; } = ["contrato", "operacion"];

    public async Task<IReadOnlyList<ResultadoBusqueda>> BuscarAsync(ConsultaBusqueda consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        if (string.IsNullOrWhiteSpace(consulta.Texto) || consulta.Limite <= 0)
        {
            return [];
        }
        var permisos = await permisosDeSesion.ObtenerAsync(consulta.Usuario, ct).ConfigureAwait(false);
        if (!permisos.Tiene(PermisosContratos.VerInternos))
        {
            return [];
        }

        var contratos = await EndpointsContratos.FiltrarTexto(contexto.Contratos.AsNoTracking(), consulta.Texto)
            .OrderBy(c => c.Nombre).Take(consulta.Limite)
            .ToListAsync(ct).ConfigureAwait(false);
        var resultados = contratos.Select(c => new ResultadoBusqueda("contrato", c.Id.ToString(), c.Nombre, $"{Valores.Texto(c.Tipo)} · {c.Codigo}",
            $"/contratos?contrato={c.Id}")).ToList();

        var restantes = consulta.Limite - resultados.Count;
        if (restantes > 0)
        {
            var termino = FuncionesSql.PatronContiene(FuncionesSql.NormalizarBusqueda(consulta.Texto));
            // Solo la última versión de cada contrato: buscar en todas repetiría la misma operación por versión.
            var ultimas = contexto.Versiones.AsNoTracking()
                .Where(v => !contexto.Versiones.Any(o => o.ContratoId == v.ContratoId && o.PublicadoEn > v.PublicadoEn));
            var operaciones = await contexto.Operaciones.AsNoTracking()
                .Join(ultimas, o => o.VersionId, v => v.Id, (o, v) => new { o.Id, o.Clave, o.Resumen, o.OperationId, v.ContratoId, v.Version })
                .Where(x => EF.Functions.Like(FuncionesSql.Minusculas(x.Clave), termino, FuncionesSql.EscapeLike)
                    || EF.Functions.Like(FuncionesSql.Minusculas(x.OperationId ?? string.Empty), termino, FuncionesSql.EscapeLike)
                    || EF.Functions.Like(FuncionesSql.Minusculas(FuncionesSql.Traducir(x.Resumen ?? string.Empty, FuncionesSql.ConTildes, FuncionesSql.SinTildes)), termino, FuncionesSql.EscapeLike))
                .OrderBy(x => x.Clave).Take(restantes)
                .ToListAsync(ct).ConfigureAwait(false);
            resultados.AddRange(operaciones.Select(o => new ResultadoBusqueda("operacion", o.Id.ToString(), o.Clave, o.Resumen,
                $"/contratos?contrato={o.ContratoId}&version={Uri.EscapeDataString(o.Version)}")));
        }
        return resultados;
    }
}
