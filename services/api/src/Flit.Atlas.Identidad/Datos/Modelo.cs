using System.Net;
using Microsoft.AspNetCore.Identity;

namespace Flit.Atlas.Identidad.Datos;

/// <summary>
/// Usuario de Atlas (empleado o externo). Su <see cref="IdentityUser{TKey}.Id"/> es el <c>sub</c> de la sesión: uuid v7
/// propio, nunca el <c>oid</c> de Entra (ATLAS-ADR-0004 §3). Enlaza con <c>plataforma_persona</c>.
/// </summary>
public sealed class UsuarioAtlas : IdentityUser<Guid>
{
    public UsuarioAtlas()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public required Guid PersonaId { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>Rol de ASP.NET Core Identity. No se usa para autorizar: los roles de Atlas viven en <c>plataforma_*</c>.</summary>
public sealed class RolIdentity : IdentityRole<Guid>;

/// <summary>
/// Sesión del BFF por dispositivo (ATLAS-ADR-0004 §6). La cookie solo lleva el <see cref="Id"/> cifrado; el ticket con
/// los claims vive aquí, protegido con Data Protection.
/// </summary>
public sealed class Sesion
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UsuarioId { get; init; }

    public required DateTimeOffset CreadaEn { get; init; }

    public required DateTimeOffset UltimoUsoEn { get; set; }

    public required DateTimeOffset ExpiraEn { get; set; }

    /// <summary>Tope absoluto: la sesión no se puede extender más allá de esta fecha.</summary>
    public required DateTimeOffset ExpiraAbsolutaEn { get; init; }

    /// <summary>Ticket de autenticación serializado y protegido con Data Protection.</summary>
    public required byte[] Ticket { get; set; }

    /// <summary>Resumen del navegador y el sistema (p. ej. «Chrome en Android»), para «dispositivos conectados».</summary>
    public string? AgenteResumen { get; init; }

    /// <summary>
    /// Última IP desde la que se usó la sesión (la del cliente, detrás de los proxies confiables). Es un dato personal:
    /// columna <c>@pii</c>, nunca va a los logs.
    /// </summary>
    public IPAddress? Ip { get; set; }

    /// <summary>
    /// Sello de seguridad del usuario cuando se creó la sesión. Si el sello del usuario cambia (roles, suspensión,
    /// vencimiento, cierre administrativo), la sesión deja de valer (ATLAS-ADR-0022).
    /// </summary>
    public required string SelloSeguridad { get; set; }

    public DateTimeOffset? RevocadaEn { get; set; }

    public string? MotivoRevocacion { get; set; }
}

/// <summary>
/// Evento de seguridad del módulo (prompt maestro §6 y §9): inicio, fallo, bloqueo, cierre remoto, suspensión,
/// reautenticación, límite de tasa… Tabla de <b>solo inserción</b>. Cada evento también queda en
/// <c>plataforma_auditoria</c> (ATLAS-ADR-0022).
/// </summary>
public sealed class EventoSeguridad
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required DateTimeOffset OcurridoEn { get; init; }

    /// <summary>Tipo sin el prefijo del módulo, p. ej. <c>cuenta_bloqueada</c> (ver <c>TiposEventoSeguridad</c>).</summary>
    public required string Tipo { get; init; }

    /// <summary>Usuario afectado. Sin llave foránea: el evento sobrevive al usuario.</summary>
    public Guid? UsuarioId { get; init; }

    public Guid? PersonaId { get; init; }

    public Guid? SesionId { get; init; }

    /// <summary>Quién hizo la acción cuando no es el propio afectado (p. ej. el administrador que suspende).</summary>
    public Guid? ActorPersonaId { get; init; }

    public IPAddress? Ip { get; init; }

    public string? AgenteResumen { get; init; }

    /// <summary>Detalle en JSON. Nunca contraseñas, tokens ni la IP.</summary>
    public string? Datos { get; init; }
}
