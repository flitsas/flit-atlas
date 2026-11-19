using System.Net;
using Flit.Atlas.Plataforma.Datos;

namespace Flit.Atlas.Plataforma.Entidades;

public enum EstadoInvitacion
{
    Pendiente,
    Aceptada,
    Vencida,
    Revocada,
    Suspendida,
}

/// <summary>Invitación de una persona externa (prompt maestro §6.2). El flujo completo llega en la HU 1.5.</summary>
public sealed class Invitacion : Entidad
{
    public required Guid OrganizacionId { get; set; }

    public required string Correo
    {
        get => _correo;
        set => _correo = value.Trim().ToLowerInvariant();
    }

    private string _correo = string.Empty;

    public required Guid RolId { get; set; }

    /// <summary>Alcance otorgado (aplicativos, paquetes o documentos), en JSON.</summary>
    public required string Alcance { get; set; }

    /// <summary>Permisos extra (exportar, sandbox), en JSON.</summary>
    public string PermisosExtra { get; set; } = "{}";

    /// <summary>Vencimiento del acceso; obligatorio para proveedores (lo valida la HU 1.5).</summary>
    public DateTimeOffset? AccesoVenceEn { get; set; }

    /// <summary>SHA-256 del token del enlace. El token en claro nunca se guarda.</summary>
    public required byte[] TokenHash { get; set; }

    /// <summary>El enlace vence a las 72 h.</summary>
    public required DateTimeOffset TokenVenceEn { get; set; }

    public EstadoInvitacion Estado { get; set; } = EstadoInvitacion.Pendiente;

    public required Guid InvitadoPor { get; set; }

    /// <summary>Persona creada al aceptar.</summary>
    public Guid? PersonaId { get; set; }

    public DateTimeOffset? AceptadaEn { get; set; }
}

public enum DocumentoLegal
{
    Terminos,
    Privacidad,
}

/// <summary>Evidencia de aceptación del aviso de privacidad y los términos (Ley 1581, prompt maestro §6.2).</summary>
public sealed class AceptacionTerminos
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PersonaId { get; init; }

    public required DocumentoLegal Documento { get; init; }

    public required string Version { get; init; }

    public required DateTimeOffset AceptadoEn { get; init; }

    public IPAddress? Ip { get; init; }

    public string? AgenteUsuario { get; init; }
}
