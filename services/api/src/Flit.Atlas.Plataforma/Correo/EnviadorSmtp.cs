using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Flit.Atlas.Plataforma.Correo;

/// <summary>
/// Envío por SMTP con MailKit (MIT). En local apunta al servidor de pruebas del AppHost (Mailpit); en qa y pdn, al
/// relé SMTP que el líder habilite (con TLS y usuario). Abre una conexión por mensaje: el volumen es bajo.
/// </summary>
public sealed class EnviadorSmtp(IOptions<OpcionesCorreo> opciones) : IEnviadorCorreo
{
    public bool Habilitado => true;

    public async Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        var o = opciones.Value;
        using var correo = new MimeMessage();
        correo.From.Add(new MailboxAddress(o.NombreRemitente, o.Remitente ?? throw new CorreoNoEnviadoException("Falta Plataforma__Correo__Remitente.")));
        correo.To.Add(new MailboxAddress(mensaje.NombrePara ?? string.Empty, mensaje.Para));
        correo.Subject = mensaje.Asunto;
        correo.Body = new BodyBuilder { HtmlBody = mensaje.Html, TextBody = mensaje.Texto }.ToMessageBody();

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(o.Tiempo);
        using var cliente = new SmtpClient { Timeout = (int)o.Tiempo.TotalMilliseconds };
        try
        {
            await cliente.ConnectAsync(o.Smtp.Host ?? throw new CorreoNoEnviadoException("Falta Plataforma__Correo__Smtp__Host."), o.Smtp.Puerto, Seguridad(o.Smtp.Seguridad), limite.Token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(o.Smtp.Usuario))
            {
                await cliente.AuthenticateAsync(o.Smtp.Usuario, o.Smtp.Clave ?? string.Empty, limite.Token).ConfigureAwait(false);
            }
            await cliente.SendAsync(correo, limite.Token).ConfigureAwait(false);
            await cliente.DisconnectAsync(true, limite.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is MailKit.ProtocolException or MailKit.Net.Smtp.SmtpCommandException or MailKit.Security.AuthenticationException
            or System.Net.Sockets.SocketException or IOException or OperationCanceledException or SslHandshakeException)
        {
            throw new CorreoNoEnviadoException($"El servidor SMTP no aceptó el correo ({ex.GetType().Name}).", ex);
        }
    }

    private static SecureSocketOptions Seguridad(string valor) => valor.Trim().ToLowerInvariant() switch
    {
        "ssl" => SecureSocketOptions.SslOnConnect,
        "ninguna" => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };
}
