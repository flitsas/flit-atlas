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

    public DateTimeOffset? RevocadaEn { get; set; }

    public string? MotivoRevocacion { get; set; }
}
