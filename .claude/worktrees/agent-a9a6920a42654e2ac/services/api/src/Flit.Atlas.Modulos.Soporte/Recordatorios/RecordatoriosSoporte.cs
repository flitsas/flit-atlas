using System.Globalization;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Microsoft365;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Recordatorios;

/// <summary>Conteos de una corrida de recordatorios.</summary>
public sealed record ResultadoRecordatorios(Guid CorridaId, DateOnly Fecha, int Enviados, int Fallidos, int Omitidos);

/// <summary>
/// Recordatorios de los turnos de soporte (HU 2.11, prompt maestro §8.3): N días antes del sábado (la anticipación de la
/// configuración) a la persona de turno, y el mismo sábado a cada persona de turno y al canal de Teams del soporte. A
/// cada persona le sale por la bandeja de la app, push (Web Push de la HU 1.6) y correo (<see cref="IEnviadorCorreo"/>),
/// según sus preferencias para el tipo <c>soporte.recordatorio_turno</c>; no hay aviso personal por Teams (el flujo de
/// Workflows publica en un canal, ATLAS-ADR-0033). El aviso al canal se encola en <see cref="ColaAvisosTeams"/>, que lo
/// publica con reintentos (HU 2.10).
/// <para>
/// Un solo nodo a la vez: candado consultivo de PostgreSQL. Idempotente: cada envío por canal queda en
/// <c>soporte_recordatorio</c> con una clave única antes de salir, así que repetir la corrida (otra réplica, «Enviar
/// ahora», un reinicio) no manda dos veces el mismo. Tolerante a fallos por canal: si el correo no sale, push y Teams
/// siguen; lo fallido se reintenta en la corrida siguiente (hasta <see cref="TopeIntentos"/> veces).
/// </para>
/// </summary>
public sealed partial class RecordatoriosSoporte(
    SoporteDbContext soporte,
    PlataformaDbContext plataforma,
    ServicioNotificaciones notificaciones,
    LlavesVapid llaves,
    IEnviadorCorreo correo,
    ColaAvisosTeams avisosTeams,
    IConfiguration configuracion,
    TimeProvider reloj,
    ILogger<RecordatoriosSoporte> log)
{
    /// <summary>Intentos por canal antes de dejar de reintentar un recordatorio fallido.</summary>
    public const int TopeIntentos = 3;

    /// <summary>Llave del candado consultivo de sesión (<c>pg_try_advisory_lock</c>) de los recordatorios.</summary>
    internal const long Candado = 0x41544C4153_0211;

    public const string Enlace = "/soporte";

    private static readonly HashSet<CanalNotificacion> Canales = [CanalNotificacion.App, CanalNotificacion.Push, CanalNotificacion.Correo];

    /// <summary>Tipo de notificación de los recordatorios: app, push y correo, encendidos por defecto (sin Teams personal).</summary>
    public static readonly TipoNotificacion Tipo = new(
        "soporte.recordatorio_turno", "Recordatorios de turno de soporte",
        "Aviso unos días antes de tu turno de soporte del sábado y el mismo sábado.",
        Canales, Canales);

    /// <summary>
    /// Corre los recordatorios de hoy (Bogotá). Devuelve <c>null</c> si otra réplica tiene el candado o si es la corrida
    /// programada y ya se hizo hoy. Las fallas de la base se lanzan (el trabajo lo intenta en la vuelta siguiente).
    /// </summary>
    public async Task<ResultadoRecordatorios?> CorrerAsync(DisparoRecordatorios disparo, Guid? actor, CancellationToken ct)
    {
        // El candado es de la sesión de PostgreSQL: la conexión queda abierta hasta soltarlo.
        await soporte.Database.OpenConnectionAsync(ct);
        try
        {
            var tomado = await soporte.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({Candado}) AS \"Value\"").SingleAsync(ct);
            if (!tomado)
            {
                return null;
            }
            try
            {
                return await CorrerConCandadoAsync(disparo, actor, ct);
            }
            finally
            {
                await soporte.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({Candado})", CancellationToken.None);
            }
        }
        finally
        {
            await soporte.Database.CloseConnectionAsync();
        }
    }

    private sealed class Conteo
    {
        public int Enviados;
        public int Fallidos;
        public int Omitidos;
    }

    private async Task<ResultadoRecordatorios?> CorrerConCandadoAsync(DisparoRecordatorios disparo, Guid? actor, CancellationToken ct)
    {
        var hoy = CalendarioBogota.Hoy(reloj);
        CorridaRecordatorios? corrida = null;
        if (disparo == DisparoRecordatorios.Programada)
        {
            corrida = await soporte.CorridasRecordatorios.FirstOrDefaultAsync(c => c.Fecha == hoy && c.Disparo == DisparoRecordatorios.Programada, ct);
            if (corrida?.TerminadaEn is not null)
            {
                return null;
            }
        }
        if (corrida is null)
        {
            // Una programada que se cortó a mitad (la base se cayó) se retoma: lo ya enviado está marcado.
            corrida = new CorridaRecordatorios { Fecha = hoy, Disparo = disparo, PedidaPorPersonaId = actor };
            soporte.CorridasRecordatorios.Add(corrida);
            await soporte.SaveChangesAsync(ct);
        }

        var config = await ConfiguracionAsync(soporte, ct);
        var hasta = config.DiasAnticipacionRecordatorio is int dias ? hoy.AddDays(dias) : hoy;
        var equipos = await soporte.Equipos.AsNoTracking().Where(e => e.Activo).ToDictionaryAsync(e => e.Id, ct);
        var turnos = (await soporte.Turnos.AsNoTracking()
                .Where(t => t.Fecha >= hoy && t.Fecha <= hasta
                    && (t.Estado == EstadoTurno.Programado || t.Estado == EstadoTurno.Confirmado || (t.Fecha == hoy && t.Estado == EstadoTurno.Aplicado)))
                .OrderBy(t => t.Fecha).ThenBy(t => t.Id)
                .ToListAsync(ct))
            .Where(t => equipos.ContainsKey(t.EquipoId))
            .ToList();
        var ids = turnos.Select(t => t.PersonaId).Distinct().ToList();
        var personas = await plataforma.Personas.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Estado == EstadoPersona.Activa && p.Tipo == TipoPersona.Empleado)
            .ToDictionaryAsync(p => p.Id, ct);
        var previos = await soporte.Recordatorios.Where(r => r.Fecha >= hoy && r.Fecha <= hasta).ToDictionaryAsync(r => r.Clave, ct);

        var conteo = new Conteo();
        foreach (var turno in turnos)
        {
            if (!personas.TryGetValue(turno.PersonaId, out var persona))
            {
                continue;
            }
            var equipo = equipos[turno.EquipoId];
            var momento = turno.Fecha == hoy ? MomentoRecordatorio.MismoDia : MomentoRecordatorio.Anticipado;
            var (titulo, cuerpo) = Textos(momento, turno, equipo, config, hoy);
            Guid? bandeja = null;
            foreach (var canal in new[] { CanalRecordatorio.App, CanalRecordatorio.Push, CanalRecordatorio.Correo })
            {
                var clave = $"{ReglasTurno.Texto(momento)}:{turno.Id:N}:{ReglasTurno.Texto(canal)}";
                if (!Pendiente(previos, clave))
                {
                    continue;
                }
                if (!await PuedeSalirAsync(persona.Id, canal, ct))
                {
                    conteo.Omitidos++;
                    continue;
                }
                var registro = await ReclamarAsync(previos, clave, momento, canal, turno.EquipoId, turno.Fecha, turno.Id, persona.Id, ct);
                await IntentarAsync(registro, conteo, async () =>
                {
                    switch (canal)
                    {
                        case CanalRecordatorio.App:
                            bandeja = (await notificaciones.RegistrarEnBandejaAsync(persona.Id, Tipo, titulo, cuerpo, Enlace, ct)).Id;
                            return true;
                        case CanalRecordatorio.Push:
                            return await notificaciones.EnviarPushAsync(persona.Id, new CargaPush(bandeja ?? registro.Id, Tipo.Codigo, titulo, cuerpo, Enlace), ct) > 0;
                        default:
                            await correo.EnviarAsync(Correo(persona, titulo, cuerpo), ct);
                            return true;
                    }
                }, ct);
            }
        }

        // El mismo sábado, un aviso por equipo en el canal de Teams del soporte: quién atiende y en qué horario. Va a la cola
        // de la HU 2.10 en el mismo guardado que lo marca enviado; el trabajo de Microsoft 365 lo publica y lo reintenta.
        if (avisosTeams.Activa)
        {
            foreach (var grupo in turnos.Where(t => t.Fecha == hoy && personas.ContainsKey(t.PersonaId)).GroupBy(t => t.EquipoId))
            {
                var equipo = equipos[grupo.Key];
                var clave = $"{ReglasTurno.Texto(MomentoRecordatorio.MismoDia)}:{equipo.Id:N}:{hoy:yyyyMMdd}:{ReglasTurno.Texto(CanalRecordatorio.CanalTeams)}";
                if (!Pendiente(previos, clave))
                {
                    continue;
                }
                var registro = await ReclamarAsync(previos, clave, MomentoRecordatorio.MismoDia, CanalRecordatorio.CanalTeams, equipo.Id, hoy, null, null, ct);
                var nombres = Unir([.. grupo.Select(t => personas[t.PersonaId].Nombre)]);
                var (inicio, fin) = Horario(grupo.First(), equipo, config);
                await IntentarAsync(registro, conteo, () => Task.FromResult(avisosTeams.Encolar(soporte, TipoAvisoTeams.Recordatorio, new MensajeTeams(
                    $"Soporte de hoy: {equipo.Nombre}",
                    $"Hoy sábado {Dia(hoy)} atiende el soporte de {equipo.Nombre}: {nombres}, de {inicio} a {fin}.",
                    [new HechoTeams("Equipo", equipo.Nombre), new HechoTeams("Atiende", nombres), new HechoTeams("Horario", $"{inicio} a {fin}")],
                    avisosTeams.Enlace(Enlace), "Ver el calendario"))), ct);
            }
        }

        corrida.Enviados += conteo.Enviados;
        corrida.Fallidos += conteo.Fallidos;
        corrida.Omitidos += conteo.Omitidos;
        corrida.TerminadaEn = reloj.GetUtcNow();
        await soporte.SaveChangesAsync(ct);
        var nombreDisparo = ReglasTurno.Texto(disparo);
        Corrida(log, nombreDisparo, conteo.Enviados, conteo.Fallidos, conteo.Omitidos);
        return new ResultadoRecordatorios(corrida.Id, hoy, corrida.Enviados, corrida.Fallidos, corrida.Omitidos);
    }

    /// <summary>
    /// Falta mandarlo: no hay registro, o falló y le quedan intentos. Uno <c>en_curso</c> (una corrida que se cortó
    /// mientras enviaba) no se repite: pudo haber salido.
    /// </summary>
    private static bool Pendiente(Dictionary<string, RecordatorioEnviado> previos, string clave) =>
        !previos.TryGetValue(clave, out var previo) || (previo.Estado == EstadoRecordatorio.Fallido && previo.Intentos < TopeIntentos);

    /// <summary>La persona quiere ese canal para este tipo y el ambiente lo tiene (la bandeja siempre).</summary>
    private async Task<bool> PuedeSalirAsync(Guid persona, CanalRecordatorio canal, CancellationToken ct)
    {
        var deNotificacion = canal switch
        {
            CanalRecordatorio.App => CanalNotificacion.App,
            CanalRecordatorio.Push => CanalNotificacion.Push,
            _ => CanalNotificacion.Correo,
        };
        if (deNotificacion == CanalNotificacion.App)
        {
            return true;
        }
        if (!CatalogoNotificaciones.Disponible(Tipo, deNotificacion, correo)
            || !await notificaciones.CanalActivoAsync(persona, Tipo, deNotificacion, ct))
        {
            return false;
        }
        // Sin llaves VAPID o sin dispositivos suscritos no hay a quién mandarle push: no cuenta como falla.
        return deNotificacion != CanalNotificacion.Push
            || (llaves.Habilitado && await plataforma.SuscripcionesPush.AnyAsync(s => s.PersonaId == persona, ct));
    }

    /// <summary>Marca el recordatorio <c>en_curso</c> antes de enviarlo (la marca es lo que evita el doble envío).</summary>
    private async Task<RecordatorioEnviado> ReclamarAsync(
        Dictionary<string, RecordatorioEnviado> previos, string clave, MomentoRecordatorio momento, CanalRecordatorio canal,
        Guid equipo, DateOnly fecha, Guid? turno, Guid? persona, CancellationToken ct)
    {
        if (!previos.TryGetValue(clave, out var registro))
        {
            registro = new RecordatorioEnviado
            {
                Clave = clave, Momento = momento, Canal = canal, EquipoId = equipo, Fecha = fecha, TurnoId = turno, PersonaId = persona,
            };
            soporte.Recordatorios.Add(registro);
            previos[clave] = registro;
        }
        registro.Estado = EstadoRecordatorio.EnCurso;
        registro.Intentos++;
        await soporte.SaveChangesAsync(ct);
        return registro;
    }

    /// <summary>Envía por un canal; una falla queda en el registro (solo su tipo) y no detiene los demás canales.</summary>
    private async Task IntentarAsync(RecordatorioEnviado registro, Conteo conteo, Func<Task<bool>> enviar, CancellationToken ct)
    {
        try
        {
            if (await enviar())
            {
                registro.Estado = EstadoRecordatorio.Enviado;
                registro.EnviadoEn = reloj.GetUtcNow();
                registro.Error = null;
                conteo.Enviados++;
            }
            else
            {
                // Push que ningún dispositivo aceptó (el enviador ya depuró las suscripciones vencidas).
                registro.Estado = EstadoRecordatorio.Fallido;
                registro.Error = "sin_entrega";
                conteo.Fallidos++;
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            registro.Estado = EstadoRecordatorio.Fallido;
            registro.Error = error.GetType().Name[..Math.Min(error.GetType().Name.Length, SoporteDbContext.LargoErrorRecordatorio)];
            conteo.Fallidos++;
            // Sin destinatario ni contenido: solo el canal y el tipo de la falla.
            var canal = ReglasTurno.Texto(registro.Canal);
            CanalFallido(log, canal, registro.Error);
        }
        await soporte.SaveChangesAsync(CancellationToken.None);
    }

    private static (string Inicio, string Fin) Horario(Turno turno, EquipoSoporte equipo, ConfiguracionSoporte config)
    {
        var (inicio, fin) = turno.HoraInicio is { } i && turno.HoraFin is { } f ? (i, f) : HorarioDe(equipo, config);
        return (Hora(inicio)!, Hora(fin)!);
    }

    /// <summary>Título y cuerpo del recordatorio: equipo, fecha y horario; sin datos de otras personas.</summary>
    internal static (string Titulo, string Cuerpo) Textos(MomentoRecordatorio momento, Turno turno, EquipoSoporte equipo, ConfiguracionSoporte config, DateOnly hoy)
    {
        var (inicio, fin) = Horario(turno, equipo, config);
        if (momento == MomentoRecordatorio.MismoDia)
        {
            return ($"Hoy tienes soporte de {equipo.Nombre}",
                $"Hoy sábado {Dia(turno.Fecha)} te toca el soporte de {equipo.Nombre}, de {inicio} a {fin} (hora de Bogotá).");
        }
        var faltan = turno.Fecha.DayNumber - hoy.DayNumber;
        var cuando = faltan == 1 ? "mañana" : $"en {faltan} días";
        return ($"Tu turno de soporte es el sábado {Dia(turno.Fecha)}",
            $"Te toca el soporte de {equipo.Nombre} {cuando}, el sábado {Dia(turno.Fecha)}, de {inicio} a {fin} (hora de Bogotá). " +
            "Si no puedes, solicita un cambio de turno en Atlas.");
    }

    /// <summary>Correo con la plantilla de la marca: solo el primer nombre, el equipo, la fecha y el horario.</summary>
    private MensajeCorreo Correo(Persona persona, string titulo, string cuerpo)
    {
        var url = configuracion["Atlas:UrlPublica"];
        var baseUrl = string.IsNullOrWhiteSpace(url) ? "https://localhost:4032" : url.TrimEnd('/');
        var nombre = persona.Nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? persona.Nombre;
        var (html, texto) = PlantillaCorreo.Armar(titulo, [$"Hola, {nombre}.", cuerpo], "Ver el calendario de soporte", $"{baseUrl}{Enlace}",
            ["Recibes este correo porque tienes activos los recordatorios de turno de soporte por correo. Puedes apagarlos en Atlas, en Ajustes, sección Notificaciones."]);
        return new MensajeCorreo(persona.Correo, persona.Nombre, titulo, html, texto);
    }

    private static string Dia(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>«Ana», «Ana y Luis», «Ana, Luis y Sara».</summary>
    private static string Unir(IReadOnlyList<string> nombres) =>
        nombres.Count <= 1 ? string.Concat(nombres) : $"{string.Join(", ", nombres.Take(nombres.Count - 1))} y {nombres[^1]}";

    [LoggerMessage(Level = LogLevel.Information, Message = "Soporte: recordatorios ({Disparo}): {Enviados} enviados, {Fallidos} fallidos, {Omitidos} omitidos")]
    private static partial void Corrida(ILogger log, string disparo, int enviados, int fallidos, int omitidos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Soporte: un recordatorio por {Canal} no salió ({Error}); se reintenta en la corrida siguiente")]
    private static partial void CanalFallido(ILogger log, string canal, string error);
}
