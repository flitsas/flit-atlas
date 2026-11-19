using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;

namespace Flit.Atlas.Plataforma.Entidades;

public enum TipoRol
{
    Interno,
    Externo,

    /// <summary>Lo da la propiedad de un recurso (responsable de un aplicativo, §7.2); no se asigna a mano.</summary>
    Implicito,
}

/// <summary>
/// Rol de Atlas (<c>atlas.*</c> o <c>ext.*</c>, prompt maestro §7). La lista y los permisos de cada rol se definen en
/// el código (<see cref="RolesAtlas"/> y los manifiestos de permisos, ATLAS-ADR-0023); el migrador los aplica a la base.
/// </summary>
public sealed class Rol : Entidad
{
    public required string Codigo { get; set; }

    public required string Nombre { get; set; }

    public required TipoRol Tipo { get; set; }

    public string? Descripcion { get; set; }
}

/// <summary>
/// Permiso de un manifiesto (<c>atlas.&lt;modulo&gt;.&lt;accion&gt;</c>, §7.4), proyectado a la base por el migrador
/// para las consultas que filtran en SQL. La fuente es el código: aquí no se edita.
/// </summary>
public sealed class Permiso
{
    public required string Codigo { get; init; }

    /// <summary>Id del módulo que lo declara (<c>plataforma</c>, <c>catalogo</c>, …).</summary>
    public required string Modulo { get; set; }

    public required string Descripcion { get; set; }

    public DateTimeOffset ActualizadoEn { get; set; }
}

/// <summary>Un rol concede un permiso (proyección de la matriz rol × permiso del código).</summary>
public sealed class RolPermiso
{
    public required Guid RolId { get; init; }

    public required string PermisoCodigo { get; init; }
}

public enum OrigenAsignacion
{
    Manual,
    GrupoEntra,

    /// <summary>La crea la aceptación de una invitación (HU 1.5).</summary>
    Invitacion,
}

/// <summary>
/// Una persona tiene un rol con un alcance y una vigencia (prompt maestro §7). Fuera de la vigencia no concede nada: los
/// permisos se calculan en cada petición con la hora actual (ATLAS-ADR-0023).
/// </summary>
public sealed class AsignacionRol : Entidad
{
    public required Guid PersonaId { get; set; }

    public required Guid RolId { get; set; }

    /// <summary>Organización de la asignación; obligatoria (y la misma de la persona) para los roles externos.</summary>
    public Guid? OrganizacionId { get; set; }

    /// <summary>
    /// Tipo de alcance: <c>global</c>, <c>organizacion</c>, <c>aplicativo</c>, <c>paquete</c>, <c>documento</c>,
    /// <c>ambiente</c> o el que declare un módulo en su manifiesto (<see cref="TiposAlcance"/>).
    /// </summary>
    public string AlcanceTipo { get; set; } = TiposAlcance.Global;

    /// <summary>Id del recurso del alcance (aplicativo, paquete, documento o ambiente); vacío si es global.</summary>
    public string? AlcanceId { get; set; }

    public required DateTimeOffset VigenteDesde { get; set; }

    /// <summary>Al vencer, el acceso se corta sin intervención (§6.2).</summary>
    public DateTimeOffset? VigenteHasta { get; set; }

    public OrigenAsignacion Origen { get; set; } = OrigenAsignacion.Manual;

    public Guid? AsignadoPor { get; set; }

    /// <summary>
    /// Cuándo el vigilante de vencimientos registró el vencimiento (auditoría e invalidación de sesiones). El corte del
    /// acceso no depende de esto: ocurre en la primera petición después de <see cref="VigenteHasta"/>.
    /// </summary>
    public DateTimeOffset? VencimientoProcesadoEn { get; set; }
}

/// <summary>Grupo de Entra ID que otorga un rol de Atlas (p. ej. <c>FLIT-Developers</c> → <c>atlas.desarrollador</c>).</summary>
public sealed class MapeoGrupoEntra : Entidad
{
    /// <summary>Object id del grupo en Entra ID.</summary>
    public required Guid GrupoId { get; set; }

    public required string GrupoNombre { get; set; }

    public required Guid RolId { get; set; }
}
