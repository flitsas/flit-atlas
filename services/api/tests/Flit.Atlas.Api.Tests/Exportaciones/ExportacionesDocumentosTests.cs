using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Flit.Atlas.Modulos.Documentacion;
using Flit.Atlas.Modulos.Documentacion.Datos;
using Flit.Atlas.Modulos.Documentacion.Exportaciones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Flit.Atlas.Api.Tests.Exportaciones.HttpExportaciones;

namespace Flit.Atlas.Api.Tests.Exportaciones;

/// <summary>Lo que los documentos usan en las pruebas: un aplicativo y un documento publicado por el líder técnico.</summary>
internal static class DocumentosDePrueba
{
    public const string Raiz = "/api/v1/documentacion/documentos";

    public static async Task<Guid> AplicativoAsync(Navegador lider, string? slug = null)
    {
        var creado = await PostAsync(lider, "/api/v1/catalogo/aplicativos", new
        {
            slug = slug ?? $"docs-demo-{Guid.NewGuid().ToString("N")[..10]}", nombre = "Docs Demo", tipo = "producto", grupo = "satelite", estado = "activo",
            criticidad = "media", manejaDatosPersonales = false,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        return (await JsonAsync(creado)).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> PublicadoAsync(Navegador lider, Guid aplicativo, string titulo = "Radicar un trámite", string contenido = "## Pasos\n\nEntra.",
        string visibilidad = "interno_negocio")
    {
        var creado = await PostAsync(lider, Raiz, new
        {
            aplicativoId = aplicativo, tipo = "guia_funcional", titulo, contenido, visibilidad, audiencias = new[] { "empleados" }, responsable = "Persona Demo", nota = "Primera.",
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await JsonAsync(creado)).GetProperty("documento").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(lider, $"{Raiz}/{id}/envio-revision", new { revision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(lider, $"{Raiz}/{id}/publicacion", new { revision = 1 })).StatusCode);
        return id;
    }
}

/// <summary>
/// HU 5.6 (ATLAS-ADR-0053 §7) con atlas-api completo y un S3 real; atlas-docs se reemplaza por sus mensajes: la visibilidad al
/// exportar, la entrada con el MDX convertido y los componentes vivos según los permisos de quien exporta, los resultados de
/// la cola, la descarga y sus topes. El generador lo prueban los <c>pytest</c> de <c>services/docs</c>.
/// </summary>
public sealed class ExportacionesDocumentosTests(EntornoApi entorno, ContenedoresExportaciones contenedores) : IClassFixture<ContenedoresExportaciones>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Ruta = "/api/v1/documentacion/exportaciones";

    private WebApplicationFactory<Program> Api(TimeProvider? reloj = null) =>
        entorno.Api(configuracion: contenedores.Configuracion(conBroker: false), reloj: reloj);

    private async Task<JsonElement> EntradaAsync(Guid id)
    {
        using var s3 = new AmazonS3Client(new BasicAWSCredentials(ContenedoresExportaciones.Usuario, contenedores.Clave),
            new AmazonS3Config { ServiceURL = contenedores.EndpointS3, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        using var objeto = await s3.GetObjectAsync(ContenedoresExportaciones.Bucket, $"exportaciones/{id}/entrada.json", Ct);
        using var lector = new StreamReader(objeto.ResponseStream);
        return JsonDocument.Parse(await lector.ReadToEndAsync(Ct)).RootElement.Clone();
    }

    private static object Pedido(params Guid[] documentos) =>
        new { documentos = documentos.Select(d => new { documentoId = d }).ToArray(), formatos = new[] { "pdf", "markdown" } };

    private static SobreEvento Sobre(string tipo, object datos) => new(
        Guid.CreateVersion7(), tipo, 1, DateTimeOffset.UtcNow, SobreEvento.SinOrganizacion, "atlas-docs", string.Empty,
        JsonSerializer.SerializeToElement(datos, SobreEvento.OpcionesJson));

    private static async Task ManejarAsync(WebApplicationFactory<Program> api, SobreEvento sobre)
    {
        await using var alcance = api.Services.CreateAsyncScope();
        var manejador = api.Services.GetServices<IManejadorEventos>().OfType<ManejadorResultadosDocumentos>().Single();
        await manejador.ManejarAsync(sobre, alcance.ServiceProvider, Ct);
    }

    [Fact]
    public async Task Solicitar_deja_la_entrada_con_el_MDX_convertido_su_evento_y_su_auditoria()
    {
        await using var api = Api();
        var (lider, personaLider) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var slug = $"docs-demo-{Guid.NewGuid().ToString("N")[..10]}";
        var aplicativo = await DocumentosDePrueba.AplicativoAsync(lider, slug);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, aplicativo,
            contenido: $"Texto.\n\n<Alerta tipo=\"aviso\">\n\nOjo.\n\n</Alerta>\n\n<Responsables aplicativo=\"{slug}\"/>\n\n```mdx\n<SoporteDeTurno/>\n```\n");

        var respuesta = await PostAsync(lider, Ruta, Pedido(documento));

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        var cuerpo = await JsonAsync(respuesta);
        var id = cuerpo.GetProperty("id").GetGuid();
        Assert.Equal($"{Ruta}/{id}", respuesta.Headers.Location?.ToString());
        Assert.Equal("pendiente", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(new[] { "pdf", "markdown", "zip" }, cuerpo.GetProperty("formatos").EnumerateArray().Select(f => f.GetString()));

        var entrada = await EntradaAsync(id);
        Assert.Equal("FLIT S.A.S. · uso interno", entrada.GetProperty("metadatos").GetProperty("organizacion").GetString());
        Assert.Equal("Radicar un trámite", entrada.GetProperty("metadatos").GetProperty("nombre").GetString());
        var exportado = entrada.GetProperty("documentos")[0];
        Assert.Equal("Guía funcional", exportado.GetProperty("tipo").GetString());
        var markdown = exportado.GetProperty("markdown").GetString()!;
        Assert.Contains("**Atención**", markdown, StringComparison.Ordinal);
        Assert.Contains("**Responsables de Docs Demo:**", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<Alerta", markdown, StringComparison.Ordinal);
        Assert.Contains("<SoporteDeTurno/>", markdown, StringComparison.Ordinal);

        await using var plataforma = entorno.Plataforma();
        var auditoria = await plataforma.Auditoria.AsNoTracking().SingleAsync(a => a.Accion == EndpointsExportacionesDocumentos.AccionSolicitada && a.RecursoId == id.ToString(), Ct);
        Assert.Equal(personaLider.ToString(), auditoria.ActorId);
        Assert.DoesNotContain("Radicar", auditoria.Datos, StringComparison.Ordinal);
        var evento = await plataforma.Outbox.AsNoTracking().Where(o => o.Tipo == EventosDocumentacion.ExportacionSolicitada.Nombre).ToListAsync(Ct);
        Assert.Contains(evento, e => e.Sobre.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lo_que_la_persona_no_puede_leer_no_se_exporta_aunque_tenga_el_permiso()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var aplicativo = await DocumentosDePrueba.AplicativoAsync(lider);
        var visible = await DocumentosDePrueba.PublicadoAsync(lider, aplicativo);
        var tecnico = await DocumentosDePrueba.PublicadoAsync(lider, aplicativo, titulo: "Runbook técnico", visibilidad: "interno_ti");
        // Negocio tiene atlas.docs.exportar, pero no ve lo interno de TI.
        var (negocio, _) = await EmpleadoAsync(entorno, api, RolesAtlas.Negocio);

        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(negocio, Ruta, Pedido(visible))).StatusCode);
        var solo = await PostAsync(negocio, Ruta, Pedido(tecnico));
        Assert.Equal(HttpStatusCode.NotFound, solo.StatusCode);
        Assert.Equal("no_encontrado", await CodigoAsync(solo));
        // Basta uno que no vea para que no salga ninguno, y sin decir cuál.
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(negocio, Ruta, Pedido(visible, tecnico))).StatusCode);
    }

    [Fact]
    public async Task Sin_el_permiso_de_un_componente_vivo_el_archivo_no_lo_trae()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var slug = $"docs-demo-{Guid.NewGuid().ToString("N")[..10]}";
        var aplicativo = await DocumentosDePrueba.AplicativoAsync(lider, slug);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, aplicativo, contenido: "<Contrato id=\"api-oculta\"/>\n");
        var (negocio, _) = await EmpleadoAsync(entorno, api, RolesAtlas.Negocio);

        var respuesta = await PostAsync(negocio, Ruta, Pedido(documento));

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        var entrada = await EntradaAsync((await JsonAsync(respuesta)).GetProperty("id").GetGuid());
        Assert.Contains(ValoresVivos.SinAcceso, entrada.GetProperty("documentos")[0].GetProperty("markdown").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_un_ingreso_reciente_pide_reautenticarse_y_sin_el_permiso_responde_403()
    {
        var reloj = new RelojDePrueba();
        await using var api = Api(reloj);
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider));
        var (lector, _) = await EmpleadoAsync(entorno, api, RolesAtlas.Lector);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(lector, Ruta, Pedido(documento))).StatusCode);

