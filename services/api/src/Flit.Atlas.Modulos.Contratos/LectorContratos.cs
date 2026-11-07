using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Contratos;

/// <summary>Un contrato por su código para los demás módulos (ATLAS-ADR-0053 §7), sin que nadie más lea estas tablas.</summary>
internal sealed class LectorContratos(ContratosDbContext contexto) : ILectorContratos
{
    public async Task<ContratoLeido?> PorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var contrato = await contexto.Contratos.AsNoTracking().Where(c => c.Codigo == codigo).OrderBy(c => c.Nombre).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Codigo, c.Nombre }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (contrato is null)
        {
            return null;
        }
        var ultima = await contexto.Versiones.AsNoTracking().Where(v => v.ContratoId == contrato.Id).OrderByDescending(v => v.PublicadoEn)
            .Select(v => v.Version).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new ContratoLeido(contrato.Id, contrato.Codigo, contrato.Nombre, ultima);
    }
}
