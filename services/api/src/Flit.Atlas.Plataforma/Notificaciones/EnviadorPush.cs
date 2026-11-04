using System.Net;
using System.Text.Json;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Plataforma.Notificaciones;

/// <summary>Lo que recibe el service worker de la PWA (<c>apps/web/src/pwa/sw.ts</c>).</summary>
public sealed record CargaPush(Guid Id, string Tipo, string Titulo, string Cuerpo, string? Enlace);

/// <summary>
/// Envía Web Push a los dispositivos de una persona con Lib.Net.Http.WebPush (VAPID y cifrado aes128gcm de la
/// librería; nada de criptografía propia). Depura las suscripciones que el servicio de push ya no acepta.
/// </summary>
public sealed partial class EnviadorPush(HttpClient http, LlavesVapid llaves, TimeProvider reloj, ILogger<EnviadorPush> registro)
{
    /// <summary>Fallos seguidos (5xx, red, tiempo) antes de dar la suscripción por perdida.</summary>
    public const int TopeFallos = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Respuestas que significan «esta suscripción no sirve más» (vencida, revocada o de otra llave VAPID).</summary>
    private static readonly HttpStatusCode[] Definitivos =
        [HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Gone];

    /// <summary>Envía a todos los dispositivos de la persona y devuelve a cuántos llegó.</summary>
    public async Task<int> EnviarAsync(PlataformaDbContext contexto, Guid personaId, CargaPush carga, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (!llaves.Habilitado)
        {
            return 0;
        }
        var suscripciones = await contexto.SuscripcionesPush.Where(s => s.PersonaId == personaId).ToListAsync(ct);
        if (suscripciones.Count == 0)
        {
            return 0;
        }

        using var vapid = new VapidAuthentication(llaves.ClavePublica, llaves.ClavePrivada) { Subject = llaves.Asunto };
        var cliente = new PushServiceClient(http) { DefaultAuthentication = vapid, AutoRetryAfter = false };
        var contenido = JsonSerializer.Serialize(carga, Json);

        var resultados = await Task.WhenAll(suscripciones.Select(s => EntregarAsync(cliente, s, contenido, ct)));

        var ahora = reloj.GetUtcNow();
        var entregadas = 0;
        foreach (var (suscripcion, resultado) in suscripciones.Zip(resultados))
        {
            switch (resultado)
            {
                case Resultado.Entregada:
                    entregadas++;
                    suscripcion.UltimoUsoEn = ahora;
                    suscripcion.FallosConsecutivos = 0;
                    break;
                case Resultado.Vencida:
                    contexto.SuscripcionesPush.Remove(suscripcion);
                    break;
                default:
                    suscripcion.FallosConsecutivos++;
                    suscripcion.UltimoFalloEn = ahora;
                    if (suscripcion.FallosConsecutivos >= TopeFallos)
                    {
                        contexto.SuscripcionesPush.Remove(suscripcion);
                    }
                    break;
            }
        }
        await contexto.SaveChangesAsync(ct);
        return entregadas;
    }

    private enum Resultado
    {
        Entregada,
        Vencida,
        Fallida,
    }

    private async Task<Resultado> EntregarAsync(PushServiceClient cliente, SuscripcionPush suscripcion, string contenido, CancellationToken ct)
    {
        var destino = new PushSubscription { Endpoint = suscripcion.Endpoint };
        destino.SetKey(PushEncryptionKeyName.P256DH, suscripcion.P256dh);
        destino.SetKey(PushEncryptionKeyName.Auth, suscripcion.Auth);
        var mensaje = new PushMessage(contenido) { TimeToLive = 24 * 3600, Urgency = PushMessageUrgency.Normal };
        try
        {
            await cliente.RequestPushMessageDeliveryAsync(destino, mensaje, ct);
            return Resultado.Entregada;
        }
        catch (PushServiceClientException error) when (Definitivos.Contains(error.StatusCode))
        {
            // Sin el endpoint en el log: identifica al dispositivo (@pii).
            LogVencida(registro, suscripcion.Id, (int)error.StatusCode);
            return Resultado.Vencida;
        }
        catch (Exception error) when ((error is PushServiceClientException or HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            LogFallida(registro, suscripcion.Id, error.GetType().Name);
            return Resultado.Fallida;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Suscripción push {Suscripcion} vencida ({Estado}): se borra.")]
    private static partial void LogVencida(ILogger registro, Guid suscripcion, int estado);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo entregar el push a la suscripción {Suscripcion} ({Error}).")]
    private static partial void LogFallida(ILogger registro, Guid suscripcion, string error);
}
