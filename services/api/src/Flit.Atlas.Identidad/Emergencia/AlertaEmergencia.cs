using Flit.Atlas.Identidad.Externos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Correo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Emergencia;

/// <summary>
/// Alerta por correo de cada evento de la cuenta de emergencia (ATLAS-ADR-0053), al buzón configurado en
/// <c>Plataforma:Correo:Remitente</c>. Solo lleva el tipo, el ambiente, la fecha y el id del evento: ni la IP, ni la
/// persona, ni enlaces. Sale de la cola de la identidad para no demorar el ingreso; si no se puede enviar, queda el evento
/// <c>identidad.correo_no_enviado</c> con el motivo <see cref="Motivo"/>.
/// </summary>
public sealed partial class AlertaEmergencia(
    ColaCorreosIdentidad cola, IOptions<OpcionesCorreo> opciones, string ambiente, ILogger<AlertaEmergencia> log)
{
    public const string Motivo = "alerta_emergencia";

    /// <summary>Encola la alerta; nunca lanza, porque el evento ya quedó registrado. Sin buzón configurado no hace nada.</summary>
    public void Avisar(string tipo, Guid eventoId, DateTimeOffset momento)
    {
        var buzon = opciones.Value.Remitente;
        if (string.IsNullOrWhiteSpace(buzon))
        {
            return;
        }
        if (!cola.Encolar(new CorreoPendiente(Mensaje(buzon, ambiente, tipo, eventoId, momento), new DatosEventoSeguridad(), Motivo)))
        {
            ColaLlena(log, tipo, eventoId);
        }
    }

    /// <summary>El correo de la alerta. Sin botón ni enlaces, como los demás avisos de seguridad (ATLAS-ADR-0049).</summary>
    public static MensajeCorreo Mensaje(string buzon, string ambiente, string tipo, Guid eventoId, DateTimeOffset momento)
    {
        var asunto = $"[Atlas {ambiente}] Alerta de seguridad: uso de la cuenta de emergencia";
        var (html, texto) = PlantillaCorreo.Armar(
            "Alerta de la cuenta de emergencia",
            [
                $"En el ambiente {ambiente} de Flit Atlas {Descripcion(tipo)}.",
                $"Fecha: {CorreosExternos.Fecha(momento)} (hora de Colombia).",
                $"Evento: {TiposEventoSeguridad.PrefijoAuditoria}{tipo} ({eventoId}).",
            ],
            null,
            null,
            [
                "Si era un uso previsto, revísalo en la auditoría y cierra la sesión de la cuenta al terminar.",
                "Si nadie lo esperaba, trátalo como un incidente de seguridad: sigue la sección «Cuenta de emergencia» del runbook de Atlas.",
            ]);
        return new MensajeCorreo(buzon, null, asunto, html, texto);
    }

    private static string Descripcion(string tipo) => tipo switch
    {
        TiposEventoSeguridad.EmergenciaCuentaCreada => "se creó la cuenta de emergencia",
        TiposEventoSeguridad.EmergenciaEnlaceEmitido => "el comando de consola emitió un enlace de activación de la cuenta de emergencia",
        TiposEventoSeguridad.EmergenciaActivada => "se activó la cuenta de emergencia",
        TiposEventoSeguridad.EmergenciaIngreso => "alguien ingresó con la cuenta de emergencia",
        _ => "se registró un evento de la cuenta de emergencia",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se encoló la alerta de {Tipo} ({EventoId}): la cola de correos está llena")]
    private static partial void ColaLlena(ILogger log, string tipo, Guid eventoId);
}
