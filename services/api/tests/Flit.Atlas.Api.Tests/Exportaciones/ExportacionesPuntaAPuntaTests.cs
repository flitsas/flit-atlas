using System.IO.Compression;
using System.Net;
using DocumentFormat.OpenXml.Packaging;
using Flit.Atlas.Modulos.Contratos.Exportaciones;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Api.Tests.Exportaciones.HttpExportaciones;

namespace Flit.Atlas.Api.Tests.Exportaciones;

/// <summary>
/// CA de la HU 4.5 de punta a punta, sin simular nada: atlas-api (con su outbox y su cola) + RabbitMQ + RustFS +
/// atlas-docs real (la imagen de <c>services/docs/Dockerfile</c>, con Pango). Exportar el paquete ICT genera los siete
/// formatos; el Excel abre; la URL firmada sirve y después vence; solicitar y descargar quedan auditados; llega el aviso.
/// </summary>
public sealed class ExportacionesPuntaAPuntaTests(EntornoApi entorno, ContenedoresConDocs contenedores) : IClassFixture<ContenedoresConDocs>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exportar_el_paquete_ICT_genera_los_siete_formatos_la_URL_vence_y_se_audita()
    {
        var baseDeDatos = await entorno.BaseNuevaAsync();
        var paquete = await ContratosDePrueba.SembrarAsync(baseDeDatos, ct: Ct);
        // URL de 3 segundos para ver el vencimiento sin esperar 15 minutos (el valor por defecto lo prueba ExportacionesTests).
        await using var api = entorno.Api(baseDeDatos: baseDeDatos, configuracion: contenedores.Configuracion(conBroker: true, new()
        {
            [$"{OpcionesExportaciones.Seccion}:DuracionUrl"] = "00:00:03",
        }));
        var (navegador, persona) = await EmpleadoAsync(entorno, api, RolExportador, baseDeDatos);

        var solicitud = await PostAsync(navegador, "/api/v1/contratos/exportaciones",
            new { paqueteId = paquete.PaqueteId, formatos = new[] { "openapi", "postman", "html", "pdf", "word", "excel" } });

        Assert.Equal(HttpStatusCode.Accepted, solicitud.StatusCode);
        var id = (await JsonAsync(solicitud)).GetProperty("id").GetString()!;
        var lista = await EsperarAsync(navegador, $"/api/v1/contratos/exportaciones/{id}", TimeSpan.FromMinutes(5));
        Assert.True(lista.GetProperty("estado").GetString() == "lista", lista.ToString());
        Assert.Matches("^[0-9a-f]{64}$", lista.GetProperty("hashContenido").GetString()!);
        var archivos = lista.GetProperty("archivos").EnumerateArray().ToList();
        Assert.Equal(FormatosExportacion.Todos.Order(StringComparer.Ordinal),
            archivos.Select(a => a.GetProperty("formato").GetString()!).Distinct().Order(StringComparer.Ordinal));

        // El ZIP con la URL firmada: sirve, como adjunto, y trae los siete formatos.
        var descarga = await JsonAsync(await navegador.GetAsync($"/api/v1/contratos/exportaciones/{id}/descarga"));
        var url = new Uri(descarga.GetProperty("url").GetString()!);
        using var s3 = new HttpClient();
        using (var zip = await s3.GetAsync(url, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
            Assert.StartsWith("attachment", zip.Content.Headers.ContentDisposition?.ToString() ?? string.Empty, StringComparison.Ordinal);
            var bytes = await zip.Content.ReadAsByteArrayAsync(Ct);
            Assert.Equal(descarga.GetProperty("bytes").GetInt64(), bytes.Length);
            using var archivo = new ZipArchive(new MemoryStream(bytes));
            var extensiones = archivo.Entries.Select(e => Path.GetExtension(e.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Superset(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".yaml", ".json", ".html", ".pdf", ".docx", ".xlsx" }, extensiones);
        }

        // El Excel suelto abre sin errores y trae la portada, el índice y una hoja por servicio.
        var excel = archivos.Single(a => a.GetProperty("formato").GetString() == "excel").GetProperty("ruta").GetString()!;
        var urlExcel = new Uri((await JsonAsync(await navegador.GetAsync(
            $"/api/v1/contratos/exportaciones/{id}/descarga?archivo={Uri.EscapeDataString(excel)}"))).GetProperty("url").GetString()!);
        var libro = await s3.GetByteArrayAsync(urlExcel, Ct);
        using (var documento = SpreadsheetDocument.Open(new MemoryStream(libro), isEditable: false))
        {
            var hojas = documento.WorkbookPart!.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(h => h.Name!.Value!).ToList();
            Assert.Contains("Portada", hojas);
            Assert.Contains("Índice", hojas);
            Assert.True(hojas.Count > 4, string.Join(", ", hojas));
        }

        // Pasado el tiempo de la firma, el S3 la rechaza.
        await Task.Delay(TimeSpan.FromSeconds(5), Ct);
        using (var vencida = await s3.GetAsync(url, Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, vencida.StatusCode);
        }

        // Auditoría (sin contenido del spec) y aviso a quien la pidió.
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var eventos = await plataforma.Auditoria.AsNoTracking().Where(a => a.RecursoId == id).OrderBy(a => a.OcurridoEn).ToListAsync(Ct);
        Assert.Equal(ServicioExportaciones.AccionSolicitada, eventos[0].Accion);
        Assert.Equal(2, eventos.Count(a => a.Accion == ServicioExportaciones.AccionDescargada));
        Assert.All(eventos, a => Assert.DoesNotContain("openapi: 3", a.Datos ?? string.Empty, StringComparison.Ordinal));
        Assert.True(await plataforma.Notificaciones.AnyAsync(n => n.PersonaId == persona && n.Tipo == ManejadorResultadosExportacion.Notificacion.Codigo
            && n.Titulo == "Tu exportación está lista", Ct));
    }
}
