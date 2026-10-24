using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Contratos.Exportaciones;

/// <summary>Un archivo de una exportación en el S3, tal como lo informa atlas-docs.</summary>
public sealed record ArchivoExportado(string Clave, string Ruta, string Formato, long Bytes, string Sha256, string Tipo);

/// <summary><c>data</c> de <c>atlas.contratos.exportacion_solicitada</c> v1.</summary>
internal sealed record DatosExportacionSolicitada(Guid ExportacionId, IReadOnlyList<string> Formatos, string EntradaClave, OrigenEvento Origen);

/// <summary>Origen en el evento: <c>{tipo: paquete, paqueteId, revision}</c> o <c>{tipo: version, versionId}</c>.</summary>
internal sealed record OrigenEvento(string Tipo, Guid? PaqueteId, int? Revision, Guid? VersionId);

/// <summary><c>data</c> de <c>atlas.contratos.exportacion_iniciada</c> v1.</summary>
internal sealed record DatosExportacionIniciada(Guid ExportacionId);

/// <summary><c>data</c> de <c>atlas.contratos.exportacion_lista</c> v1.</summary>
internal sealed record DatosExportacionLista(Guid ExportacionId, string HashContenido, IReadOnlyList<ArchivoExportado> Archivos);

/// <summary><c>data</c> de <c>atlas.contratos.exportacion_fallida</c> v1.</summary>
internal sealed record DatosExportacionFallida(Guid ExportacionId, string Codigo, string Mensaje);

/// <summary>Formatos de exportación (los de atlas-docs, ATLAS-ADR-0042), en su orden.</summary>
internal static class FormatosExportacion
{
    public const string Zip = "zip";

    public static readonly IReadOnlyList<string> Todos = ["openapi", "postman", "html", "pdf", "word", "excel", Zip];

    /// <summary>Sin repetidos, en el orden de <see cref="Todos"/> y siempre con el ZIP; null si alguno no existe o no hay ninguno.</summary>
    public static IReadOnlyList<string>? Normalizar(IReadOnlyList<string?>? pedidos)
    {
        var lista = (pedidos ?? []).Select(f => f?.Trim().ToLowerInvariant()).ToList();
        if (lista.Count == 0 || lista.Any(f => f is null || !Todos.Contains(f)))
        {
            return null;
        }
        return [.. Todos.Where(f => f == Zip || lista.Contains(f))];
    }
}

/// <summary>
/// Lo común a las exportaciones internas y del portal (HU 4.5, ATLAS-ADR-0042): crear (subir la entrada al S3; guardar
/// la fila, el evento de la outbox y la auditoría en una sola transacción) y auditar las descargas. Lo de más de ~1 s
/// (generar) no pasa aquí: lo hace atlas-docs al leer la cola (prompt maestro §5.1).
/// </summary>
internal static class ServicioExportaciones
{
    public const string RecursoAuditoria = "contratos.exportacion";
    public const string AccionSolicitada = "contratos.exportacion_solicitada";
    public const string AccionDescargada = "contratos.exportacion_descargada";
    public const string AccionFallida = "contratos.exportacion_fallida";
    public const string AccionVencida = "contratos.exportacion_vencida";

    public static string Prefijo(Guid id) => $"exportaciones/{id:D}/";

    public static string ClaveEntrada(Guid id) => Prefijo(id) + "entrada.json";

    public static IReadOnlyList<ArchivoExportado> LeerArchivos(string json) =>
        JsonSerializer.Deserialize<List<ArchivoExportado>>(json, EntradaExportacion.Json) ?? [];

