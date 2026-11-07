using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Mensajeria;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Documentacion;

/// <summary>Piezas compartidas por la publicación desde la CI y desde Atlas: una sola forma de versión (ATLAS-ADR-0053 §2).</summary>
internal static class PublicacionDocumentos
{
    public const string RecursoAuditoria = "documentacion.documento";
    public const string AccionVersionPublicada = "documentacion.version_publicada";
    public const string AccionCanalActualizado = "documentacion.canal_actualizado";
    public const string AccionCanalRetirado = "documentacion.canal_retirado";
    public const string AccionCreado = "documentacion.documento_creado";
    public const string AccionRevisado = "documentacion.documento_revisado";
    public const string AccionRestaurado = "documentacion.documento_restaurado";
    public const string AccionEnviado = "documentacion.documento_enviado_a_revision";
    public const string AccionDevuelto = "documentacion.documento_devuelto";
    public const string AccionArchivado = "documentacion.documento_archivado";

    /// <summary><c>data</c> de <c>atlas.documentacion.version_publicada</c> v1 (contracts/asyncapi/atlas-events.v1.yaml).</summary>
    internal sealed record DatosVersionPublicada(
        Guid DocumentoId, Guid VersionId, int Numero, Guid AplicativoId, string Aplicativo, string Origen, string Tipo, string Visibilidad,
        bool EsBorrador, string Sha256, string? Ambiente, string? Repositorio, string? Rama, string? Commit);

    /// <summary>Mueve el canal a la versión; devuelve si cambió. La lista queda al día para <see cref="RecalcularVigenteAsync"/>.</summary>
    public static bool MoverCanal(DocumentacionDbContext contexto, List<CanalDocumento> canales, Guid documentoId, string ambiente, int numero)
    {
        var canal = canales.FirstOrDefault(c => c.Ambiente == ambiente);
        if (canal is null)
        {
            canal = new CanalDocumento { DocumentoId = documentoId, Ambiente = ambiente, VersionNumero = numero };
            canales.Add(canal);
            contexto.Canales.Add(canal);
            return true;
        }
        if (canal.VersionNumero == numero)
        {
            return false;
        }
        canal.VersionNumero = numero;
        return true;
    }

    /// <summary>
    /// La versión vigente es la del canal pdn o, sin él, la última; el documento copia su foto: lo que se lista es lo que se
    /// puede leer, y una versión de develop no cambia el título ni la visibilidad de lo que corre en pdn.
    /// </summary>
    public static async Task RecalcularVigenteAsync(DocumentacionDbContext contexto, Documento documento, IReadOnlyList<CanalDocumento> canales, CancellationToken ct)
    {
        var numero = canales.FirstOrDefault(c => c.Ambiente == OpcionesDocumentacion.CanalVigente)?.VersionNumero ?? documento.VersionActual;
        if (numero <= 0 || documento.VersionVigente == numero)
        {
            return;
        }
        var version = contexto.Versiones.Local.FirstOrDefault(v => v.DocumentoId == documento.Id && v.Numero == numero)
            ?? await contexto.Versiones.AsNoTracking().FirstAsync(v => v.DocumentoId == documento.Id && v.Numero == numero, ct);
        documento.VersionVigente = numero;
        documento.Tipo = version.Tipo;
        documento.Titulo = version.Titulo;
        documento.Visibilidad = version.Visibilidad;
        documento.Audiencias = [.. version.Audiencias];
        documento.Responsable = version.Responsable;
    }

    /// <summary>Auditoría de una acción: ids, números, visibilidad y hash; nunca el título, el cuerpo ni la nota.</summary>
    public static EventoAuditoria Evento(HttpContext http, DateTimeOffset ahora, string accion, Guid documentoId, int? numero, TipoActor actor, string? actorId, object datos) => new()
    {
        OcurridoEn = ahora,
        ActorTipo = actor,
        ActorId = actorId is { Length: > 200 } ? actorId[..200] : actorId,
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var org) ? org : null,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = documentoId.ToString(),
        Version = numero?.ToString(CultureInfo.InvariantCulture),
        Ip = http.Connection.RemoteIpAddress,
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>Los cambios del documento, su auditoría y su evento en <b>una sola transacción</b> (outbox, ATLAS-ADR-0018).</summary>
    public static async Task GuardarAsync(
        DocumentacionDbContext contexto, PlataformaDbContext plataforma, EventoAuditoria auditoria, DatosVersionPublicada? evento, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.SaveChangesAsync(ct);
        plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        try
        {
            plataforma.Auditoria.Add(auditoria);
            if (evento is not null)
            {
                plataforma.EncolarEvento(EventosDocumentacion.VersionPublicada, evento);
            }
            await plataforma.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        finally
        {
            await plataforma.Database.UseTransactionAsync(null, ct);
            plataforma.ChangeTracker.Clear();
        }
    }
}
