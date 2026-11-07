using System.Security.Claims;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>
/// Qué documentos puede ver y tocar la persona (prompt maestro §7.1 y §7.2, ATLAS-ADR-0053 §6). Se calcula en cada petición
/// y se traduce a un predicado que va en <b>todas</b> las consultas, incluida la búsqueda. Los externos no ven nada hasta
/// que exista compartir (HU 5.6), aunque <c>atlas.docs.ver</c> ya se les conceda.
/// </summary>
internal sealed class AccesoDocumentos
{
    private static readonly HashSet<string> RolesInternos =
        RolesAtlas.Todos.Where(r => r.Tipo == TipoRol.Interno).Select(r => r.Codigo).ToHashSet(StringComparer.Ordinal);

    /// <summary>§7.1: «roles técnicos». Soporte, negocio y lector se quedan en <c>interno_negocio</c> (§7.2).</summary>
    private static readonly HashSet<string> RolesTecnicos =
        new([RolesAtlas.Superadmin, RolesAtlas.LiderTecnico, RolesAtlas.Desarrollador], StringComparer.Ordinal);

    /// <summary>§7.1: administración, líder técnico (decisión del líder, 06/11/2026) y gobierno de IA.</summary>
    private static readonly HashSet<string> RolesRestringido =
        new([RolesAtlas.Superadmin, RolesAtlas.LiderTecnico, RolesAtlas.GobiernoIa], StringComparer.Ordinal);

    private readonly PermisosEfectivos _permisos;
    private readonly IReadOnlyDictionary<Guid, AplicativoLeido> _aplicativos;
    private readonly HashSet<Guid> _deResponsable;

    private AccesoDocumentos(PermisosEfectivos permisos, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        _permisos = permisos;
        _aplicativos = aplicativos;
        _deResponsable = permisos.Concesiones
            .Where(c => c.Rol == RolesAtlas.Responsable && c.Alcance.Tipo == TiposAlcance.Aplicativo && Guid.TryParse(c.Alcance.Id, out _))
            .Select(c => Guid.Parse(c.Alcance.Id!)).ToHashSet();

        var globales = permisos.Concesiones.Where(c => c.Alcance.EsGlobal).Select(c => c.Rol).ToHashSet(StringComparer.Ordinal);
        var niveles = new List<VisibilidadDocumento>();
        if (globales.Overlaps(RolesInternos))
        {
            niveles.Add(VisibilidadDocumento.PublicoClientes);
            niveles.Add(VisibilidadDocumento.InternoNegocio);
        }
        if (globales.Overlaps(RolesTecnicos))
        {
            niveles.Add(VisibilidadDocumento.InternoTi);
            VeBorradores = true;
        }
        if (globales.Overlaps(RolesRestringido))
        {
            niveles.Add(VisibilidadDocumento.Restringido);
        }
        Niveles = [.. niveles];
        EditaGlobal = permisos.TieneEn(PermisosDocumentacion.Editar, AlcanceAsignacion.Global);
        Propios = [.. aplicativos.Keys.Where(EsResponsable)];
    }

    public static async Task<AccesoDocumentos> CalcularAsync(
        ClaimsPrincipal usuario, IPermisosDeSesion permisosDeSesion, ILectorAplicativos lector, CancellationToken ct)
    {
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct).ConfigureAwait(false);
        var aplicativos = (await lector.ListarAsync(ct).ConfigureAwait(false)).ToDictionary(a => a.Id);
        return new AccesoDocumentos(permisos, aplicativos);
    }

    /// <summary>Niveles que ve por su rol, en cualquier aplicativo.</summary>
    public VisibilidadDocumento[] Niveles { get; }

    /// <summary>Si ve las versiones de repo sin aprobar (plan 03 §18.3).</summary>
    public bool VeBorradores { get; }

    /// <summary>Si puede editar en cualquier aplicativo: ve los borradores de los niveles que alcanza.</summary>
    public bool EditaGlobal { get; }

    /// <summary>Aplicativos de los que es responsable (o de un ancestro): ve todos sus documentos (§7.2).</summary>
    public Guid[] Propios { get; }

    public IReadOnlyDictionary<Guid, AplicativoLeido> Aplicativos => _aplicativos;

    private bool EsResponsable(Guid aplicativoId) => ReglasDocumentos.ConAncestros(aplicativoId, _aplicativos).Any(_deResponsable.Contains);

    /// <summary>El predicado de lectura. Un documento archivado solo lo ven quienes pueden editarlo.</summary>
    public IQueryable<Documento> Filtrar(DocumentacionDbContext contexto, IQueryable<Documento> consulta)
    {
        var niveles = Niveles;
        var propios = Propios;
        var editaGlobal = EditaGlobal;
        var borradores = VeBorradores;
        return consulta.Where(d => propios.Contains(d.AplicativoId)
            || (niveles.Contains(d.Visibilidad)
                && (editaGlobal
                    || (d.Estado != EstadoDocumento.Archivado
                        && contexto.Versiones.Any(v => v.DocumentoId == d.Id && v.Numero == d.VersionVigente
                            && niveles.Contains(v.Visibilidad) && (!v.EsBorrador || borradores))))));
    }

    /// <summary>Las versiones que puede leer: cada una con su propia visibilidad, no la del documento.</summary>
    public IQueryable<VersionDocumento> FiltrarVersiones(IQueryable<VersionDocumento> consulta, Guid aplicativoId)
    {
        if (Propios.Contains(aplicativoId))
        {
            return consulta;
        }
        var niveles = Niveles;
        var borradores = VeBorradores;
        return consulta.Where(v => niveles.Contains(v.Visibilidad) && (!v.EsBorrador || borradores));
    }

    /// <summary>Puede crear y editar en este aplicativo: <c>atlas.docs.editar</c> global o por responsable.</summary>
    public bool PuedeEditar(Guid aplicativoId) =>
        _permisos.TieneEn(PermisosDocumentacion.Editar, AlcanceAsignacion.Global)
        || ReglasDocumentos.ConAncestros(aplicativoId, _aplicativos).Any(id => _permisos.TieneEn(PermisosDocumentacion.Editar, ReglasDocumentos.Alcance(id)));

    /// <summary>Puede aprobar en este aplicativo: <c>atlas.docs.aprobar</c> global o por responsable.</summary>
    public bool PuedeAprobar(Guid aplicativoId) =>
        _permisos.TieneEn(PermisosDocumentacion.Aprobar, AlcanceAsignacion.Global)
        || ReglasDocumentos.ConAncestros(aplicativoId, _aplicativos).Any(id => _permisos.TieneEn(PermisosDocumentacion.Aprobar, ReglasDocumentos.Alcance(id)));

    /// <summary>Es principal o secundario del aplicativo o de un ancestro.</summary>
    public bool EsResponsableDe(Guid aplicativoId) => Propios.Contains(aplicativoId);

    /// <summary>Puede dejar un documento en este nivel: uno que después podrá seguir viendo.</summary>
    public bool AlcanzaNivel(Guid aplicativoId, VisibilidadDocumento nivel) => Propios.Contains(aplicativoId) || Niveles.Contains(nivel);
}
