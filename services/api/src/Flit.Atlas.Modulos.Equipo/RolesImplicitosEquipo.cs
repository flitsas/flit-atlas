using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Equipo;

/// <summary>
/// Rol implícito por propiedad (prompt maestro §7.2, ATLAS-ADR-0023 y ATLAS-ADR-0031): el responsable principal o
/// secundario <b>vigente</b> de un aplicativo recibe <see cref="RolesAtlas.Responsable"/> con alcance
/// <c>aplicativo:&lt;id&gt;</c>, solo sobre ese aplicativo. Los módulos que evalúan por recurso (el catálogo con
/// <c>atlas.catalogo.editar</c> sobre el aplicativo y sus ancestros) lo reconocen sin cambios. Lo calcula la plataforma en
/// cada petición: un cambio del reparto vale en la siguiente.
/// </summary>
internal sealed class RolesImplicitosEquipo(EquipoDbContext equipo, TimeProvider reloj) : IProveedorRolesImplicitos
{
    public async Task<IReadOnlyList<RolImplicito>> ObtenerAsync(Guid personaId, CancellationToken ct = default)
    {
        var hoy = CalendarioBogota.Hoy(reloj);
        var aplicativos = await equipo.Responsabilidades.AsNoTracking()
            .Where(r => r.PersonaId == personaId && r.VigenteDesde <= hoy && (r.VigenteHasta == null || r.VigenteHasta >= hoy))
            .Select(r => r.AplicativoId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. aplicativos.Select(id => new RolImplicito(RolesAtlas.Responsable, new AlcanceAsignacion(TiposAlcance.Aplicativo, id.ToString("D"))))];
    }
}
