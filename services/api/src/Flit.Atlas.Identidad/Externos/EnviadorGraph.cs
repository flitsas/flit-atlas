using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Plataforma.Correo;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Envío por Microsoft Graph <c>POST /users/{buzón}/sendMail</c> desde el buzón compartido de Atlas, con un token de
/// aplicación del registro de Entra del ambiente (ATLAS-ADR-0025). Requiere el permiso de aplicación <c>Mail.Send</c>
/// con consentimiento de administrador, <b>limitado a ese buzón</b> con una política de acceso de aplicaciones de
/// Exchange Online (paso del líder: sin ella, la app podría enviar como cualquier buzón del tenant). No guarda copia en
/// «Enviados» (<c>saveToSentItems = false</c>): el correo lleva un enlace de un solo uso.
/// </summary>
public sealed class EnviadorGraph(HttpClient http, IAuthorizationHeaderProvider autorizacion, IOptions<OpcionesCorreo> opciones) : IEnviadorCorreo
{
    public bool Habilitado => true;

    public async Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        var o = opciones.Value;
        var buzon = o.Remitente ?? throw new CorreoNoEnviadoException("Falta Plataforma__Correo__Remitente (buzón compartido).");
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(o.Tiempo);
        try
        {
            var cabecera = await autorizacion.CabeceraAplicacionAsync(PerfilGraph.ScopeGraph, limite.Token).ConfigureAwait(false);
            using var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri($"v1.0/users/{Uri.EscapeDataString(buzon)}/sendMail", UriKind.Relative))
            {
                Content = JsonContent.Create(new
                {
                    message = new
                    {
                        subject = mensaje.Asunto,
                        body = new { contentType = "HTML", content = mensaje.Html },
                        toRecipients = new[] { new { emailAddress = new { address = mensaje.Para, name = mensaje.NombrePara } } },
                    },
                    saveToSentItems = false,
                }),
            };
            peticion.Headers.Authorization = AuthenticationHeaderValue.Parse(cabecera);
            using var respuesta = await http.SendAsync(peticion, limite.Token).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
            {
                // Solo el código: el cuerpo de error de Graph puede repetir el destinatario.
                throw new CorreoNoEnviadoException($"Graph respondió {(int)respuesta.StatusCode} a sendMail.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Microsoft.Identity.Client.MsalException or InvalidOperationException)
        {
            throw new CorreoNoEnviadoException($"No se pudo enviar por Graph ({ex.GetType().Name}).", ex);
        }
    }
}
