using System.Security.Claims;
using Flit.Atlas.Modulos.Diagramas.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Modulos.Diagramas;

/// <summary>
/// Qué diagramas puede ver y tocar la persona (prompt maestro §7.1 y §7.2, ATLAS-ADR-0054 §3). Se calcula en cada petición y se
/// traduce a un predicado que va en <b>todas</b> las consultas. Los externos no ven nada: sus roles no dan ningún nivel.
/// </summary>
internal sealed class AccesoDiagramas
{
    private static readonly HashSet<string> RolesInternos =
        RolesAtlas.Todos.Where(r => r.Tipo == TipoRol.Interno).Select(r => r.Codigo).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> RolesTecnicos =
        new([RolesAtlas.Superadmin, RolesAtlas.LiderTecnico, RolesAtlas.Desarrollador], StringComparer.Ordinal);

    /// <summary>§7.1: administración, líder técnico (decisión del líder, 06/11/2026) y gobierno de IA.</summary>
    private static readonly HashSet<string> RolesRestringido =
        new([RolesAtlas.Superadmin, RolesAtlas.LiderTecnico, RolesAtlas.GobiernoIa], StringComparer.Ordinal);

    private readonly PermisosEfectivos _permisos;
    private readonly IReadOnlyDictionary<Guid, AplicativoLeido> _aplicativos;

    private AccesoDiagramas(PermisosEfectivos permisos, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        _permisos = permisos;
        _aplicativos = aplicativos;
        var deResponsable = permisos.Concesiones
            .Where(c => c.Rol == RolesAtlas.Responsable && c.Alcance.Tipo == TiposAlcance.Aplicativo && Guid.TryParse(c.Alcance.Id, out _))
            .Select(c => Guid.Parse(c.Alcance.Id!)).ToHashSet();

        var globales = permisos.Concesiones.Where(c => c.Alcance.EsGlobal).Select(c => c.Rol).ToHashSet(StringComparer.Ordinal);
        var niveles = new List<VisibilidadDiagrama>();
        if (globales.Overlaps(RolesInternos))
        {
            niveles.Add(VisibilidadDiagrama.InternoNegocio);
        }
        if (globales.Overlaps(RolesTecnicos))
        {
            niveles.Add(VisibilidadDiagrama.InternoTi);
        }
        if (globales.Overlaps(RolesRestringido))
        {
            niveles.Add(VisibilidadDiagrama.Restringido);
        }
        Niveles = [.. niveles];
        Propios = [.. aplicativos.Keys.Where(id => ReglasDiagramas.ConAncestros(id, aplicativos).Any(deResponsable.Contains))];
    }

    public static async Task<AccesoDiagramas> CalcularAsync(
        ClaimsPrincipal usuario, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct).ConfigureAwait(false);
        var aplicativos = (await lector.ListarAsync(ct).ConfigureAwait(false)).ToDictionary(a => a.Id);
        return new AccesoDiagramas(permisos, aplicativos);
    }

    /// <summary>Niveles que ve por su rol, en cualquier aplicativo.</summary>
    public VisibilidadDiagrama[] Niveles { get; }

    /// <summary>Aplicativos de los que es responsable (o de un ancestro): ve todos sus diagramas (§7.2).</summary>
    public Guid[] Propios { get; }

    public IReadOnlyDictionary<Guid, AplicativoLeido> Aplicativos => _aplicativos;

    /// <summary>El predicado de lectura de los diagramas.</summary>
    public IQueryable<Diagrama> Filtrar(IQueryable<Diagrama> consulta)
    {
        var niveles = Niveles;
        var propios = Propios;
        return consulta.Where(d => propios.Contains(d.AplicativoId) || niveles.Contains(d.Visibilidad));
    }

    /// <summary>Las versiones que puede leer: cada una con su propia visibilidad, no la del diagrama.</summary>
    public IQueryable<VersionDiagrama> FiltrarVersiones(IQueryable<VersionDiagrama> consulta, Guid aplicativoId)
    {
        if (Propios.Contains(aplicativoId))
        {
            return consulta;
        }
        var niveles = Niveles;
        return consulta.Where(v => niveles.Contains(v.Visibilidad));
    }

    /// <summary>Puede crear y editar en este aplicativo: <c>atlas.diagramas.editar</c> global o por responsable.</summary>
    public bool PuedeEditar(Guid aplicativoId) =>
        _permisos.TieneEn(PermisosDiagramas.Editar, AlcanceAsignacion.Global)
        || ReglasDiagramas.ConAncestros(aplicativoId, _aplicativos).Any(id => _permisos.TieneEn(PermisosDiagramas.Editar, new AlcanceAsignacion(TiposAlcance.Aplicativo, id.ToString("D"))));

    /// <summary>Puede dejar un diagrama en este nivel: uno que después podrá seguir viendo.</summary>
    public bool AlcanzaNivel(Guid aplicativoId, VisibilidadDiagrama nivel) => Propios.Contains(aplicativoId) || Niveles.Contains(nivel);
}
