using System.Globalization;
using System.Text.Json;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma.Almacenamiento;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Documentacion.Exportaciones;

/// <summary>Un archivo de una exportación en el S3, tal como lo informa atlas-docs.</summary>
public sealed record ArchivoExportadoDocumentos(string Clave, string Ruta, string Formato, long Bytes, string Sha256, string Tipo);

/// <summary>
/// Los resultados que atlas-docs publica en <c>flit.atlas.docs</c> para los documentos (ATLAS-ADR-0053 §7), como
/// <c>ManejadorResultadosExportacion</c> de contratos. Comparte la cola <c>atlas.api.exportaciones</c>: el consumidor
/// despacha por tipo, y así no hace falta otro permiso en RabbitMQ.
/// </summary>
internal sealed partial class ManejadorResultadosDocumentos(
    IOptions<OpcionesExportacionesDocumentos> opciones, TimeProvider reloj, ILogger<ManejadorResultadosDocumentos> log) : IManejadorEventos
{
    public const string NombreCola = "atlas.api.exportaciones";

    public const string ExchangeDocs = "flit.atlas.docs";

    private const string ConsumidorInbox = "atlas.api.exportaciones.documentacion";

    private sealed record DatosIniciada(Guid ExportacionId);

    private sealed record DatosLista(Guid ExportacionId, string HashContenido, IReadOnlyList<ArchivoExportadoDocumentos> Archivos);

    private sealed record DatosFallida(Guid ExportacionId, string Codigo, string Mensaje);

    internal sealed record Aviso(string Titulo, string Cuerpo, string Enlace);

    public static readonly TipoNotificacion Notificacion = new(
        "documentacion.exportacion", "Exportaciones de documentos", "Cuando una exportación de documentos que pediste queda lista o falla.",
        new HashSet<CanalNotificacion> { CanalNotificacion.App, CanalNotificacion.Push });

    private static readonly HashSet<string> CodigosFalla = new HashSet<string>(StringComparer.Ordinal)
    {
        "pdf_no_disponible", "entrada_invalida", "tiempo_agotado", "interno",
    };

    public string Cola => NombreCola;

    public string Exchange => ExchangeDocs;

    public IReadOnlyList<TipoEvento> Tipos { get; } =
        [EventosDocumentacion.ExportacionIniciada, EventosDocumentacion.ExportacionLista, EventosDocumentacion.ExportacionFallida];

    public async Task ManejarAsync(SobreEvento sobre, IServiceProvider servicios, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sobre);
        var contexto = servicios.GetRequiredService<DocumentacionDbContext>();
        var plataforma = servicios.GetRequiredService<PlataformaDbContext>();
        var id = sobre.Type == EventosDocumentacion.ExportacionIniciada.Nombre ? sobre.DatosComo<DatosIniciada>().ExportacionId
            : sobre.Type == EventosDocumentacion.ExportacionLista.Nombre ? sobre.DatosComo<DatosLista>().ExportacionId
            : sobre.DatosComo<DatosFallida>().ExportacionId;
        var exportacion = await contexto.Exportaciones.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (exportacion is null)
        {
            Desconocida(log, id, sobre.Type);
            return;
        }

        var ahora = reloj.GetUtcNow();
        EventoAuditoria? auditoria = null;
        Aviso? aviso = null;
        if (sobre.Type == EventosDocumentacion.ExportacionIniciada.Nombre)
        {
            if (exportacion.Estado != EstadoExportacionDocumentos.Pendiente)
            {
                return;
            }
            exportacion.Estado = EstadoExportacionDocumentos.Procesando;
            exportacion.IniciadaEn = ahora;
        }
        else if (exportacion.Estado is EstadoExportacionDocumentos.Lista or EstadoExportacionDocumentos.Fallida or EstadoExportacionDocumentos.Vencida)
        {
            return;
        }
        else if (sobre.Type == EventosDocumentacion.ExportacionLista.Nombre)
        {
            var datos = sobre.DatosComo<DatosLista>();
            var prefijo = EndpointsExportacionesDocumentos.Prefijo(exportacion.Id);
            var archivos = datos.Archivos.Where(a => a.Clave.StartsWith(prefijo, StringComparison.Ordinal) && !a.Clave.Contains("..", StringComparison.Ordinal)).ToList();
            var zip = archivos.FirstOrDefault(a => a.Formato == FormatosDocumentos.Zip);
            if (zip is null || archivos.Count != datos.Archivos.Count || !EsHash(datos.HashContenido))
            {
                Fallar(exportacion, ahora, "interno", "atlas-docs devolvió un resultado incompleto.");
                auditoria = AuditoriaFallida(exportacion, ahora, "interno");
                aviso = AvisoFallida(exportacion);
            }
            else
            {
                exportacion.Estado = EstadoExportacionDocumentos.Lista;
                exportacion.Objeto = zip.Clave;
                exportacion.HashContenido = datos.HashContenido;
                exportacion.Archivos = JsonSerializer.Serialize(archivos, EndpointsExportacionesDocumentos.Json);
                exportacion.IniciadaEn ??= ahora;
                exportacion.TerminadaEn = ahora;
                exportacion.VenceEn = ahora + opciones.Value.Retencion;
                var hasta = TimeZoneInfo.ConvertTime(exportacion.VenceEn.Value, TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"));
                aviso = new Aviso("Tu exportación está lista",
                    $"{exportacion.Nombre}: disponible hasta el {hasta.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.", Enlace(exportacion));
            }
        }
        else
        {
            var datos = sobre.DatosComo<DatosFallida>();
            var codigo = CodigosFalla.Contains(datos.Codigo) ? datos.Codigo : "interno";
            Fallar(exportacion, ahora, codigo, datos.Mensaje);
            auditoria = AuditoriaFallida(exportacion, ahora, codigo);
            aviso = AvisoFallida(exportacion);
        }

        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
            plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
            await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct).ConfigureAwait(false);
            if (!await plataforma.RegistrarEnInboxAsync(sobre, ConsumidorInbox, ct).ConfigureAwait(false))
            {
                await transaccion.RollbackAsync(ct).ConfigureAwait(false);
                return;
            }
            if (auditoria is not null)
            {
                plataforma.Auditoria.Add(auditoria);
            }
            await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            await plataforma.Database.UseTransactionAsync(null, ct).ConfigureAwait(false);
        }
        if (aviso is not null)
        {
            await AvisarAsync(servicios, exportacion.SolicitadaPor, aviso, ct).ConfigureAwait(false);
        }
    }

    internal static void Fallar(ExportacionDocumentos exportacion, DateTimeOffset ahora, string codigo, string? mensaje)
    {
        exportacion.Estado = EstadoExportacionDocumentos.Fallida;
        exportacion.ErrorCodigo = codigo;
        var texto = string.IsNullOrWhiteSpace(mensaje) ? "La exportación no se pudo generar." : mensaje.Trim();
        exportacion.Error = texto.Length <= DocumentacionDbContext.LargoErrorExportacion ? texto : texto[..DocumentacionDbContext.LargoErrorExportacion];
        exportacion.TerminadaEn = ahora;
        // La entrada sigue en el S3: la retención la borra igual que los archivos de una lista.
        exportacion.VenceEn = ahora + TimeSpan.FromDays(1);
    }

    internal static EventoAuditoria AuditoriaFallida(ExportacionDocumentos exportacion, DateTimeOffset ahora, string codigo) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = TipoActor.Sistema,
        ActorId = "atlas-docs",
        Accion = EndpointsExportacionesDocumentos.AccionFallida,
        RecursoTipo = EndpointsExportacionesDocumentos.RecursoAuditoria,
        RecursoId = exportacion.Id.ToString(),
        Datos = JsonSerializer.Serialize(new { codigo }, EndpointsExportacionesDocumentos.Json),
    };

    internal static Aviso AvisoFallida(ExportacionDocumentos exportacion) =>
        new("Tu exportación falló", $"{exportacion.Nombre}: {exportacion.Error}", Enlace(exportacion));

    /// <summary>A dónde lleva el aviso: el documento (o la lista, si es una colección) con la exportación abierta.</summary>
    internal static string Enlace(ExportacionDocumentos e) =>
        e.DocumentoId is { } documento ? $"/documentacion/{documento}?exportacion={e.Id}" : $"/documentacion?exportacion={e.Id}";

    internal async Task AvisarAsync(IServiceProvider servicios, Guid persona, Aviso aviso, CancellationToken ct)
    {
        try
        {
            await servicios.GetRequiredService<ServicioNotificaciones>()
                .NotificarAsync(persona, Notificacion, aviso.Titulo, aviso.Cuerpo, aviso.Enlace, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // El resultado ya quedó guardado; el aviso es de mejor esfuerzo.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AvisoFallido(log, ex);
        }
    }

    private static bool EsHash(string? valor) => valor is { Length: 64 } && valor.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resultado {Tipo} de una exportación de documentos que no existe: {ExportacionId}")]
    private static partial void Desconocida(ILogger log, Guid exportacionId, string tipo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo avisar de una exportación de documentos")]
    private static partial void AvisoFallido(ILogger log, Exception ex);
}

/// <summary>
/// Retención (como <c>VencedorExportaciones</c> de contratos): las listas o fallidas pierden sus archivos al vencer y las que
/// no terminan quedan fallidas por tiempo. Seguro con varias réplicas: cada cambio de estado es condicional.
/// </summary>
internal sealed partial class VencedorExportacionesDocumentos(
    IServiceScopeFactory alcances, IOptions<OpcionesExportacionesDocumentos> opciones, TimeProvider reloj, ILogger<VencedorExportacionesDocumentos> log)
    : BackgroundService
{
    private const int Lote = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VencerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Un ciclo fallido (base o S3 caídos) se registra y se reintenta en el siguiente.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                CicloFallido(log, ex);
            }
            try
            {
                await Task.Delay(opciones.Value.IntervaloVencimiento, reloj, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<(int Vencidas, int Agotadas)> VencerAsync(CancellationToken ct)
    {
        using var alcance = alcances.CreateScope();
        var servicios = alcance.ServiceProvider;
        var contexto = servicios.GetRequiredService<DocumentacionDbContext>();
        var almacen = servicios.GetRequiredService<IAlmacenObjetos>();
        var ahora = reloj.GetUtcNow();

        var vencidas = 0;
        if (almacen.Disponible)
        {
            var porVencer = await contexto.Exportaciones.AsNoTracking()
                .Where(e => (e.Estado == EstadoExportacionDocumentos.Lista || e.Estado == EstadoExportacionDocumentos.Fallida) && e.VenceEn != null && e.VenceEn <= ahora)
                .OrderBy(e => e.VenceEn).Take(Lote).Select(e => new { e.Id, e.Estado })
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var e in porVencer)
            {
                await almacen.BorrarPrefijoAsync(EndpointsExportacionesDocumentos.Prefijo(e.Id), ct).ConfigureAwait(false);
                vencidas += await contexto.Exportaciones
                    .Where(x => x.Id == e.Id && x.Estado == e.Estado)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, EstadoExportacionDocumentos.Vencida).SetProperty(x => x.VencidaEn, ahora), ct)
                    .ConfigureAwait(false);
            }
        }

        var limite = ahora - opciones.Value.TiempoMaximo;
        var atascadas = await contexto.Exportaciones.AsNoTracking()
            .Where(e => (e.Estado == EstadoExportacionDocumentos.Pendiente || e.Estado == EstadoExportacionDocumentos.Procesando) && e.SolicitadaEn <= limite)
            .OrderBy(e => e.SolicitadaEn).Take(Lote)
            .ToListAsync(ct).ConfigureAwait(false);
        var plataforma = servicios.GetRequiredService<PlataformaDbContext>();
        var manejador = ActivatorUtilities.CreateInstance<ManejadorResultadosDocumentos>(servicios);
        var agotadas = 0;
        foreach (var e in atascadas)
        {
            var estado = e.Estado;
            ManejadorResultadosDocumentos.Fallar(e, ahora, "tiempo_agotado", "La exportación tardó demasiado; solicítala de nuevo.");
            var cambiadas = await contexto.Exportaciones
                .Where(x => x.Id == e.Id && x.Estado == estado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Estado, e.Estado)
                    .SetProperty(x => x.ErrorCodigo, e.ErrorCodigo)
                    .SetProperty(x => x.Error, e.Error)
                    .SetProperty(x => x.TerminadaEn, e.TerminadaEn)
                    .SetProperty(x => x.VenceEn, e.VenceEn)
                    .SetProperty(x => x.ActualizadoEn, ahora), ct)
                .ConfigureAwait(false);
            if (cambiadas == 0)
            {
                continue;
            }
            agotadas++;
            plataforma.Auditoria.Add(ManejadorResultadosDocumentos.AuditoriaFallida(e, ahora, "tiempo_agotado"));
            await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
            await manejador.AvisarAsync(servicios, e.SolicitadaPor, ManejadorResultadosDocumentos.AvisoFallida(e), ct).ConfigureAwait(false);
        }
        return (vencidas, agotadas);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falló el ciclo de retención de exportaciones de documentos; se reintenta")]
    private static partial void CicloFallido(ILogger log, Exception ex);
}
