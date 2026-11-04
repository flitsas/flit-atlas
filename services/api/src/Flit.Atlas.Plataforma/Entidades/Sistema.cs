using System.Net;

namespace Flit.Atlas.Plataforma.Entidades;

public enum TipoActor
{
    Persona,
    Servicio,
    Sistema,
}

/// <summary>
/// Registro de auditoría: quién, qué, versión, cuándo, IP y organización (prompt maestro §8.9). La tabla es de
/// <b>solo inserción</b>: la base rechaza <c>UPDATE</c>, <c>DELETE</c> y <c>TRUNCATE</c> (migración inicial).
/// </summary>
public sealed class EventoAuditoria
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required DateTimeOffset OcurridoEn { get; init; }

    public required TipoActor ActorTipo { get; init; }

    /// <summary>Id de la persona o del cliente de servicio; vacío para el sistema.</summary>
    public string? ActorId { get; init; }

    /// <summary>Sin llave foránea: el registro sobrevive aunque la organización se borre.</summary>
    public Guid? OrganizacionId { get; init; }

    /// <summary>Acción con forma <c>&lt;modulo&gt;.&lt;accion&gt;</c>, p. ej. <c>identidad.inicio_sesion</c>.</summary>
    public required string Accion { get; init; }

    public required string RecursoTipo { get; init; }

    public string? RecursoId { get; init; }

    public string? Version { get; init; }

    public IPAddress? Ip { get; init; }

    /// <summary>Detalle en JSON. Nunca contraseñas, tokens ni contenido de usuario.</summary>
    public string? Datos { get; init; }

    public string? CorrelacionId { get; init; }
}

/// <summary>Registro y bandera de un módulo (ATLAS-ADR-0016). Cada ambiente tiene su propia base.</summary>
public sealed class Modulo
{
    /// <summary>Id del módulo (<c>soporte</c>, <c>catalogo</c>, …).</summary>
    public required string Id { get; init; }

    public bool Activo { get; set; }

    /// <summary>Organizaciones externas con el módulo habilitado; vacío = ninguna restricción por organización.</summary>
    public List<Guid> OrganizacionesHabilitadas { get; set; } = [];

    public DateTimeOffset ActualizadoEn { get; set; }
}

/// <summary>Configuración de Atlas por clave, con valor en JSON.</summary>
public sealed class Configuracion
{
    public required string Clave { get; init; }

    public required string Valor { get; set; }

    public DateTimeOffset ActualizadoEn { get; set; }

    public Guid? ActualizadoPor { get; set; }
}
