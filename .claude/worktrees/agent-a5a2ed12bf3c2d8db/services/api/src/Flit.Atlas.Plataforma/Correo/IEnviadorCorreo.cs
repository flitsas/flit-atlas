using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma.Correo;

/// <summary>
/// Un correo que envía Atlas (invitaciones, recuperación de cuenta y, desde F2, notificaciones; prompt maestro §6.2 y
/// §8.9). <see cref="Html"/> y <see cref="Texto"/> llevan el mismo contenido; el texto es para clientes sin HTML.
/// </summary>
/// <param name="Para">Correo del destinatario (dato personal: nunca va a los logs).</param>
public sealed record MensajeCorreo(string Para, string? NombrePara, string Asunto, string Html, string Texto);

/// <summary>
/// Envío de correo de Atlas (ATLAS-ADR-0025): Microsoft Graph <c>sendMail</c> desde el buzón compartido o SMTP. Las
/// implementaciones nunca registran el cuerpo (puede llevar enlaces de un solo uso) ni el destinatario.
/// </summary>
public interface IEnviadorCorreo
{
    /// <summary><c>false</c> si el ambiente no tiene correo configurado: el envío se omite y quien llama lo informa.</summary>
    bool Habilitado { get; }

    /// <summary>Envía el mensaje; lanza <see cref="CorreoNoEnviadoException"/> si el servidor lo rechaza o no responde.</summary>
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default);
}

/// <summary>El correo no salió. El mensaje es apto para los logs: no lleva destinatario ni contenido.</summary>
public sealed class CorreoNoEnviadoException(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

/// <summary>Sección <c>Plataforma:Correo</c>.</summary>
public sealed class OpcionesCorreo
{
    public const string Seccion = "Plataforma:Correo";

    public const string ProveedorSmtp = "smtp";
    public const string ProveedorGraph = "graph";

    /// <summary><c>graph</c>, <c>smtp</c> o vacío (sin correo: solo se permite en local y dev).</summary>
    public string? Proveedor { get; set; }

    /// <summary>Buzón remitente (el buzón compartido de Atlas). Con Graph, es el buzón desde el que se envía.</summary>
    public string? Remitente { get; set; }

    public string NombreRemitente { get; set; } = "Flit Atlas";

    public OpcionesSmtp Smtp { get; set; } = new();

    /// <summary>Tiempo máximo de un envío; el correo sale dentro de la petición (el enlace no puede ir a una cola).</summary>
    public TimeSpan Tiempo { get; set; } = TimeSpan.FromSeconds(15);
}

public sealed class OpcionesSmtp
{
    public string? Host { get; set; }

    public int Puerto { get; set; } = 587;

    /// <summary><c>starttls</c> (por defecto), <c>ssl</c> o <c>ninguna</c> (solo para el servidor de pruebas local).</summary>
    public string Seguridad { get; set; } = "starttls";

    public string? Usuario { get; set; }

    /// <summary>Secreto: SealedSecret en qa y pdn.</summary>
    public string? Clave { get; set; }
}

/// <summary>Sin proveedor configurado (local sin servidor de pruebas): no envía nada y lo deja dicho en el log.</summary>
public sealed partial class EnviadorDesactivado(ILogger<EnviadorDesactivado> log) : IEnviadorCorreo
{
    public bool Habilitado => false;

    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        // Ni el destinatario ni el cuerpo: solo que no se envió.
        NoEnviado(log);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Correo no enviado: el ambiente no tiene Plataforma__Correo__Proveedor")]
    private static partial void NoEnviado(ILogger log);
}
