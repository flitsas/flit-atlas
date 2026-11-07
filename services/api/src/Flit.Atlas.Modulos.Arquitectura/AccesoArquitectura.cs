using System.Security.Claims;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Modulos.Arquitectura;

/// <summary>
/// Qué publicaciones puede ver la persona (prompt maestro §7.1, ATLAS-ADR-0056 §3). Se traduce a un predicado que va en todas las
/// consultas. Un externo no recibe ningún nivel aunque tenga <c>atlas.arquitectura.ver</c>: <c>ext.asesor_comercial</c> no ve nada.
/// </summary>
internal sealed class AccesoArquitectura
{
    private static readonly HashSet<string> RolesTecnicos =
        new([RolesAtlas.Superadmin, RolesAtlas.LiderTecnico, RolesAtlas.Desarrollador], StringComparer.Ordinal);

    /// <summary>§7.1: administración, líder técnico y gobierno de IA (decisión del líder, 06/11/2026, ATLAS-ADR-0053 §6).</summary>
    private static readonly HashSet<string> RolesRestringido =
        new([RolesAtlas.Superadmin, RolesAtlas.LiderTecnico, RolesAtlas.GobiernoIa], StringComparer.Ordinal);

    public AccesoArquitectura(PermisosEfectivos permisos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        var globales = permisos.Concesiones.Where(c => c.Alcance.EsGlobal).Select(c => c.Rol).ToHashSet(StringComparer.Ordinal);
        var niveles = new List<VisibilidadArquitectura>();
        if (globales.Overlaps(RolesTecnicos))
        {
            niveles.Add(VisibilidadArquitectura.InternoTi);
        }
        if (globales.Overlaps(RolesRestringido))
        {
            niveles.Add(VisibilidadArquitectura.Restringido);
        }
        Niveles = [.. niveles];
        VeDespliegue = permisos.Tiene(PermisosArquitectura.VerDespliegue) && Niveles.Length > 0;
        VeContratos = permisos.Tiene(PermisosArquitectura.VerContratos);
    }

    public static async Task<AccesoArquitectura> CalcularAsync(ClaimsPrincipal usuario, IPermisosDeSesion permisosDeSesion, CancellationToken ct) =>
        new(await permisosDeSesion.ObtenerAsync(usuario, ct).ConfigureAwait(false));

    public VisibilidadArquitectura[] Niveles { get; }

    /// <summary>Tiene <c>ver_despliegue</c>: sin él, el modelo se entrega sin vistas ni nodos de despliegue.</summary>
    public bool VeDespliegue { get; }

    public bool VeContratos { get; }

    public IQueryable<PublicacionArquitectura> Filtrar(IQueryable<PublicacionArquitectura> consulta)
    {
        var niveles = Niveles;
        return consulta.Where(p => niveles.Contains(p.Visibilidad));
    }

    public IQueryable<HallazgoDrift> Filtrar(IQueryable<HallazgoDrift> consulta)
    {
        var niveles = Niveles;
        return consulta.Where(h => niveles.Contains(h.Visibilidad));
    }
}
