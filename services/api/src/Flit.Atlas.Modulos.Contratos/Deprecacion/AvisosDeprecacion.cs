using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Guias;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Modulos.Contratos.Portal;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Notificaciones;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Contratos.EndpointsContratos;

namespace Flit.Atlas.Modulos.Contratos.Deprecacion;

/// <summary>Conteos de una corrida de avisos de deprecación.</summary>
/// <param name="Personas">A cuántas personas les tocaba algún aviso en esta corrida (enviado o no).</param>
public sealed record ResultadoAvisos(int Versiones, int Personas, int Enviados, int Fallidos, int Omitidos);

/// <summary>
/// Avisos de deprecación (HU 4.8, plan 00 §8.2 regla 7, prompt maestro §8.9, ATLAS-ADR-0046). Por cada versión deprecada con
/// fecha de retiro hoy o después:
/// <list type="bullet">
/// <item><b>Externos</b>: las personas activas de las organizaciones con una compartición vigente cuyo paquete (revisión
/// fijada o vigente) incluye la versión, y solo si <see cref="AccesoPortal"/> les muestra un paquete con ella (las mismas
/// reglas del portal: alcance de su invitación, rol, vencimientos, contrato aún compartible). El texto nombra solo sus
/// paquetes; nunca otras organizaciones.</item>
/// <item><b>Internos</b>: los responsables vigentes (principal o secundario) del aplicativo del contrato o, si no tiene, del
/// ancestro más cercano que los tenga; el texto dice a cuántas organizaciones se avisó, sin nombrarlas.</item>
/// </list>
/// Sale por la bandeja, push y correo (un correo por persona, sin copias), según las preferencias del tipo
/// <c>contratos.aviso_deprecacion</c>. Al deprecar sale el aviso; después, uno por recordatorio (30 y 7 días antes, por
/// defecto) cuya ventana empiece después del aviso. Idempotente: cada envío por canal queda en
/// <c>contratos_aviso_deprecacion</c> con una clave única antes de salir. Un nodo a la vez (candado consultivo). Cada aviso
/// por persona queda en la auditoría con su organización.
/// </summary>
internal sealed partial class AvisosDeprecacion(
    ContratosDbContext contexto,
    PlataformaDbContext plataforma,
    AccesoPortal acceso,
    CalculadoraPermisos calculadora,
    ILectorAplicativos lector,
    ILectorResponsables responsables,
    ServicioNotificaciones notificaciones,
    LlavesVapid llaves,
    IEnviadorCorreo correo,
    IConfiguration configuracion,
    IOptions<OpcionesAvisosDeprecacion> opciones,
    TimeProvider reloj,
    ILogger<AvisosDeprecacion> log)
{
    /// <summary>Intentos por canal antes de dejar de reintentar un aviso fallido.</summary>
    public const int TopeIntentos = 3;

    /// <summary>Llave del candado consultivo de sesión (<c>pg_try_advisory_lock</c>) de los avisos.</summary>
    internal const long Candado = 0x41544C4153_0408;

    public const string RecursoAuditoria = "contratos.version";
    public const string AccionAviso = "contratos.aviso_deprecacion_enviado";

    private static readonly HashSet<CanalNotificacion> Canales = [CanalNotificacion.App, CanalNotificacion.Push, CanalNotificacion.Correo];

    /// <summary>Tipo de notificación de los avisos: app, push y correo, encendidos por defecto.</summary>
    public static readonly TipoNotificacion Tipo = new(
        "contratos.aviso_deprecacion", "Avisos de deprecación de contratos",
        "Cuando se depreca una versión de un contrato que usas o del que eres responsable, y unos días antes de su retiro.",
        Canales, Canales);

    private sealed class Conteo
    {
        public int Enviados;
        public int Fallidos;
        public int Omitidos;
    }

    /// <summary>A quién le toca un aviso de una versión y qué le muestra.</summary>
    private sealed record Destinatario(Persona Persona, bool Externo, IReadOnlyList<(Guid Id, string Nombre)> Paquetes);

    /// <summary>
    /// Corre los avisos de hoy (Bogotá); con <paramref name="soloVersion"/>, solo los de esa versión (lo que dispara deprecar).
    /// Devuelve <c>null</c> si otra réplica tiene el candado. Las fallas de la base se lanzan (el trabajo reintenta).
    /// </summary>
    public async Task<ResultadoAvisos?> CorrerAsync(Guid? soloVersion, CancellationToken ct)
    {
        await contexto.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var tomado = await contexto.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({Candado}) AS \"Value\"").SingleAsync(ct).ConfigureAwait(false);
            if (!tomado)
            {
                return null;
            }
            try
            {
                return await CorrerConCandadoAsync(soloVersion, ct).ConfigureAwait(false);
            }
            finally
            {
                await contexto.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({Candado})", CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            await contexto.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task<ResultadoAvisos> CorrerConCandadoAsync(Guid? soloVersion, CancellationToken ct)
    {
        var hoy = ReglasDeprecacion.Hoy(reloj);
        var consulta = contexto.Versiones.AsNoTracking().Where(v => v.Estado == EstadoVersion.Deprecada && v.RetiroEn != null && v.RetiroEn >= hoy);
        if (soloVersion is { } id)
        {
            consulta = consulta.Where(v => v.Id == id);
        }
        var versiones = await SinContenido(consulta).ToListAsync(ct).ConfigureAwait(false);
        if (versiones.Count == 0)
        {
            return new ResultadoAvisos(0, 0, 0, 0, 0);
        }
        var idsContratos = versiones.Select(v => v.ContratoId).Distinct().ToList();
        var contratos = await contexto.Contratos.AsNoTracking().Where(c => idsContratos.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var reemplazos = await ReglasDeprecacion.ReemplazosAsync(contexto, [.. versiones.Select(v => (v.Id, v.ContratoId, v.Version))], ct).ConfigureAwait(false);
        var externos = await ExternosAsync(versiones, ct).ConfigureAwait(false);
        var internos = await InternosAsync(versiones, contratos, ct).ConfigureAwait(false);
        var idsVersiones = versiones.Select(v => v.Id).ToList();
        var previos = await contexto.AvisosDeprecacion.Where(a => idsVersiones.Contains(a.VersionId) && a.RetiroEn >= hoy)
            .ToDictionaryAsync(a => a.Clave, ct).ConfigureAwait(false);
        var dias = opciones.Value.Dias;

        var conteo = new Conteo();
        var personas = 0;
        foreach (var version in versiones)
        {
            var contrato = contratos[version.ContratoId];
            var retiro = version.RetiroEn!.Value;
            var deExternos = externos.GetValueOrDefault(version.Id) ?? [];
            var organizaciones = deExternos.Select(d => d.Persona.OrganizacionId).Distinct().Count();
            foreach (var destinatario in deExternos.Concat(internos.GetValueOrDefault(version.Id) ?? []).DistinctBy(d => d.Persona.Id))
            {
                var momentos = new List<(MomentoAviso Momento, int? Dias)> { (MomentoAviso.Deprecacion, null) };
                var avisoApp = previos.GetValueOrDefault(ReglasDeprecacion.Clave(version.Id, retiro, MomentoAviso.Deprecacion, null, destinatario.Persona.Id, CanalAviso.App));
                if (avisoApp is not null && ReglasDeprecacion.RecordatorioDeHoy(retiro, hoy, ReglasDeprecacion.Fecha(avisoApp.EnviadoEn ?? avisoApp.CreadoEn), dias) is { } d)
                {
                    momentos.Add((MomentoAviso.Recordatorio, d));
                }
                var tocaba = false;
                foreach (var (momento, diasAntes) in momentos)
                {
                    var (titulo, cuerpo) = destinatario.Externo
                        ? ReglasDeprecacion.TextoExterno(momento, version.Version, contrato.Nombre, retiro, hoy, reemplazos.GetValueOrDefault(version.Id),
                            [.. destinatario.Paquetes.Select(p => p.Nombre)])
                        : ReglasDeprecacion.TextoInterno(momento, version.Version, contrato.Nombre, retiro, hoy, reemplazos.GetValueOrDefault(version.Id), organizaciones);
                    var enlace = destinatario.Externo
                        ? $"/portal/paquetes/{destinatario.Paquetes[0].Id}/contratos/{contrato.Id}"
                        : $"/contratos/{contrato.Id}";
                    var enviados = await EnviarAsync(destinatario, version, retiro, momento, diasAntes, titulo, cuerpo, enlace, previos, conteo, ct).ConfigureAwait(false);
                    tocaba |= enviados.Intentados;
                    if (enviados.Canales.Count > 0)
                    {
                        await AuditarAsync(destinatario, version, contrato, retiro, momento, diasAntes, enviados.Canales, ct).ConfigureAwait(false);
                    }
                }
                personas += tocaba ? 1 : 0;
            }
        }
        Corrida(log, versiones.Count, personas, conteo.Enviados, conteo.Fallidos, conteo.Omitidos);
        return new ResultadoAvisos(versiones.Count, personas, conteo.Enviados, conteo.Fallidos, conteo.Omitidos);
    }

    /// <summary>
    /// Por versión, las personas externas que la ven en el portal: primero las organizaciones con una compartición vigente
    /// cuya revisión la incluye (barato) y después, por persona, la vista de <see cref="AccesoPortal"/> con sus permisos.
    /// </summary>
    private async Task<Dictionary<Guid, List<Destinatario>>> ExternosAsync(IReadOnlyList<VersionContratoSinContenido> versiones, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var comparticiones = await contexto.Comparticiones.AsNoTracking()
            .Where(c => c.RevocadoEn == null && c.VigenteDesde <= ahora && (c.VigenteHasta == null || c.VigenteHasta > ahora))
            .ToListAsync(ct).ConfigureAwait(false);
        var idsPaquetes = comparticiones.Select(c => c.PaqueteId).Distinct().ToList();
        var paquetes = await contexto.Paquetes.AsNoTracking()
            .Where(p => idsPaquetes.Contains(p.Id) && p.Estado == EstadoPaquete.Publicado && p.RevisionPublicada != null)
            .ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var pedidas = comparticiones.Where(c => paquetes.ContainsKey(c.PaqueteId))
            .Select(c => (c.OrganizacionId, c.PaqueteId, Numero: c.RevisionFijada ?? paquetes[c.PaqueteId].RevisionPublicada!.Value)).ToList();
        var numeros = pedidas.Select(p => p.Numero).Distinct().ToList();
        var definiciones = (await contexto.RevisionesPaquete.AsNoTracking()
                .Where(r => idsPaquetes.Contains(r.PaqueteId) && numeros.Contains(r.Numero))
                .Select(r => new { r.PaqueteId, r.Numero, r.Definicion })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(r => (r.PaqueteId, r.Numero), r => FotoPaquete.LeerDefinicion(r.Definicion).Select(e => e.VersionId).ToHashSet());
        var idsVersiones = versiones.Select(v => v.Id).ToHashSet();
        var organizacionesPorVersion = pedidas
            .SelectMany(p => (definiciones.GetValueOrDefault((p.PaqueteId, p.Numero)) ?? []).Where(idsVersiones.Contains).Select(v => (Version: v, p.OrganizacionId)))
            .GroupBy(x => x.Version)
            .ToDictionary(g => g.Key, g => g.Select(x => x.OrganizacionId).ToHashSet());
        var resultado = new Dictionary<Guid, List<Destinatario>>();
        var organizaciones = organizacionesPorVersion.Values.SelectMany(o => o).Distinct().ToList();
        if (organizaciones.Count == 0)
        {
            return resultado;
        }
        var personas = await plataforma.Personas.AsNoTracking()
            .Where(p => p.Tipo == TipoPersona.Externo && p.Estado == EstadoPersona.Activa && organizaciones.Contains(p.OrganizacionId))
            .OrderBy(p => p.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var persona in personas)
        {
            var permisos = await calculadora.CalcularAsync(persona.Id, ct).ConfigureAwait(false);
            var vista = await acceso.VistaDeAsync(persona.Id, persona.OrganizacionId, permisos, ct).ConfigureAwait(false);
            foreach (var (version, deLaVersion) in organizacionesPorVersion)
            {
                if (!deLaVersion.Contains(persona.OrganizacionId))
                {
                    continue;
                }
                var conLaVersion = vista.Paquetes.Where(p => p.Contratos.Any(c => c.Version.Id == version))
                    .Select(p => (p.Paquete.Id, p.RevisionPaquete.Nombre)).ToList();
                if (conLaVersion.Count > 0)
                {
                    (resultado.TryGetValue(version, out var lista) ? lista : resultado[version] = []).Add(new Destinatario(persona, true, conLaVersion));
                }
            }
        }
        return resultado;
    }

    /// <summary>Por versión, los responsables internos vigentes del aplicativo del contrato (o del ancestro más cercano con responsables).</summary>
    private async Task<Dictionary<Guid, List<Destinatario>>> InternosAsync(
        IReadOnlyList<VersionContratoSinContenido> versiones, IReadOnlyDictionary<Guid, Contrato> contratos, CancellationToken ct)
    {
        var aplicativos = (await lector.ListarAsync(ct).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var cadenas = contratos.Values.ToDictionary(c => c.Id, c => Cadena(c.AplicativoId, aplicativos));
        var porAplicativo = await responsables.DeAplicativosAsync([.. cadenas.Values.SelectMany(c => c).Distinct()], ct).ConfigureAwait(false);
        var elegidos = cadenas.ToDictionary(c => c.Key, c => c.Value.Select(a => porAplicativo.GetValueOrDefault(a)).FirstOrDefault(r => r is { Count: > 0 }) ?? []);
        var ids = elegidos.Values.SelectMany(r => r).Distinct().ToList();
        var personas = await plataforma.Personas.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa)
            .ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        return versiones.ToDictionary(v => v.Id, v => elegidos[v.ContratoId].Where(personas.ContainsKey)
            .Select(p => new Destinatario(personas[p], false, [])).ToList());
    }

    /// <summary>El aplicativo y sus ancestros, del más cercano al más lejano (sin ciclos).</summary>
    private static List<Guid> Cadena(Guid aplicativo, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        var cadena = new List<Guid> { aplicativo };
        var actual = aplicativos.GetValueOrDefault(aplicativo);
        while (actual?.PadreId is { } padre && !cadena.Contains(padre))
        {
            cadena.Add(padre);
            actual = aplicativos.GetValueOrDefault(padre);
        }
        return cadena;
    }

    /// <summary>Lo que salió (canales enviados) y si algún canal estaba pendiente.</summary>
    private sealed record Envio(List<string> Canales, bool Intentados);

    private async Task<Envio> EnviarAsync(
        Destinatario destinatario, VersionContratoSinContenido version, DateOnly retiro, MomentoAviso momento, int? dias, string titulo, string cuerpo,
        string enlace, Dictionary<string, AvisoDeprecacion> previos, Conteo conteo, CancellationToken ct)
    {
        var persona = destinatario.Persona;
        var enviados = new List<string>();
        var intentados = false;
        Guid? bandeja = null;
        foreach (var canal in new[] { CanalAviso.App, CanalAviso.Push, CanalAviso.Correo })
        {
            var clave = ReglasDeprecacion.Clave(version.Id, retiro, momento, dias, persona.Id, canal);
            if (!Pendiente(previos, clave))
            {
                continue;
            }
            intentados = true;
            if (!await PuedeSalirAsync(persona.Id, canal, ct).ConfigureAwait(false))
            {
                conteo.Omitidos++;
                continue;
            }
            var registro = await ReclamarAsync(previos, clave, version.Id, retiro, momento, dias, persona, canal, ct).ConfigureAwait(false);
            var salio = await IntentarAsync(registro, conteo, async () =>
            {
                switch (canal)
                {
                    case CanalAviso.App:
                        bandeja = (await notificaciones.RegistrarEnBandejaAsync(persona.Id, Tipo, titulo, cuerpo, enlace, ct).ConfigureAwait(false)).Id;
                        return true;
                    case CanalAviso.Push:
                        return await notificaciones.EnviarPushAsync(persona.Id, new CargaPush(bandeja ?? registro.Id, Tipo.Codigo, titulo, cuerpo, enlace), ct).ConfigureAwait(false) > 0;
                    default:
                        // Un correo por persona, solo a ella: nunca con copia a nadie más.
                        await correo.EnviarAsync(Correo(persona, titulo, cuerpo, enlace, destinatario.Externo), ct).ConfigureAwait(false);
                        return true;
                }
            }, ct).ConfigureAwait(false);
            if (salio)
            {
                enviados.Add(ReglasDeprecacion.Texto(canal));
            }
        }
        return new Envio(enviados, intentados);
    }

    /// <summary>Falta mandarlo: no hay registro, o falló y le quedan intentos. Uno <c>en_curso</c> no se repite: pudo haber salido.</summary>
    private static bool Pendiente(Dictionary<string, AvisoDeprecacion> previos, string clave) =>
        !previos.TryGetValue(clave, out var previo) || (previo.Estado == EstadoAviso.Fallido && previo.Intentos < TopeIntentos);

    /// <summary>La persona quiere ese canal para este tipo y el ambiente lo tiene (la bandeja siempre).</summary>
    private async Task<bool> PuedeSalirAsync(Guid persona, CanalAviso canal, CancellationToken ct)
    {
        var deNotificacion = canal switch
        {
            CanalAviso.App => CanalNotificacion.App,
            CanalAviso.Push => CanalNotificacion.Push,
            _ => CanalNotificacion.Correo,
        };
        if (deNotificacion == CanalNotificacion.App)
        {
            return true;
        }
        if (!CatalogoNotificaciones.Disponible(Tipo, deNotificacion, correo)
            || !await notificaciones.CanalActivoAsync(persona, Tipo, deNotificacion, ct).ConfigureAwait(false))
        {
            return false;
        }
        // Sin llaves VAPID o sin dispositivos suscritos no hay a quién mandarle push: no cuenta como falla.
        return deNotificacion != CanalNotificacion.Push
            || (llaves.Habilitado && await plataforma.SuscripcionesPush.AnyAsync(s => s.PersonaId == persona, ct).ConfigureAwait(false));
    }

    /// <summary>Marca el aviso <c>en_curso</c> antes de enviarlo (la marca es lo que evita el doble envío).</summary>
    private async Task<AvisoDeprecacion> ReclamarAsync(
        Dictionary<string, AvisoDeprecacion> previos, string clave, Guid version, DateOnly retiro, MomentoAviso momento, int? dias, Persona persona,
        CanalAviso canal, CancellationToken ct)
    {
        if (!previos.TryGetValue(clave, out var registro))
        {
            registro = new AvisoDeprecacion
            {
                Clave = clave, VersionId = version, RetiroEn = retiro, Momento = momento, Dias = dias, PersonaId = persona.Id,
                OrganizacionId = persona.OrganizacionId, Canal = canal,
            };
            contexto.AvisosDeprecacion.Add(registro);
            previos[clave] = registro;
        }
        registro.Estado = EstadoAviso.EnCurso;
        registro.Intentos++;
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return registro;
    }

    /// <summary>Envía por un canal; una falla queda en el registro (solo su tipo) y no detiene los demás canales.</summary>
    private async Task<bool> IntentarAsync(AvisoDeprecacion registro, Conteo conteo, Func<Task<bool>> enviar, CancellationToken ct)
    {
        var salio = false;
        try
        {
            if (await enviar().ConfigureAwait(false))
            {
                registro.Estado = EstadoAviso.Enviado;
                registro.EnviadoEn = reloj.GetUtcNow();
                registro.Error = null;
                conteo.Enviados++;
                salio = true;
            }
            else
            {
                // Push que ningún dispositivo aceptó (el enviador ya depuró las suscripciones vencidas).
                registro.Estado = EstadoAviso.Fallido;
                registro.Error = "sin_entrega";
                conteo.Fallidos++;
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            registro.Estado = EstadoAviso.Fallido;
            var tipo = error.GetType().Name;
            registro.Error = tipo[..Math.Min(tipo.Length, ContratosDbContext.LargoErrorAviso)];
            conteo.Fallidos++;
            // Sin destinatario ni contenido: solo el canal y el tipo de la falla.
            CanalFallido(log, ReglasDeprecacion.Texto(registro.Canal), registro.Error);
        }
        await contexto.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return salio;
    }

    /// <summary>Cada aviso por persona en la auditoría, con la organización de la persona: ids, momento y canales; nunca el correo.</summary>
    private async Task AuditarAsync(
        Destinatario destinatario, VersionContratoSinContenido version, Contrato contrato, DateOnly retiro, MomentoAviso momento, int? dias,
        IReadOnlyList<string> canales, CancellationToken ct)
    {
        plataforma.Auditoria.Add(new EventoAuditoria
        {
            OcurridoEn = reloj.GetUtcNow(),
            ActorTipo = TipoActor.Sistema,
            ActorId = "contratos.avisos_deprecacion",
            OrganizacionId = destinatario.Persona.OrganizacionId,
            Accion = AccionAviso,
            RecursoTipo = RecursoAuditoria,
            RecursoId = version.Id.ToString(),
            Version = version.Version,
            Datos = JsonSerializer.Serialize(new
            {
                persona = destinatario.Persona.Id, contrato = contrato.Id, retiroEn = retiro, momento = momento == MomentoAviso.Deprecacion ? "deprecacion" : "recordatorio",
                dias, externo = destinatario.Externo, paquetes = destinatario.Paquetes.Select(p => p.Id).ToList(), canales,
            }, JsonSerializerOptions.Web),
        });
        await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Correo con la plantilla de la marca, solo para esta persona: su primer nombre, el aviso y el enlace.</summary>
    private MensajeCorreo Correo(Persona persona, string titulo, string cuerpo, string enlace, bool externo)
    {
        var url = configuracion["Atlas:UrlPublica"];
        var baseUrl = string.IsNullOrWhiteSpace(url) ? "https://localhost:4032" : url.TrimEnd('/');
        var nombre = persona.Nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? persona.Nombre;
        var (html, texto) = PlantillaCorreo.Armar(titulo, [$"Hola, {nombre}.", cuerpo], externo ? "Ver el contrato en el portal" : "Ver el contrato", $"{baseUrl}{enlace}",
            ["Recibes este correo porque tienes activos los avisos de deprecación de contratos por correo. Puedes apagarlos en Atlas, en Ajustes, sección Notificaciones."]);
        return new MensajeCorreo(persona.Correo, persona.Nombre, titulo, html, texto);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Contratos: avisos de deprecación de {Versiones} versiones a {Personas} personas: {Enviados} enviados, {Fallidos} fallidos, {Omitidos} omitidos")]
    private static partial void Corrida(ILogger log, int versiones, int personas, int enviados, int fallidos, int omitidos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Contratos: un aviso de deprecación por {Canal} no salió ({Error}); se reintenta en la corrida siguiente")]
    private static partial void CanalFallido(ILogger log, string canal, string error);
}
