using Flit.Atlas.Plataforma.Datos;

namespace Flit.Atlas.Plataforma.Entidades;

public enum TipoOrganizacion
{
    Interna,
    Cliente,
    Proveedor,
}

public enum EstadoOrganizacion
{
    Activa,
    Suspendida,
}

/// <summary>FLIT (interna) o una organización externa: cliente, organismo o proveedor (prompt maestro §6.2).</summary>
public sealed class Organizacion : Entidad
{
    public required string Nombre { get; set; }

    public required TipoOrganizacion Tipo { get; set; }

    public EstadoOrganizacion Estado { get; set; } = EstadoOrganizacion.Activa;

    /// <summary>Permite que un <c>ext.admin_organizacion</c> invite a colegas de su organización (§7.3).</summary>
    public bool AdminOrganizacionHabilitado { get; set; }
}

public enum TipoPersona
{
    Empleado,
    Externo,
}

public enum EstadoPersona
{
    Activa,
    Inactiva,
    Suspendida,
}

/// <summary>Empleado (enlazado a Entra ID) o persona externa invitada (prompt maestro §6 y §8.2).</summary>
public sealed class Persona : Entidad
{
    public required TipoPersona Tipo { get; set; }

    public required Guid OrganizacionId { get; set; }

    public required string Nombre { get; set; }

    /// <summary>Siempre en minúsculas: es único.</summary>
    public required string Correo
    {
        get => _correo;
        set => _correo = value.Trim().ToLowerInvariant();
    }

    private string _correo = string.Empty;

    /// <summary><c>oid</c> del usuario en Entra ID; solo empleados.</summary>
    public Guid? EntraOid { get; set; }

    public string? Cargo { get; set; }

    public string? Area { get; set; }

    /// <summary>Solo visible con <c>atlas.equipo.ver_contacto</c>.</summary>
    public string? Telefono { get; set; }

    public EstadoPersona Estado { get; set; } = EstadoPersona.Activa;
}
