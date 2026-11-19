using System.Globalization;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Correos de las cuentas externas: invitación y recuperación (ATLAS-ADR-0025). Los enlaces llevan el token en el
/// <b>fragmento</b> (<c>#…</c>): el navegador no lo envía a ningún servidor, así que no queda en los logs de nginx, del
/// Ingress ni de Vite, ni en el <c>Referer</c>. La PWA lo lee y lo manda a la API en el cuerpo de un POST. Ni el token,
/// ni el enlace, ni el destinatario van a los logs.
/// </summary>
public sealed partial class CorreosExternos(IEnviadorCorreo enviador, IConfiguration configuracion, ILogger<CorreosExternos> log)
{
    public const string RutaInvitacion = "/invitacion";
    public const string RutaRecuperacion = "/recuperar";

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    public async Task<bool> EnviarInvitacionAsync(Invitacion invitacion, string token, string organizacion, string rol, HttpContext? http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invitacion);
        var enlace = $"{UrlPublica(http)}{RutaInvitacion}#{token}";
        var (html, texto) = PlantillaCorreo.Armar(
            "Te invitaron a Flit Atlas",
            [
                $"Hola{(string.IsNullOrWhiteSpace(invitacion.Nombre) ? string.Empty : ", " + invitacion.Nombre)}.",
                $"FLIT S.A.S. te invitó a Flit Atlas, su plataforma de conocimiento, para {organizacion}, con el rol «{rol}».",
                "Al aceptar creas tu cuenta personal: te recomendamos una passkey (la huella, el rostro o el PIN de tu dispositivo); también puedes usar una contraseña con una aplicación de autenticación.",
            ],
            "Aceptar la invitación",
            enlace,
            [
                $"El enlace sirve una sola vez y vence el {Fecha(invitacion.TokenVenceEn)} (hora de Colombia).",
                invitacion.AccesoVenceEn is { } vence ? $"Tu acceso estará vigente hasta el {Fecha(vence)}." : string.Empty,
                "Si no esperabas esta invitación, ignora este correo.",
            ]);
        return await EnviarAsync(new MensajeCorreo(invitacion.Correo, invitacion.Nombre, "Te invitaron a Flit Atlas", html, texto), ct);
    }

    public async Task<bool> EnviarRecuperacionAsync(string correo, string nombre, string enlaceToken, DateTimeOffset vence, bool forzada, HttpContext? http, CancellationToken ct)
    {
        var enlace = $"{UrlPublica(http)}{RutaRecuperacion}#{enlaceToken}";
        var (html, texto) = PlantillaCorreo.Armar(
            "Recupera tu cuenta de Flit Atlas",
            [
                $"Hola, {nombre}.",
                forzada
                    ? "Un administrador de FLIT restableció los factores de tu cuenta de Flit Atlas. Para volver a entrar, registra una passkey o una contraseña con tu aplicación de autenticación."
                    : "Recibimos una solicitud para recuperar tu cuenta de Flit Atlas. Registra un factor nuevo (passkey, o contraseña con tu aplicación de autenticación) para volver a entrar.",
                "Los factores anteriores dejarán de servir y se cerrarán tus sesiones abiertas.",
            ],
            "Recuperar mi cuenta",
            enlace,
            [
                $"El enlace sirve una sola vez y vence el {Fecha(vence)} (hora de Colombia).",
                forzada ? string.Empty : "Si no fuiste tú, ignora este correo: tu cuenta sigue igual.",
            ]);
        return await EnviarAsync(new MensajeCorreo(correo, nombre, "Recupera tu cuenta de Flit Atlas", html, texto), ct);
    }

    private async Task<bool> EnviarAsync(MensajeCorreo mensaje, CancellationToken ct)
    {
        if (!enviador.Habilitado)
        {
            await enviador.EnviarAsync(mensaje, ct);
            return false;
        }
        try
        {
            await enviador.EnviarAsync(mensaje, ct);
            return true;
        }
        catch (CorreoNoEnviadoException ex)
        {
            // El mensaje de la excepción no lleva destinatario ni enlace.
            NoSeEnvio(log, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// URL pública del ambiente (<c>Atlas:UrlPublica</c>, obligatoria en qa y pdn). En local y dev, si falta, la del propio
    /// pedido (Vite hace de proxy y conserva el host).
    /// </summary>
    private string UrlPublica(HttpContext? http)
    {
        var configurada = configuracion[OpcionesExternos.ClaveUrlPublica];
        if (!string.IsNullOrWhiteSpace(configurada))
        {
            return configurada.TrimEnd('/');
        }
        return http is null ? "https://localhost:4032" : $"{http.Request.Scheme}://{http.Request.Host}";
    }

    private static string Fecha(DateTimeOffset momento) =>
        TimeZoneInfo.ConvertTime(momento, Bogota).ToString("dd/MM/yyyy 'a las' HH:mm", CultureInfo.InvariantCulture);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se envió un correo de cuentas externas: {Motivo}")]
    private static partial void NoSeEnvio(ILogger log, string motivo);
}
