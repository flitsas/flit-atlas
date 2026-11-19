using System.Net;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Flit.Atlas.Modulos.Contratos;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Exportaciones;
using Flit.Atlas.Plataforma.Mensajeria;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Flit.Atlas.Api.Tests.Exportaciones.HttpExportaciones;

namespace Flit.Atlas.Api.Tests.Exportaciones;

/// <summary>
/// HU 4.5 (ATLAS-ADR-0042) con atlas-api completo y un S3 real (RustFS); atlas-docs se reemplaza aquí por sus mensajes
/// (el generador real lo usa <see cref="ExportacionesPuntaAPuntaTests"/>): validación, permisos, reautenticación, la entrada
/// podada en el S3 con su evento y su auditoría, el portal (niega por defecto; con la compartición, solo la organización
/// de la persona), los resultados de la cola, la URL firmada de 15 minutos, la retención y el corte por tiempo.
/// </summary>
public sealed class ExportacionesTests(EntornoApi entorno, ContenedoresExportaciones contenedores) : IClassFixture<ContenedoresExportaciones>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Ruta = "/api/v1/contratos/exportaciones";
    private const string RutaPortal = "/api/v1/portal/exportaciones";

    private static readonly string[] Todos = ["openapi", "postman", "html", "pdf", "word", "excel"];

    private WebApplicationFactory<Program> Api(TimeProvider? reloj = null, Action<IServiceCollection>? servicios = null) =>
        entorno.Api(configuracion: contenedores.Configuracion(conBroker: false), reloj: reloj, servicios: servicios);

    private AmazonS3Client S3() => new(new BasicAWSCredentials(ContenedoresExportaciones.Usuario, contenedores.Clave),
        new AmazonS3Config { ServiceURL = contenedores.EndpointS3, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

    private async Task<string> LeerS3Async(string clave)
    {
        using var s3 = S3();
        using var objeto = await s3.GetObjectAsync(ContenedoresExportaciones.Bucket, clave, Ct);
        using var lector = new StreamReader(objeto.ResponseStream);
        return await lector.ReadToEndAsync(Ct);
    }

    private ContratosDbContext Contratos() => ContratosDePrueba.Contexto(entorno.Postgres.GetConnectionString());

    private static ManejadorResultadosExportacion Manejador(WebApplicationFactory<Program> api) =>
        api.Services.GetServices<IManejadorEventos>().OfType<ManejadorResultadosExportacion>().Single();

    private static SobreEvento Sobre(string tipo, object datos) => new(
        Guid.CreateVersion7(), tipo, 1, DateTimeOffset.UtcNow, SobreEvento.SinOrganizacion, "atlas-docs", string.Empty,
        JsonSerializer.SerializeToElement(datos, SobreEvento.OpcionesJson));

    private static async Task ManejarAsync(WebApplicationFactory<Program> api, SobreEvento sobre)
    {
        await using var alcance = api.Services.CreateAsyncScope();
        await Manejador(api).ManejarAsync(sobre, alcance.ServiceProvider, Ct);
    }

    /// <summary>Simula lo que haría atlas-docs: sube dos archivos y publica <c>lista</c>.</summary>
    private async Task<SobreEvento> ListaAsync(WebApplicationFactory<Program> api, Guid id)
    {
        var prefijo = $"exportaciones/{id}/";
        using var s3 = S3();
        foreach (var (ruta, contenido) in new[] { ("demo-r1/demo-r1.pdf", "%PDF-1.7 ficticio"), ("demo-r1.zip", "PK ficticio") })
        {
            await s3.PutObjectAsync(new PutObjectRequest { BucketName = ContenedoresExportaciones.Bucket, Key = prefijo + ruta, ContentBody = contenido }, Ct);
        }
        var sobre = Sobre(EventosContratos.ExportacionLista.Nombre, new
        {
            exportacionId = id,
            hashContenido = new string('c', 64),
            archivos = new[]
            {
                new { clave = prefijo + "demo-r1/demo-r1.pdf", ruta = "demo-r1/demo-r1.pdf", formato = "pdf", bytes = 17L, sha256 = new string('d', 64), tipo = "application/pdf" },
                new { clave = prefijo + "demo-r1.zip", ruta = "demo-r1.zip", formato = "zip", bytes = 11L, sha256 = new string('e', 64), tipo = "application/zip" },
            },
        });
        await ManejarAsync(api, sobre);
        return sobre;
    }

    private async Task<(Navegador Navegador, Guid Persona, PaqueteSembrado Paquete, Guid Exportacion)> SolicitadaAsync(WebApplicationFactory<Program> api)
    {
        var paquete = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(), ct: Ct);
        var (navegador, persona) = await EmpleadoAsync(entorno, api, RolExportador);
        var respuesta = await PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = new[] { "pdf" } });
        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        return (navegador, persona, paquete, Guid.Parse((await JsonAsync(respuesta)).GetProperty("id").GetString()!));
    }

    // --- Solicitar ---

    [Fact]
    public async Task Solicitar_deja_la_entrada_podada_en_el_S3_con_su_evento_y_su_auditoria()
    {
        await using var api = Api();
        var paquete = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(), ct: Ct);
        var (navegador, persona) = await EmpleadoAsync(entorno, api, RolExportador);

        var respuesta = await PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = Todos });

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        var cuerpo = await JsonAsync(respuesta);
        var id = Guid.Parse(cuerpo.GetProperty("id").GetString()!);
        Assert.Equal($"{Ruta}/{id}", respuesta.Headers.Location?.ToString());
        Assert.Equal("pendiente", cuerpo.GetProperty("estado").GetString());
        Assert.Equal([.. Todos, "zip"], cuerpo.GetProperty("formatos").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("Integración ICT · revisión 1", cuerpo.GetProperty("nombre").GetString());

        // La entrada: el spec podado (sin servers internos ni operaciones fuera de la selección), la guía y el changelog.
        var entrada = JsonDocument.Parse(await LeerS3Async($"exportaciones/{id}/entrada.json")).RootElement;
        Assert.Equal(ModulosEntradaOrganizacion, entrada.GetProperty("metadatos").GetProperty("organizacion").GetString());
        Assert.Equal(1, entrada.GetProperty("metadatos").GetProperty("revision").GetInt32());
        Assert.StartsWith("Primera revisión del paquete: 1 contrato,",
            entrada.GetProperty("metadatos").GetProperty("changelog")[0].GetProperty("cambio").GetString(), StringComparison.Ordinal);
        var spec = Assert.Single(entrada.GetProperty("specs").EnumerateArray());
        Assert.Equal(paquete.Codigo, spec.GetProperty("contrato").GetString());
        Assert.DoesNotContain("x-flit-fuente", spec.GetProperty("contenido").GetString(), StringComparison.Ordinal);
        Assert.Equal("Primeros pasos", Assert.Single(entrada.GetProperty("guias").EnumerateArray()).GetProperty("titulo").GetString());

        await using var plataforma = entorno.Plataforma();
        var evento = await plataforma.Outbox.AsNoTracking().SingleAsync(m => m.Tipo == EventosContratos.ExportacionSolicitada.Nombre && m.Sobre.Contains(id.ToString()), Ct);
        var datos = SobreEvento.DesdeJson(System.Text.Encoding.UTF8.GetBytes(evento.Sobre)).Data;
        Assert.Equal($"exportaciones/{id}/entrada.json", datos.GetProperty("entradaClave").GetString());
        Assert.Equal("paquete", datos.GetProperty("origen").GetProperty("tipo").GetString());
        var auditoria = await plataforma.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoId == id.ToString(), Ct);
        Assert.Equal(ServicioExportaciones.AccionSolicitada, auditoria.Accion);
        Assert.Equal(persona.ToString(), auditoria.ActorId);
        Assert.DoesNotContain("openapi:", auditoria.Datos!, StringComparison.Ordinal);
        await using var contratos = Contratos();
        var fila = await contratos.Exportaciones.AsNoTracking().SingleAsync(e => e.Id == id, Ct);
        Assert.Null(fila.OrganizacionId);
        Assert.Matches("^[0-9a-f]{64}$", fila.EntradaSha256);
    }

    private const string ModulosEntradaOrganizacion = "FLIT S.A.S. · uso interno";

    [Fact]
    public async Task Solicitar_valida_el_cuerpo()
    {
        await using var api = Api();
        var (navegador, _) = await EmpleadoAsync(entorno, api, RolExportador);

        var sinOrigen = await PostAsync(navegador, Ruta, new { formatos = Todos });
        var ambos = await PostAsync(navegador, Ruta, new { paqueteId = Guid.NewGuid(), versionId = Guid.NewGuid(), formatos = Todos });
        var formato = await PostAsync(navegador, Ruta, new { paqueteId = Guid.NewGuid(), formatos = new[] { "docx" } });
        var vacio = await PostAsync(navegador, Ruta, new { paqueteId = Guid.NewGuid(), formatos = Array.Empty<string>() });
        var noExiste = await PostAsync(navegador, Ruta, new { paqueteId = Guid.NewGuid(), formatos = Todos });

        Assert.Equal(HttpStatusCode.BadRequest, sinOrigen.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ambos.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, formato.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
    }

    [Theory]
    [InlineData(RolesAtlas.Soporte)]
    [InlineData(RolesAtlas.Negocio)]
    public async Task Sin_los_permisos_no_se_exporta(string rol)
    {
        await using var api = Api();
        var paquete = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(), ct: Ct);
        var (navegador, _) = await EmpleadoAsync(entorno, api, rol);

        var respuesta = await PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = Todos });

        // Soporte no tiene atlas.exportaciones.solicitar; negocio sí, pero no ve contratos internos.
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Exportar_exige_un_ingreso_reciente()
    {
        var reloj = new RelojDePrueba();
        await using var api = Api(reloj);
        var paquete = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(), ct: Ct);
        var (navegador, _) = await EmpleadoAsync(entorno, api, RolExportador);
        reloj.Adelantar(TimeSpan.FromMinutes(11));

        var respuesta = await PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = Todos });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("reautenticacion_requerida", await CodigoAsync(respuesta));
    }

    [Fact]
    public async Task Una_version_proto_no_se_exporta_y_una_openapi_va_completa()
    {
        await using var api = Api();
        var proto = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(),
            "syntax = \"proto3\";\npackage demo.v1;\nservice Demo { rpc Get (Vacio) returns (Vacio); }\nmessage Vacio {}\n", TipoContrato.Proto, Ct);
        var ict = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(), ct: Ct);
        var (navegador, _) = await EmpleadoAsync(entorno, api, RolExportador);

        var conProto = await PostAsync(navegador, Ruta, new { versionId = proto.VersionId, formatos = Todos });
        var completa = await PostAsync(navegador, Ruta, new { versionId = ict.VersionId, formatos = new[] { "openapi" } });

        Assert.Equal(HttpStatusCode.Conflict, conProto.StatusCode);
        Assert.Equal("no_exportable", await CodigoAsync(conProto));
        Assert.Equal(HttpStatusCode.Accepted, completa.StatusCode);
        var id = (await JsonAsync(completa)).GetProperty("id").GetString();
        var entrada = JsonDocument.Parse(await LeerS3Async($"exportaciones/{id}/entrada.json")).RootElement;
        // Completa: el spec tal como se publicó (sin podar).
        Assert.Equal(await File.ReadAllTextAsync(ContratosDePrueba.RutaIct, Ct), entrada.GetProperty("specs")[0].GetProperty("contenido").GetString());
    }

    // --- Resultados de atlas-docs ---

    [Fact]
    public async Task El_resultado_de_atlas_docs_deja_la_exportacion_lista_y_avisa_una_sola_vez()
    {
        await using var api = Api();
        var (navegador, persona, _, id) = await SolicitadaAsync(api);

        await ManejarAsync(api, Sobre(EventosContratos.ExportacionIniciada.Nombre, new { exportacionId = id }));
        Assert.Equal("procesando", (await JsonAsync(await navegador.GetAsync($"{Ruta}/{id}"))).GetProperty("estado").GetString());
        var lista = await ListaAsync(api, id);
        await ManejarAsync(api, lista);

        var actual = await JsonAsync(await navegador.GetAsync($"{Ruta}/{id}"));
        Assert.Equal("lista", actual.GetProperty("estado").GetString());
        Assert.Equal(["demo-r1/demo-r1.pdf", "demo-r1.zip"], actual.GetProperty("archivos").EnumerateArray().Select(a => a.GetProperty("ruta").GetString()));
        Assert.Equal("demo-r1.pdf", actual.GetProperty("archivos")[0].GetProperty("nombre").GetString());
        await using var plataforma = entorno.Plataforma();
        Assert.Equal(1, await plataforma.Notificaciones.CountAsync(n => n.PersonaId == persona && n.Titulo == "Tu exportación está lista", Ct));
        var notificacion = await plataforma.Notificaciones.SingleAsync(n => n.PersonaId == persona, Ct);
        Assert.Contains($"exportacion={id}", notificacion.Enlace, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_resultado_con_claves_ajenas_o_una_falla_quedan_fallidas_auditadas_y_avisadas()
    {
        await using var api = Api();
        var (navegador, persona, _, ajena) = await SolicitadaAsync(api);
        var (_, _, _, fallida) = await SolicitadaAsync(api);

        await ManejarAsync(api, Sobre(EventosContratos.ExportacionLista.Nombre, new
        {
            exportacionId = ajena,
            hashContenido = new string('c', 64),
            archivos = new[] { new { clave = $"exportaciones/{Guid.NewGuid()}/otra.zip", ruta = "otra.zip", formato = "zip", bytes = 1L, sha256 = new string('d', 64), tipo = "application/zip" } },
        }));
        await ManejarAsync(api, Sobre(EventosContratos.ExportacionFallida.Nombre, new { exportacionId = fallida, codigo = "spec_invalido", mensaje = "El contrato no es OpenAPI ni AsyncAPI." }));

        var primera = await JsonAsync(await navegador.GetAsync($"{Ruta}/{ajena}"));
        Assert.Equal("fallida", primera.GetProperty("estado").GetString());
        Assert.Equal("interno", primera.GetProperty("error").GetProperty("codigo").GetString());
        await using var plataforma = entorno.Plataforma();
        var auditoria = await plataforma.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoId == fallida.ToString() && a.Accion == ServicioExportaciones.AccionFallida, Ct);
        Assert.Contains("spec_invalido", auditoria.Datos!, StringComparison.Ordinal);
        Assert.True(await plataforma.Notificaciones.AnyAsync(n => n.PersonaId == persona && n.Titulo == "Tu exportación falló", Ct));
    }

    // --- Descarga ---

    [Fact]
    public async Task La_descarga_es_una_URL_firmada_de_15_minutos_auditada_y_solo_para_quien_la_pidio()
    {
        await using var api = Api();
        var (navegador, _, _, id) = await SolicitadaAsync(api);
        var antes = await navegador.GetAsync($"{Ruta}/{id}/descarga");
        await ListaAsync(api, id);
        var (otro, _) = await EmpleadoAsync(entorno, api, RolExportador);

        var descarga = await JsonAsync(await navegador.GetAsync($"{Ruta}/{id}/descarga"));
        var pdf = await navegador.GetAsync($"{Ruta}/{id}/descarga?archivo={Uri.EscapeDataString("demo-r1/demo-r1.pdf")}");
        var ajeno = await otro.GetAsync($"{Ruta}/{id}/descarga");

        Assert.Equal(HttpStatusCode.Conflict, antes.StatusCode);
        Assert.Equal("no_lista", await CodigoAsync(antes));
        var url = new Uri(descarga.GetProperty("url").GetString()!);
        var expira = int.Parse(System.Web.HttpUtility.ParseQueryString(url.Query)["X-Amz-Expires"]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expira, 895, 900);
        Assert.Equal("demo-r1.zip", descarga.GetProperty("nombre").GetString());
        using (var http = new HttpClient())
        {
            Assert.Equal("PK ficticio", await http.GetStringAsync(url, Ct));
        }
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        await using var plataforma = entorno.Plataforma();
        Assert.Equal(2, await plataforma.Auditoria.CountAsync(a => a.RecursoId == id.ToString() && a.Accion == ServicioExportaciones.AccionDescargada, Ct));
    }

    // --- Retención ---

    [Fact]
    public async Task La_retencion_borra_los_archivos_y_la_deja_vencida_y_una_atascada_falla_por_tiempo()
    {
        await using var api = Api();
        var (navegador, _, _, id) = await SolicitadaAsync(api);
        var (otro, _, _, atascada) = await SolicitadaAsync(api);
        await ListaAsync(api, id);
        await using (var contratos = Contratos())
        {
            await contratos.Exportaciones.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.VenceEn, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
            await contratos.Exportaciones.Where(e => e.Id == atascada).ExecuteUpdateAsync(s => s.SetProperty(e => e.SolicitadaEn, DateTimeOffset.UtcNow.AddHours(-3)), Ct);
        }
        var vencedor = api.Services.GetServices<IHostedService>().OfType<VencedorExportaciones>().Single();

        // El trabajo también corre solo en las otras instancias de prueba (misma base): se miran los estados, no los conteos.
        await vencedor.VencerAsync(Ct);

        Assert.Equal("vencida", (await JsonAsync(await navegador.GetAsync($"{Ruta}/{id}"))).GetProperty("estado").GetString());
        var descarga = await navegador.GetAsync($"{Ruta}/{id}/descarga");
        Assert.Equal(HttpStatusCode.Gone, descarga.StatusCode);
        using var s3 = S3();
        var quedan = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = ContenedoresExportaciones.Bucket, Prefix = $"exportaciones/{id}/" }, Ct);
        Assert.Empty(quedan.S3Objects ?? []);
        var corte = await JsonAsync(await otro.GetAsync($"{Ruta}/{atascada}"));
        Assert.Equal("tiempo_agotado", corte.GetProperty("error").GetProperty("codigo").GetString());
    }

    // --- Límites (HU 4.10, ATLAS-ADR-0048) ---

    private static async Task AssertEnCursoAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Equal("30", Assert.Single(respuesta.Headers.GetValues("Retry-After")));
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var cuerpo = await JsonAsync(respuesta);
        Assert.Equal(EndpointsExportaciones.CodigoEnCurso, cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("exportaciones en curso", cuerpo.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_persona_no_pasa_del_maximo_de_exportaciones_en_curso_y_al_terminar_una_puede_pedir_otra()
    {
        await using var api = entorno.Api(configuracion: contenedores.Configuracion(conBroker: false, new()
        {
            [$"{OpcionesExportaciones.Seccion}:MaximoEnCursoPorPersona"] = "1",
        }));
        var paquete = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(), ct: Ct);
        var (navegador, _) = await EmpleadoAsync(entorno, api, RolExportador);
        var (otra, _) = await EmpleadoAsync(entorno, api, RolExportador);

        var primera = await PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = new[] { "openapi" } });
        var segunda = await PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = new[] { "openapi" } });
        var deOtra = await PostAsync(otra, Ruta, new { paqueteId = paquete.PaqueteId, formatos = new[] { "openapi" } });

        Assert.Equal(HttpStatusCode.Accepted, primera.StatusCode);
        await AssertEnCursoAsync(segunda);
        Assert.Equal(HttpStatusCode.Accepted, deOtra.StatusCode);

        // Al quedar fallida la primera (como si atlas-docs la terminara), hay cupo otra vez.
        var id = Guid.Parse((await JsonAsync(primera)).GetProperty("id").GetString()!);
        await ManejarAsync(api, Sobre(EventosContratos.ExportacionFallida.Nombre, new { exportacionId = id, codigo = "interno", mensaje = "Falla de prueba." }));
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = new[] { "openapi" } })).StatusCode);
    }

    [Fact]
    public async Task Las_solicitudes_simultaneas_no_pasan_del_maximo_en_curso()
    {
        await using var api = entorno.Api(configuracion: contenedores.Configuracion(conBroker: false, new()
        {
            [$"{OpcionesExportaciones.Seccion}:MaximoEnCursoPorPersona"] = "2",
            [$"{Identidad.Seguridad.OpcionesLimiteTasa.Seccion}:ExportacionesPorPersona"] = "100",
        }));
        var paquete = await ContratosDePrueba.SembrarAsync(entorno.Postgres.GetConnectionString(), ct: Ct);
        var (navegador, persona) = await EmpleadoAsync(entorno, api, RolExportador);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PostAsync(navegador, Ruta, new { paqueteId = paquete.PaqueteId, formatos = new[] { "openapi" } })));

        Assert.Equal(2, respuestas.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(4, respuestas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        await using var contratos = Contratos();
        Assert.Equal(2, await contratos.Exportaciones.CountAsync(e => e.SolicitadaPor == persona, Ct));
    }

    [Fact]
    public async Task Solicitar_exportaciones_tiene_limite_de_tasa_por_persona()
    {
        await using var api = entorno.Api(configuracion: contenedores.Configuracion(conBroker: false, new()
        {
            [$"{Identidad.Seguridad.OpcionesLimiteTasa.Seccion}:ExportacionesPorPersona"] = "2",
        }));
        var (navegador, _) = await EmpleadoAsync(entorno, api, RolExportador);

        // Aun las que no pasan la validación cuentan: el límite va antes del endpoint.
        var respuestas = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            respuestas.Add((await PostAsync(navegador, Ruta, new { formatos = Todos })).StatusCode);
        }
        var listar = await navegador.GetAsync(Ruta);

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], respuestas);
        Assert.Equal(HttpStatusCode.OK, listar.StatusCode);
    }

    [Fact]
    public async Task Una_organizacion_no_pasa_del_maximo_de_exportaciones_en_curso_ni_de_su_limite_de_solicitudes()
    {
        var (escenario, paqueteA, paqueteB, orgA, orgB) = await PortalAsync(exportar: true, new()
        {
            [$"{OpcionesExportaciones.Seccion}:MaximoEnCursoPorOrganizacion"] = "1",
            [$"{Identidad.Seguridad.OpcionesLimiteTasa.Seccion}:ExportacionesPorOrganizacion"] = "3",
        });
        await using var _ = escenario;
        var a1 = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador, extras: ["exportar"]);
        var a2 = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador, extras: ["exportar"]);
        var b = await escenario.ExternoAsync(orgB, RolesAtlas.Integrador, extras: ["exportar"]);

        var primera = await PostAsync(a1.Navegador, RutaPortal, new { paqueteId = paqueteA, formatos = new[] { "openapi" } });
        var deColega = await PostAsync(a2.Navegador, RutaPortal, new { paqueteId = paqueteA, formatos = new[] { "openapi" } });
        var deOtraOrganizacion = await PostAsync(b.Navegador, RutaPortal, new { paqueteId = paqueteB, formatos = new[] { "openapi" } });
        // La tercera solicitud de la organización A en la ventana: la corta el límite de tasa, no el tope en curso.
        var tercera = await PostAsync(a2.Navegador, RutaPortal, new { paqueteId = paqueteA, formatos = new[] { "openapi" } });
        var cuarta = await PostAsync(a1.Navegador, RutaPortal, new { paqueteId = paqueteA, formatos = new[] { "openapi" } });

        Assert.Equal(HttpStatusCode.Accepted, primera.StatusCode);
        await AssertEnCursoAsync(deColega);
        Assert.Equal(HttpStatusCode.Accepted, deOtraOrganizacion.StatusCode);
        await AssertEnCursoAsync(tercera);
        Assert.Equal(HttpStatusCode.TooManyRequests, cuarta.StatusCode);
        Assert.Equal("limite_de_tasa", await CodigoAsync(cuarta));
        Assert.True(cuarta.Headers.Contains("Retry-After"));
    }

    // --- Portal (con las comparticiones reales de la HU 4.3 y el acceso del portal de la HU 4.4) ---

    /// <summary>Un paquete publicado y compartido con la organización A (con o sin exportar) y otro solo con B.</summary>
    private async Task<(EscenarioPortal Escenario, Guid PaqueteA, Guid PaqueteB, Guid OrgA, Guid OrgB)> PortalAsync(
        bool exportar, Dictionary<string, string>? configuracion = null)
    {
        var escenario = new EscenarioPortal(entorno, contenedores.Configuracion(conBroker: false, configuracion));
        var (lider, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);
        var (_, slug) = await EscenarioPortal.AplicativoAsync(lider, "API Demo Exportaciones");
        var (contrato, version) = await escenario.PublicarAsync(slug, "api-exporta", EscenarioPortal.Spec("API Exporta Demo", "exporta", "Exporta"));
        var paqueteA = await EscenarioPortal.PaquetePublicadoAsync(lider, "Paquete Exporta Demo A", contrato, version, ["GET /exporta/{id}"]);
        var paqueteB = await EscenarioPortal.PaquetePublicadoAsync(lider, "Paquete Exporta Demo B", contrato, version, ["GET /exporta/{id}"]);
        var orgA = await escenario.OrganizacionAsync("Organismo Demo A");
        var orgB = await escenario.OrganizacionAsync("Organismo Demo B");
        await EscenarioPortal.CompartirAsync(lider, paqueteA, orgA, exportar: exportar);
        await EscenarioPortal.CompartirAsync(lider, paqueteB, orgB, exportar: true);
        return (escenario, paqueteA, paqueteB, orgA, orgB);
    }

    [Fact]
    public async Task El_portal_no_exporta_sin_puede_exportar_ni_un_paquete_ajeno_y_un_externo_no_usa_la_exportacion_interna()
    {
        var (escenario, paqueteA, paqueteB, orgA, _) = await PortalAsync(exportar: false);
        await using var _escenario = escenario;
        var a = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador, extras: ["exportar"]);

        var sinPermiso = await PostAsync(a.Navegador, RutaPortal, new { paqueteId = paqueteA, formatos = Todos });
        var ajeno = await PostAsync(a.Navegador, RutaPortal, new { paqueteId = paqueteB, formatos = Todos });
        var interna = await PostAsync(a.Navegador, Ruta, new { paqueteId = paqueteA, formatos = Todos });

        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        Assert.Equal("no_permitido_por_comparticion", await CodigoAsync(sinPermiso));
        // El paquete de otra organización no existe para A.
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, interna.StatusCode);
    }

    [Fact]
    public async Task Con_la_comparticion_el_externo_exporta_con_su_marca_y_nadie_de_otra_organizacion_la_ve_ni_la_descarga()
    {
        var (escenario, paqueteA, _, orgA, orgB) = await PortalAsync(exportar: true);
        await using var _escenario = escenario;
        var a = await escenario.ExternoAsync(orgA, RolesAtlas.Integrador, extras: ["exportar"]);
        var b = await escenario.ExternoAsync(orgB, RolesAtlas.Integrador, extras: ["exportar"]);
        var (empleado, _) = await escenario.EmpleadoAsync(RolesAtlas.LiderTecnico);

        var respuesta = await PostAsync(a.Navegador, RutaPortal, new { paqueteId = paqueteA, formatos = new[] { "pdf", "excel" } });

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        var id = Guid.Parse((await JsonAsync(respuesta)).GetProperty("id").GetString()!);
        await ListaAsync(escenario.Api, id);
        Assert.Equal(HttpStatusCode.OK, (await a.Navegador.GetAsync($"{RutaPortal}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Navegador.GetAsync($"{RutaPortal}/{id}/descarga")).StatusCode);
        // B (otra organización) ni la ve ni la descarga, y el 404 no revela que existe; tampoco el lado interno.
        Assert.Equal(HttpStatusCode.NotFound, (await b.Navegador.GetAsync($"{RutaPortal}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Navegador.GetAsync($"{RutaPortal}/{id}/descarga")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await empleado.GetAsync($"{Ruta}/{id}")).StatusCode);

        // La entrada lleva la organización en la marca de agua y el spec podado: sin la operación ni el servidor internos.
        var entrada = JsonDocument.Parse(await LeerS3Async($"exportaciones/{id}/entrada.json")).RootElement;
        await using var plataforma = entorno.Plataforma();
        var organizacion = await plataforma.Organizaciones.AsNoTracking().Where(o => o.Id == orgA).Select(o => o.Nombre).SingleAsync(Ct);
        Assert.Equal(organizacion, entrada.GetProperty("metadatos").GetProperty("organizacion").GetString());
        var spec = entrada.GetProperty("specs")[0].GetProperty("contenido").GetString()!;
        Assert.DoesNotContain("pagos-internos", spec, StringComparison.Ordinal);
        Assert.DoesNotContain("servidor-interno", spec, StringComparison.Ordinal);
        await using var propio = Contratos();
        Assert.Equal(orgA, (await propio.Exportaciones.AsNoTracking().SingleAsync(e => e.Id == id, Ct)).OrganizacionId);
    }
}
