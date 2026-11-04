using System.Globalization;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Notificaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Flit.Atlas.Modulos.Contratos.Exportaciones.ServicioExportaciones;

namespace Flit.Atlas.Modulos.Contratos.Exportaciones;

/// <summary>
/// Cola <c>atlas.api.exportaciones</c> (ATLAS-ADR-0042 y ATLAS-ADR-0018): los resultados que atlas-docs publica en
/// <c>flit.atlas.docs</c>. <c>iniciada</c> pasa la exportación a procesando; <c>lista</c> guarda los archivos, fija la
/// retención y avisa a la persona («Tu exportación está lista», bandeja y push); <c>fallida</c> guarda el error, lo audita
/// y avisa. La inbox y el cambio van en la misma transacción; además, un resultado para una exportación ya terminada se
/// ignora (atlas-docs regenera lo mismo si la cola le reentrega la solicitud).
/// </summary>
internal sealed partial class ManejadorResultadosExportacion(
    IOptions<OpcionesExportaciones> opciones, TimeProvider reloj, ILogger<ManejadorResultadosExportacion> log) : IManejadorEventos
{
    public const string NombreCola = "atlas.api.exportaciones";

    /// <summary>Exchange de atlas-docs: solo atlas-api publica en <c>flit.atlas</c> (ATLAS-ADR-0018).</summary>
    public const string ExchangeDocs = "flit.atlas.docs";

    public string Cola => NombreCola;

    public string Exchange => ExchangeDocs;

    public IReadOnlyList<TipoEvento> Tipos { get; } =
        [EventosContratos.ExportacionIniciada, EventosContratos.ExportacionLista, EventosContratos.ExportacionFallida];

    public static readonly TipoNotificacion Notificacion = new(
        "contratos.exportacion", "Exportaciones de contratos", "Cuando una exportación que pediste queda lista o falla.",
        new HashSet<CanalNotificacion> { CanalNotificacion.App, CanalNotificacion.Push });

    private const int LargoError = ContratosDbContext.LargoErrorExportacion;

    public async Task ManejarAsync(SobreEvento sobre, IServiceProvider servicios, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sobre);
        var contexto = servicios.GetRequiredService<ContratosDbContext>();
        var plataforma = servicios.GetRequiredService<PlataformaDbContext>();
        var id = sobre.Type switch
        {
            _ when sobre.Type == EventosContratos.ExportacionIniciada.Nombre => sobre.DatosComo<DatosExportacionIniciada>().ExportacionId,
            _ when sobre.Type == EventosContratos.ExportacionLista.Nombre => sobre.DatosComo<DatosExportacionLista>().ExportacionId,
            _ => sobre.DatosComo<DatosExportacionFallida>().ExportacionId,
        };
        var exportacion = await contexto.Exportaciones.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (exportacion is null)
        {
            ExportacionDesconocida(log, id, sobre.Type);
            return;
        }

        var ahora = reloj.GetUtcNow();
        EventoAuditoria? auditoria = null;
        Aviso? aviso = null;
        if (sobre.Type == EventosContratos.ExportacionIniciada.Nombre)
        {
            if (exportacion.Estado != EstadoExportacion.Pendiente)
            {
                return;
            }
            exportacion.Estado = EstadoExportacion.Procesando;
            exportacion.IniciadaEn = ahora;
        }
        else if (Terminada(exportacion.Estado))
        {
            ResultadoRepetido(log, id, sobre.Type);
            return;
        }
        else if (sobre.Type == EventosContratos.ExportacionLista.Nombre)
        {
            var datos = sobre.DatosComo<DatosExportacionLista>();
            var prefijo = Prefijo(exportacion.Id);
            var archivos = datos.Archivos.Where(a => a.Clave.StartsWith(prefijo, StringComparison.Ordinal) && !a.Clave.Contains("..", StringComparison.Ordinal)).ToList();
            var zip = archivos.FirstOrDefault(a => a.Formato == FormatosExportacion.Zip);
            if (zip is null || archivos.Count != datos.Archivos.Count || !EsHash(datos.HashContenido))
            {
                // Un resultado que no cumple el contrato no se da por bueno: queda fallida y se avisa.
                Fallar(exportacion, ahora, "interno", "atlas-docs devolvió un resultado incompleto.");
                auditoria = AuditoriaFallida(exportacion, ahora, "interno");
                aviso = AvisoFallida(exportacion);
            }
            else
            {
                exportacion.Estado = EstadoExportacion.Lista;
                exportacion.Objeto = zip.Clave;
                exportacion.HashContenido = datos.HashContenido;
                exportacion.Archivos = JsonSerializer.Serialize(archivos, EntradaExportacion.Json);
                exportacion.IniciadaEn ??= ahora;
                exportacion.TerminadaEn = ahora;
                exportacion.VenceEn = ahora + opciones.Value.Retencion;
                aviso = new Aviso("Tu exportación está lista",
                    $"{exportacion.Nombre}: disponible hasta el {ArmadorEntrada.FechaBogota(exportacion.VenceEn.Value).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.",
                    Enlace(exportacion));
            }
        }
        else
        {
            var datos = sobre.DatosComo<DatosExportacionFallida>();
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
            if (!await plataforma.RegistrarEnInboxAsync(sobre, NombreCola, ct).ConfigureAwait(false))
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

    /// <summary>Códigos de error que atlas-docs informa; cualquier otro se guarda como <c>interno</c>.</summary>
    internal static readonly IReadOnlySet<string> CodigosFalla = new HashSet<string>(StringComparer.Ordinal)
    {
        "spec_invalido", "pdf_no_disponible", "entrada_invalida", "tiempo_agotado", "interno",
    };

    internal static bool Terminada(EstadoExportacion estado) =>
        estado is EstadoExportacion.Lista or EstadoExportacion.Fallida or EstadoExportacion.Vencida;

    internal static void Fallar(ExportacionContratos exportacion, DateTimeOffset ahora, string codigo, string? mensaje)
    {
        exportacion.Estado = EstadoExportacion.Fallida;
        exportacion.ErrorCodigo = codigo;
        var texto = string.IsNullOrWhiteSpace(mensaje) ? "La exportación no se pudo generar." : mensaje.Trim();
        exportacion.Error = texto.Length <= LargoError ? texto : texto[..LargoError];
        exportacion.TerminadaEn = ahora;
        // La entrada sigue en el S3: la retención la borra igual que los archivos de una lista.
        exportacion.VenceEn = ahora + TimeSpan.FromDays(1);
    }

    internal static EventoAuditoria AuditoriaFallida(ExportacionContratos exportacion, DateTimeOffset ahora, string codigo) =>
        Evento(new ActorAuditoria(TipoActor.Sistema, "atlas-docs", null, null, null), ahora, AccionFallida, exportacion, new
        {
            codigo,
            origen = Texto(exportacion.Origen),
            paqueteId = exportacion.PaqueteId,
            revision = exportacion.Revision,
            versionId = exportacion.VersionId,
            portal = exportacion.DesdePortal,
        });

    internal sealed record Aviso(string Titulo, string Cuerpo, string Enlace);

    internal static Aviso AvisoFallida(ExportacionContratos exportacion) =>
        new("Tu exportación falló", $"{exportacion.Nombre}: {exportacion.Error}", Enlace(exportacion));

    /// <summary>A dónde lleva el aviso: el paquete (o el contrato) con la exportación abierta.</summary>
    internal static string Enlace(ExportacionContratos e) => e switch
    {
        { DesdePortal: true } => $"/portal/paquetes/{e.PaqueteId}?exportacion={e.Id}",
        { Origen: OrigenExportacion.Paquete } => $"/contratos/paquetes?paquete={e.PaqueteId}&exportacion={e.Id}",
        _ => $"/contratos/{e.ContratoId}?exportacion={e.Id}",
    };

    /// <summary>El aviso no deshace el resultado: si la bandeja o el push fallan, queda en el log.</summary>
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resultado {Tipo} de una exportación que no existe: {ExportacionId}")]
    private static partial void ExportacionDesconocida(ILogger log, Guid exportacionId, string tipo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resultado {Tipo} repetido para la exportación {ExportacionId}: se ignora")]
    private static partial void ResultadoRepetido(ILogger log, Guid exportacionId, string tipo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo avisar de una exportación")]
    private static partial void AvisoFallido(ILogger log, Exception ex);
}