    /// <summary>
    /// Sube la entrada y guarda la exportación pendiente con su evento y su auditoría. Si la base falla, la entrada queda
    /// huérfana en el S3 (no tiene datos que nadie más lea; la limpia la regla de ciclo de vida del bucket, si existe).
    /// </summary>
    public static async Task<ExportacionContratos> CrearAsync(
        ContratosDbContext contexto, PlataformaDbContext plataforma, Plataforma.Almacenamiento.IAlmacenObjetos almacen,
        ExportacionContratos borrador, EntradaExportacion entrada, ActorAuditoria actor, DateTimeOffset ahora, CancellationToken ct)
    {
        var bytes = entrada.AJson();
        await almacen.SubirAsync(borrador.EntradaClave, bytes, "application/json", ct).ConfigureAwait(false);
        var exportacion = new ExportacionContratos
        {
            Id = borrador.Id,
            SolicitadaPor = borrador.SolicitadaPor,
            OrganizacionId = borrador.OrganizacionId,
            DesdePortal = borrador.DesdePortal,
            Origen = borrador.Origen,
            PaqueteId = borrador.PaqueteId,
            Revision = borrador.Revision,
            VersionId = borrador.VersionId,
            ContratoId = borrador.ContratoId,
            Nombre = borrador.Nombre,
            Formatos = borrador.Formatos,
            EntradaClave = borrador.EntradaClave,
            EntradaSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            SolicitadaEn = ahora,
        };
        contexto.Exportaciones.Add(exportacion);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct).ConfigureAwait(false);
        plataforma.Auditoria.Add(Evento(actor, ahora, AccionSolicitada, exportacion, new
        {
            origen = Texto(exportacion.Origen),
            paqueteId = exportacion.PaqueteId,
            revision = exportacion.Revision,
            versionId = exportacion.VersionId,
            formatos = exportacion.Formatos,
            portal = exportacion.DesdePortal,
            entradaSha256 = exportacion.EntradaSha256,
            specs = entrada.Specs.Select(s => new { s.Contrato, s.Version }).ToList(),
            guias = entrada.Guias.Count,
        }));
        plataforma.EncolarEvento(EventosContratos.ExportacionSolicitada, new DatosExportacionSolicitada(
            exportacion.Id, exportacion.Formatos, exportacion.EntradaClave,
            new OrigenEvento(Texto(exportacion.Origen), exportacion.PaqueteId, exportacion.Revision, exportacion.VersionId)),
            exportacion.OrganizacionId);
        await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);
        await plataforma.Database.UseTransactionAsync(null, ct).ConfigureAwait(false);
        return exportacion;
    }

    /// <summary>Audita una descarga (qué archivo, su tamaño y el hash del contenido; nunca el contenido ni la URL firmada).</summary>
    public static async Task AuditarDescargaAsync(
        PlataformaDbContext plataforma, ActorAuditoria actor, DateTimeOffset ahora, ExportacionContratos exportacion, ArchivoExportado archivo, CancellationToken ct)
    {
        plataforma.Auditoria.Add(Evento(actor, ahora, AccionDescargada, exportacion, new
        {
            archivo = archivo.Ruta,
            formato = archivo.Formato,
            bytes = archivo.Bytes,
            sha256 = archivo.Sha256,
            hashContenido = exportacion.HashContenido,
            portal = exportacion.DesdePortal,
        }));
        await plataforma.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Quién hace la acción, para la auditoría.</summary>
    public sealed record ActorAuditoria(TipoActor Tipo, string? Id, Guid? OrganizacionId, IPAddress? Ip, string? Correlacion);

    public static EventoAuditoria Evento(ActorAuditoria actor, DateTimeOffset ahora, string accion, ExportacionContratos exportacion, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = actor.Tipo,
        ActorId = actor.Id,
        OrganizacionId = actor.OrganizacionId ?? exportacion.OrganizacionId,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = exportacion.Id.ToString(),
        Version = exportacion.Revision?.ToString(CultureInfo.InvariantCulture),
        Ip = actor.Ip,
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = actor.Correlacion,
    };

    public static string Texto(OrigenExportacion o) => o == OrigenExportacion.Paquete ? "paquete" : "version";

    public static string Texto(EstadoExportacion e) => e switch
    {
        EstadoExportacion.Pendiente => "pendiente",
        EstadoExportacion.Procesando => "procesando",
        EstadoExportacion.Lista => "lista",
        EstadoExportacion.Fallida => "fallida",
        _ => "vencida",
    };

    /// <summary>Nombre con que se descarga un archivo: el último tramo de su ruta.</summary>
    public static string NombreArchivo(string ruta)
    {
        var i = ruta.LastIndexOf('/');
        return i >= 0 ? ruta[(i + 1)..] : ruta;
    }
}
