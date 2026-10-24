using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Modulos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Equipo;

/// <summary>
/// Responsables vigentes (principal o secundario, hoy en Bogotá) de los aplicativos, para los demás módulos (HU 4.8,
/// ATLAS-ADR-0031 y ATLAS-ADR-0046): con las mismas reglas de vigencia que el rol implícito.
/// </summary>
internal sealed class LectorResponsablesEquipo(EquipoDbContext equipo, TimeProvider reloj) : ILectorResponsables
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DeAplicativosAsync(IReadOnlyCollection<Guid> aplicativos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aplicativos);
        if (aplicativos.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }
        var hoy = CalendarioBogota.Hoy(reloj);
        var ids = aplicativos.Distinct().ToList();
        var filas = await equipo.Responsabilidades.AsNoTracking()
            .Where(r => ids.Contains(r.AplicativoId) && r.VigenteDesde <= hoy && (r.VigenteHasta == null || r.VigenteHasta >= hoy))
            .Select(r => new { r.AplicativoId, r.PersonaId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return filas.GroupBy(f => f.AplicativoId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(f => f.PersonaId).Distinct()]);
    }
}