        reloj.Adelantar(TimeSpan.FromMinutes(11));
        var respuesta = await PostAsync(lider, Ruta, Pedido(documento));
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("reautenticacion_requerida", await CodigoAsync(respuesta));
    }

    [Theory]
    [InlineData("{\"documentos\":[],\"formatos\":[\"pdf\"]}", "documentos")]
    [InlineData("{\"documentos\":[{\"documentoId\":\"0199f0a0-7c3e-7b1a-9d4e-2f6a8b0c1d50\"}],\"formatos\":[\"postman\"]}", "formatos")]
    [InlineData("{\"documentos\":[{\"documentoId\":\"0199f0a0-7c3e-7b1a-9d4e-2f6a8b0c1d50\"},{\"documentoId\":\"0199f0a0-7c3e-7b1a-9d4e-2f6a8b0c1d50\"}],\"formatos\":[\"pdf\"]}", "documentos")]
    public async Task Una_solicitud_mal_formada_se_rechaza(string cuerpo, string campo)
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);

        using var contenido = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json");
        var respuesta = await lider.EnviarAsync(HttpMethod.Post, Ruta, contenido);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.True((await JsonAsync(respuesta)).GetProperty("errors").TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task Con_dos_en_curso_la_tercera_responde_429_y_el_resultado_de_la_cola_las_libera()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider));

        var primera = (await JsonAsync(await PostAsync(lider, Ruta, Pedido(documento)))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(lider, Ruta, Pedido(documento))).StatusCode);
        var tercera = await PostAsync(lider, Ruta, Pedido(documento));
        Assert.Equal(HttpStatusCode.TooManyRequests, tercera.StatusCode);
        Assert.NotNull(tercera.Headers.RetryAfter);

        await ManejarAsync(api, Sobre(EventosDocumentacion.ExportacionFallida.Nombre, new { exportacionId = primera, codigo = "interno", mensaje = "No se pudo." }));

        var fallida = await JsonAsync(await lider.GetAsync($"{Ruta}/{primera}"));
        Assert.Equal("fallida", fallida.GetProperty("estado").GetString());
        Assert.Equal("interno", fallida.GetProperty("error").GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(lider, Ruta, Pedido(documento))).StatusCode);
    }

    [Fact]
    public async Task Al_quedar_lista_solo_quien_la_pidio_la_ve_y_la_descarga_con_una_URL_firmada()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider));
        var id = (await JsonAsync(await PostAsync(lider, Ruta, Pedido(documento)))).GetProperty("id").GetGuid();

        Assert.Equal("no_lista", await CodigoAsync(await lider.GetAsync($"{Ruta}/{id}/descarga")));

        var prefijo = $"exportaciones/{id}/";
        await ManejarAsync(api, Sobre(EventosDocumentacion.ExportacionIniciada.Nombre, new { exportacionId = id }));
        Assert.Equal("procesando", (await JsonAsync(await lider.GetAsync($"{Ruta}/{id}"))).GetProperty("estado").GetString());
        var lista = Sobre(EventosDocumentacion.ExportacionLista.Nombre, new
        {
            exportacionId = id,
            hashContenido = new string('c', 64),
            archivos = new[]
            {
                new { clave = prefijo + "demo.md", ruta = "demo.md", formato = "markdown", bytes = 17L, sha256 = new string('d', 64), tipo = "text/markdown" },
                new { clave = prefijo + "demo.zip", ruta = "demo.zip", formato = "zip", bytes = 11L, sha256 = new string('e', 64), tipo = "application/zip" },
            },
        });
        await ManejarAsync(api, lista);

        var detalle = await JsonAsync(await lider.GetAsync($"{Ruta}/{id}"));
        Assert.Equal("lista", detalle.GetProperty("estado").GetString());
        Assert.Equal(new string('c', 64), detalle.GetProperty("hashContenido").GetString());
        Assert.Equal(2, detalle.GetProperty("archivos").GetArrayLength());

        var descarga = await JsonAsync(await lider.GetAsync($"{Ruta}/{id}/descarga?archivo=demo.md"));
        Assert.Equal("demo.md", descarga.GetProperty("nombre").GetString());
        Assert.Contains("X-Amz-Signature", descarga.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await lider.GetAsync($"{Ruta}/{id}/descarga?archivo=otro.md")).StatusCode);

        var (otro, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync($"{Ruta}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync($"{Ruta}/{id}/descarga")).StatusCode);

        await using var plataforma = entorno.Plataforma();
        Assert.True(await plataforma.Auditoria.AsNoTracking().AnyAsync(a => a.Accion == EndpointsExportacionesDocumentos.AccionDescargada && a.RecursoId == id.ToString(), Ct));

        // La cola reentrega: un resultado para una exportación ya terminada se ignora.
        await ManejarAsync(api, lista);
        await ManejarAsync(api, Sobre(EventosDocumentacion.ExportacionFallida.Nombre, new { exportacionId = id, codigo = "interno", mensaje = "Tarde." }));
        Assert.Equal("lista", (await JsonAsync(await lider.GetAsync($"{Ruta}/{id}"))).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Un_resultado_con_claves_ajenas_o_sin_ZIP_queda_fallido()
    {
        await using var api = Api();
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider));
        var id = (await JsonAsync(await PostAsync(lider, Ruta, Pedido(documento)))).GetProperty("id").GetGuid();

        await ManejarAsync(api, Sobre(EventosDocumentacion.ExportacionLista.Nombre, new
        {
            exportacionId = id,
            hashContenido = new string('c', 64),
            archivos = new[] { new { clave = "otra/clave.zip", ruta = "clave.zip", formato = "zip", bytes = 1L, sha256 = new string('e', 64), tipo = "application/zip" } },
        }));

        Assert.Equal("fallida", (await JsonAsync(await lider.GetAsync($"{Ruta}/{id}"))).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task La_retencion_vence_las_terminadas_y_corta_las_que_no_terminan()
    {
        var reloj = new RelojDePrueba();
        await using var api = Api(reloj);
        var (lider, _) = await EmpleadoAsync(entorno, api, RolesAtlas.LiderTecnico);
        var documento = await DocumentosDePrueba.PublicadoAsync(lider, await DocumentosDePrueba.AplicativoAsync(lider));
        var id = (await JsonAsync(await PostAsync(lider, Ruta, Pedido(documento)))).GetProperty("id").GetGuid();
        var vencedor = ActivatorUtilities.CreateInstance<VencedorExportacionesDocumentos>(api.Services);

        reloj.Adelantar(TimeSpan.FromHours(3));
        var (_, agotadas) = await vencedor.VencerAsync(Ct);
        Assert.True(agotadas >= 1);
        await using (var contexto = api.Services.CreateAsyncScope())
        {
            var bd = contexto.ServiceProvider.GetRequiredService<DocumentacionDbContext>();
            var fila = await bd.Exportaciones.AsNoTracking().SingleAsync(e => e.Id == id, Ct);
            Assert.Equal(EstadoExportacionDocumentos.Fallida, fila.Estado);
            Assert.Equal("tiempo_agotado", fila.ErrorCodigo);
        }

        reloj.Adelantar(TimeSpan.FromDays(2));
        await vencedor.VencerAsync(Ct);
        await using (var contexto = api.Services.CreateAsyncScope())
        {
            var bd = contexto.ServiceProvider.GetRequiredService<DocumentacionDbContext>();
            Assert.Equal(EstadoExportacionDocumentos.Vencida, (await bd.Exportaciones.AsNoTracking().SingleAsync(e => e.Id == id, Ct)).Estado);
        }
    }
}
