using System.Net;
using Flit.Atlas.Plataforma.Datos;

namespace Flit.Atlas.Plataforma.Entidades;

/// <summary>
/// Estado guardado de una invitación (HU 1.5, ATLAS-ADR-0025). El vencimiento del enlace y el del acceso no se guardan
/// como estado: se calculan con <see cref="Invitacion.TokenVenceEn"/> y <see cref="Invitacion.AccesoVenceEn"/> (así no
/// dependen de un proceso que los marque).
/// </summary>
public enum EstadoInvitacion
{
    Pendiente,
    Aceptada,

    /// <summary>Reservado: el vencimiento se calcula al leer (ver <see cref="EstadoInvitacion"/>).</summary>
    Vencida,
    Revocada,
    Suspendida,
}

/// <summary>Invitación de una persona externa (prompt maestro §6.2, HU 1.5).</summary>
public sealed class Invitacion : Entidad
{
    public required Guid OrganizacionId { get; set; }

    public required string Correo
    {
        get => _correo;
        set => _correo = value.Trim().ToLowerInvariant();
    }

    private string _correo = string.Empty;

    /// <summary>Nombre del invitado, si quien invita lo conoce; al aceptar, el invitado escribe el suyo.</summary>
    public string? Nombre { get; set; }

    public required Guid RolId { get; set; }

    /// <summary>
    /// Alcance otorgado (aplicativos, paquetes o documentos), en JSON: arreglo de <c>{ "tipo", "id" }</c>. Vacío = todo lo
    /// que se comparta con la organización.
    /// </summary>
    public required string Alcance { get; set; }

    /// <summary>Permisos extra (<c>exportar</c>, <c>sandbox</c>), en JSON: arreglo de códigos.</summary>
    public string PermisosExtra { get; set; } = "[]";

    /// <summary>Vencimiento del acceso: obligatorio para proveedores y, para todos, dentro del máximo configurado.</summary>
    public DateTimeOffset? AccesoVenceEn { get; set; }

    /// <summary>SHA-256 del token del enlace. El token en claro nunca se guarda.</summary>
    public required byte[] TokenHash { get; set; }

    /// <summary>El enlace vence a las 72 h de cada envío.</summary>
    public required DateTimeOffset TokenVenceEn { get; set; }

    public EstadoInvitacion Estado { get; set; } = EstadoInvitacion.Pendiente;

    public required Guid InvitadoPor { get; set; }

    /// <summary>Persona creada al aceptar.</summary>
    public Guid? PersonaId { get; set; }

    public DateTimeOffset? AceptadaEn { get; set; }

    /// <summary>Último envío del correo que salió bien; vacío si el último intento falló.</summary>
    public DateTimeOffset? EnviadaEn { get; set; }

    /// <summary>Cuántos correos se intentaron enviar (el primero y los reenvíos).</summary>
    public int Envios { get; set; }

    /// <summary>Último cambio de estado (suspensión, renovación o revocación) y quién lo hizo.</summary>
    public DateTimeOffset? EstadoCambiadoEn { get; set; }

    public Guid? EstadoCambiadoPor { get; set; }
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

    /// <summary>SHA-256 (hex) del texto exacto que se mostró, para probar qué se aceptó aunque el texto cambie.</summary>
    public string? HashContenido { get; init; }

    /// <summary>Invitación con la que se aceptó; vacía si fue en un ingreso posterior (versión nueva).</summary>
    public Guid? InvitacionId { get; init; }

    public required DateTimeOffset AceptadoEn { get; init; }

    public IPAddress? Ip { get; init; }

    public string? AgenteUsuario { get; init; }
}
