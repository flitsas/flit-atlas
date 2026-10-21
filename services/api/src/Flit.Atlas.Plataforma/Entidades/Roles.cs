using Flit.Atlas.Plataforma.Datos;

namespace Flit.Atlas.Plataforma.Entidades;

public enum TipoRol
{
    Interno,
    Externo,
}

/// <summary>Rol de Atlas (<c>atlas.*</c> o <c>ext.*</c>, prompt maestro §7). Los permisos de cada rol llegan en la HU 1.4.</summary>
public sealed class Rol : Entidad
{
    public required string Codigo { get; set; }

    public required string Nombre { get; set; }

    public required TipoRol Tipo { get; set; }

    public string? Descripcion { get; set; }
}

public enum TipoAlcance
{
    Global,
    Organizacion,
    Aplicativo,
    Paquete,
    Documento,
    Ambiente,
}

public enum OrigenAsignacion
{
    Manual,
    GrupoEntra,
}

/// <summary>Una persona tiene un rol con un alcance y una vigencia (prompt maestro §7).</summary>
public sealed class AsignacionRol : Entidad
{
    public required Guid PersonaId { get; set; }

    public required Guid RolId { get; set; }

    /// <summary>Organización de la asignación; obligatoria para roles externos (lo valida la HU 1.4).</summary>
    public Guid? OrganizacionId { get; set; }

    public TipoAlcance AlcanceTipo { get; set; } = TipoAlcance.Global;

    /// <summary>Id del recurso del alcance (aplicativo, paquete, documento o ambiente); vacío si es global.</summary>
    public string? AlcanceId { get; set; }

    public required DateTimeOffset VigenteDesde { get; set; }

    /// <summary>Al vencer, el acceso se corta sin intervención (§6.2).</summary>
    public DateTimeOffset? VigenteHasta { get; set; }

    public OrigenAsignacion Origen { get; set; } = OrigenAsignacion.Manual;

    public Guid? AsignadoPor { get; set; }
}

/// <summary>Grupo de Entra ID que otorga un rol de Atlas (p. ej. <c>FLIT-Developers</c> → <c>atlas.desarrollador</c>).</summary>
public sealed class MapeoGrupoEntra : Entidad
{
    /// <summary>Object id del grupo en Entra ID.</summary>
    public required Guid GrupoId { get; set; }

    public required string GrupoNombre { get; set; }

    public required Guid RolId { get; set; }
}
