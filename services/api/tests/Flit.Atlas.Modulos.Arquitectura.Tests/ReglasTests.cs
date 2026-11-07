using System.Net;
using System.Text.Json;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Modulos.Arquitectura.Drift;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Arquitectura.Tests;

/// <summary>Reglas puras de la HU 6.3 a 6.5 sobre la salida real de <c>likec4 export json</c> de un modelo ficticio.</summary>
public class ReglasTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

    private static string ModeloDemo() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "modelo-demo.json"));

    private static JsonElement Elemento(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Lee_la_salida_real_de_likec4_con_sus_vistas_por_categoria()
    {
        var (modelo, codigo, errores) = ReglasArquitectura.LeerModelo(Elemento(ModeloDemo()));

        Assert.True(modelo is not null, $"{codigo}: {string.Join("; ", errores)}");
        var vistas = modelo.Vistas.ToDictionary(v => v.Id, v => v.Categoria);
        Assert.Equal("landscape", vistas["landscape"]);
        Assert.Equal("contexto", vistas["demo_contexto"]);
        Assert.Equal("contenedores", vistas["demo_contenedores"]);
        Assert.Equal("componentes", vistas["demo_web_componentes"]);
        Assert.Equal("dinamica", vistas["flujo_radicacion"]);
        Assert.Equal("despliegue", vistas["despliegue_qa"]);

        var web = modelo.Elementos.Single(e => e.Id == "demo.web");
        Assert.Equal("demo-web", web.Metadato("k8s_workload"));
        Assert.Contains("critico", web.Etiquetas);
        Assert.Equal("Next.js", web.Tecnologia);
        Assert.Equal("https://ejemplo.com/demo", modelo.Elementos.Single(e => e.Id == "demo").Enlaces.Single().Url);
    }

    [Theory]
    [InlineData("[]", "modelo_invalido")]
    [InlineData("""{"_stage":"computed","elements":{},"views":{}}""", "modelo_invalido")]
    [InlineData("""{"_stage":"layouted","views":{}}""", "modelo_invalido")]
    [InlineData("""{"_stage":"layouted","elements":{},"views":{}}""", "modelo_invalido")]
    public void Rechaza_lo_que_no_es_un_modelo_con_layout(string json, string esperado)
    {
        var (modelo, codigo, _) = ReglasArquitectura.LeerModelo(Elemento(json));
        Assert.Null(modelo);
        Assert.Equal(esperado, codigo);
    }

    [Theory]
    [InlineData("el host es 192.0.2.40", "una dirección IP")]
    [InlineData("nodo fd00::1 interno", "una dirección IP")]
    [InlineData("password=hunter2", "un secreto")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----", "un secreto")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789", "un secreto")]
    public void Un_modelo_con_forma_de_ip_o_secreto_se_rechaza_sin_citar_el_valor(string texto, string forma)
    {
        var nodo = JsonSerializer.Serialize(new { _stage = "layouted", views = new { v = new { id = "v" } }, elements = new { a = new { title = "A", description = texto } } });

        var (modelo, codigo, errores) = ReglasArquitectura.LeerModelo(Elemento(nodo));

        Assert.Null(modelo);
        Assert.Equal("contenido_sensible", codigo);
        Assert.Contains(forma, errores[0], StringComparison.Ordinal);
        Assert.DoesNotContain(texto, string.Join(' ', errores), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LikeC4 1.59.4")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Puerto 4031 con token de acceso")]
    [InlineData("12:30:45")]
    public void Lo_que_solo_se_parece_a_una_ip_o_un_secreto_pasa(string texto) => Assert.Null(ReglasArquitectura.FormaSensible(texto));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://ejemplo.com")]
    [InlineData("https://usuario:clave@ejemplo.com")]
    [InlineData("data:text/html,hola")]
    public void Solo_se_aceptan_enlaces_https_sin_credenciales(string url)
    {
        Assert.False(ReglasArquitectura.EnlaceSeguro(url));
        var nodo = JsonSerializer.Serialize(new { _stage = "layouted", views = new { v = new { id = "v" } }, elements = new { a = new { title = "A", links = new[] { new { url } } } } });
        Assert.NotNull(ReglasArquitectura.LeerModelo(Elemento(nodo)).Codigo);
    }

    [Fact]
    public void Sin_ver_despliegue_el_modelo_pierde_las_vistas_y_los_nodos_de_despliegue()
    {
        var limpio = ReglasArquitectura.SinDespliegue(ModeloDemo());

        Assert.DoesNotContain("despliegue_qa", limpio, StringComparison.Ordinal);
        Assert.DoesNotContain("demo-qa", limpio, StringComparison.Ordinal);
        using var documento = JsonDocument.Parse(limpio);
        Assert.Empty(documento.RootElement.GetProperty("deployments").GetProperty("elements").EnumerateObject());
        Assert.True(documento.RootElement.GetProperty("views").TryGetProperty("demo_contenedores", out _));
    }

    [Fact]
    public void El_diff_de_modelos_ve_altas_bajas_y_cambios_de_definicion()
    {
        var antes = """{"elements":{"a":{"title":"A"},"b":{"title":"B"}},"relations":{},"views":{"v":{"hash":"1","nodes":[{"x":1}]},"w":{"hash":"2"}}}""";
        var despues = """{"elements":{"a":{"title":"A2"},"c":{"title":"C"}},"relations":{},"views":{"v":{"hash":"1","nodes":[{"x":9}]},"w":{"hash":"3"}}}""";

        var diff = ReglasArquitectura.DiffModelos(antes, despues, conDespliegue: true);

        Assert.Equal("c", Assert.Single(diff.Altas).Id);
        Assert.Equal("b", Assert.Single(diff.Bajas).Id);
        Assert.Equal(["a", "w"], diff.Cambios.Select(c => c.Id));
    }

    [Theory]
    [InlineData("contracts/openapi/external-api.v1.json", "external-api")]
    [InlineData("contracts/asyncapi/core-events.v2.yaml", "core-events")]
    [InlineData("demo.yaml", "demo")]
    public void El_codigo_del_contrato_sale_de_su_ruta(string ruta, string codigo) => Assert.Equal(codigo, ReglasArquitectura.CodigoContrato(ruta));

    [Fact]
    public void El_aplicativo_se_resuelve_por_slug_ruta_workload_repo_y_proyecto_y_lo_ajeno_no_tiene_ficha()
    {
        var proyecto = Guid.NewGuid();
        var porRuta = new AplicativoLeido(Guid.NewGuid(), "por-ruta", "Por ruta", "servicio", null, "flit2", "activo", "flitsas-demo/demo", "api");
        var porCarga = new AplicativoLeido(Guid.NewGuid(), "demo-web", "Por carga", "servicio", null, "flit2", "activo", null, null);
        var delProyecto = new AplicativoLeido(proyecto, "demo-portal", "Del proyecto", "producto", null, "flit2", "activo", null, null);
        var otroProducto = new AplicativoLeido(Guid.NewGuid(), "otro-producto", "Otro", "producto", null, "flit2", "activo", "flitsas-demo/otro", null);
        IReadOnlyList<AplicativoLeido> catalogo = [porRuta, porCarga, delProyecto, otroProducto];
        ElementoLeido Con(params (string K, string V)[] m) =>
            new("demo.x", "container", "X", null, null, [], m.ToDictionary(x => x.K, x => (IReadOnlyList<string>)[x.V]), []);

        const string RepoProyecto = "flitsas-demo/portal";
        AplicativoLeido? Resolver(ElementoLeido e, string? heredado = null) => ReglasArquitectura.ResolverAplicativo(e, heredado, RepoProyecto, proyecto, catalogo);

        Assert.Equal("demo-web", Resolver(Con(("aplicativo", "demo-web")))!.Slug);
        Assert.Equal("por-ruta", Resolver(Con(("repo_path", "/api/")), "FLITSAS-demo/demo")!.Slug);
        Assert.Equal("demo-web", Resolver(Con(("k8s_workload", "demo-web")))!.Slug);
        // Un contenedor del repo del proyecto sin entrada propia va a la ficha del proyecto.
        Assert.Equal("demo-portal", Resolver(Con(("aplicativo", "no-existe")), RepoProyecto)!.Slug);
        // Un sistema con su repo va al producto de ese repo; un actor o un externo no tiene ficha.
        Assert.Equal("otro-producto", Resolver(Con(("repo", "flitsas-demo/otro")))!.Slug);
        Assert.Null(Resolver(Con()));
        Assert.Null(Resolver(Con(("repo", "flitsas-demo/no-esta"))));
    }

    private static string Snapshot(string hosts = """["app.ejemplo.com"]""", string imagen = "ghcr.io/flitsas-demo/app", string extra = "") => $$"""
        {
          "ambiente": "qa", "gitopsSha": "{{Sha}}", "generadoEn": "2026-11-06T15:00:00Z",
          "cluster": { "id": "cluster-demo", "raiz": "root-demo", "namespaces": [
            { "nombre": "demo-qa", "application": "demo-qa", "ingressHosts": {{hosts}},
              "workloads": [ { "kind": "Deployment", "nombre": "demo-web", "replicas": 2, "imagenes": [ { "imagen": "{{imagen}}", "digest": "{{Digest}}" } ] } ] }
          ] }{{extra}}
        }
        """;

    private static (SnapshotDespliegue? Snapshot, string? Codigo, IReadOnlyList<string> Errores) Leer(string json) =>
        ReglasSnapshot.Leer(Elemento(json), "qa", "cluster-demo", Sha);

    [Fact]
    public void Acepta_la_salida_real_del_generador()
    {
        var (snapshot, codigo, errores) = ReglasSnapshot.Leer(
            Elemento(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshot-demo.json"))), "qa", "cluster-a", Sha);
        Assert.True(snapshot is not null, $"{codigo}: {string.Join("; ", errores)}");
        Assert.Equal(["demo-api", "demo-limpieza", "demo-web"], snapshot.Cluster.Namespaces.Single().Workloads.Select(w => w.Nombre));
        Assert.Equal("pgbouncer*", Assert.Single(snapshot.Ignorar!).Patron);
    }

    [Fact]
    public void Lee_un_snapshot_valido()
    {
        var (snapshot, codigo, errores) = Leer(Snapshot());
        Assert.True(snapshot is not null, $"{codigo}: {string.Join("; ", errores)}");
        Assert.Equal("demo-web", snapshot.Cluster.Namespaces.Single().Workloads.Single().Nombre);
    }

    [Theory]
    [InlineData("192.0.2.5")]
    [InlineData("app.svc.cluster.local")]
    [InlineData("nodo.internal")]
    [InlineData("sinpunto")]
    public void Un_host_interno_o_una_ip_en_el_snapshot_se_rechaza_sin_citarlo(string host)
    {
        var (snapshot, codigo, errores) = Leer(Snapshot(hosts: $"""["{host}"]"""));
        Assert.Null(snapshot);
        Assert.Equal("contenido_sensible", codigo);
        Assert.DoesNotContain(host, string.Join(' ', errores), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("192.0.2.5:5000/app")]
    [InlineData("registro.internal/app")]
    [InlineData("registro.ejemplo.com:5000/app")]
    public void Una_imagen_de_un_registro_interno_se_rechaza(string imagen) => Assert.Equal("contenido_sensible", Leer(Snapshot(imagen: imagen)).Codigo);

    [Theory]
    [InlineData(""","env":[{"name":"CLAVE","value":"x"}]""")]
    [InlineData(""","anotaciones":{"a":"b"}""")]
    public void Un_campo_desconocido_en_el_snapshot_se_rechaza(string extra) => Assert.Equal("snapshot_invalido", Leer(Snapshot(extra: extra)).Codigo);

    [Fact]
    public void Un_snapshot_de_otro_ambiente_o_de_otro_sha_se_rechaza()
    {
        Assert.Equal("snapshot_invalido", ReglasSnapshot.Leer(Elemento(Snapshot()), "pdn", "cluster-demo", Sha).Codigo);
        Assert.Equal("snapshot_invalido", ReglasSnapshot.Leer(Elemento(Snapshot()), "qa", "otro", Sha).Codigo);
        Assert.Equal("snapshot_invalido", ReglasSnapshot.Leer(Elemento(Snapshot()), "qa", "cluster-demo", new string('f', 40)).Codigo);
    }

    [Fact]
    public void La_fecha_de_generacion_no_cuenta_en_el_hash_para_que_republicar_de_igual()
    {
        var snapshot = Leer(Snapshot()).Snapshot!;
        Assert.Equal(ReglasSnapshot.Canonico(snapshot).Sha256, ReglasSnapshot.Canonico(snapshot with { GeneradoEn = Ahora.AddHours(6) }).Sha256);
        Assert.NotEqual(ReglasSnapshot.Canonico(snapshot).Sha256, ReglasSnapshot.Canonico(snapshot with { Ignorar = [new("x", "y", null)] }).Sha256);
    }

    [Fact]
    public void El_diff_de_snapshots_ve_workloads_imagenes_replicas_y_hosts()
    {
        var antes = Leer(Snapshot()).Snapshot!;
        var espacio = antes.Cluster.Namespaces.Single();
        var carga = espacio.Workloads.Single();
        var despues = antes with
        {
            Cluster = antes.Cluster with
            {
                Namespaces =
                [
                    espacio with
                    {
                        IngressHosts = ["otro.ejemplo.com"],
                        Workloads = [carga with { Replicas = 3, Imagenes = [new("ghcr.io/flitsas-demo/app", null)] }, new("Deployment", "nuevo", [], 1)],
                    },
                ],
            },
        };

        var diff = ReglasSnapshot.Diff(antes, despues);

        Assert.Contains(diff.Altas, c => c.Tipo == "workload" && c.Id == "demo-qa/Deployment/nuevo");
        Assert.Contains(diff.Altas, c => c.Tipo == "host" && c.Id == "otro.ejemplo.com");
        Assert.Contains(diff.Bajas, c => c.Tipo == "host" && c.Id == "app.ejemplo.com");
        Assert.Contains(diff.Cambios, c => c.Tipo == "imagen");
        Assert.Contains(diff.Cambios, c => c.Tipo == "replicas" && c.Detalle == "2 → 3");
    }

    private static ElementoEsperado Esperado(string carga, string? ambientes = null, params string[] etiquetas) =>
        new($"demo.{carga}", carga, ambientes, etiquetas, Guid.Empty, VisibilidadArquitectura.InternoTi);

    private static List<HallazgoCalculado> D1(IReadOnlyList<ElementoEsperado> elementos, SnapshotDespliegue snapshot) =>
        ReglasDrift.ModeloContraGitops("qa", elementos, [snapshot], DateOnly.FromDateTime(Ahora.UtcDateTime), null, VisibilidadArquitectura.InternoTi);

    [Fact]
    public void D1_sin_diferencias_no_da_hallazgos() =>
        Assert.Empty(D1([Esperado("demo-web")], Leer(Snapshot()).Snapshot!));

    [Fact]
    public void D1_ve_lo_ausente_con_su_severidad_lo_que_no_esta_en_el_modelo_y_lo_que_esta_fuera_de_su_ambiente()
    {
        var snapshot = Leer(Snapshot()).Snapshot!;

        var hallazgos = D1([Esperado("demo-api"), Esperado("demo-pago", null, "critico")], snapshot);
        Assert.Equal(SeveridadDrift.Media, hallazgos.Single(h => h.Sujeto == "demo-api" && h.Codigo == "ausente_en_gitops").Severidad);
        Assert.Equal(SeveridadDrift.Alta, hallazgos.Single(h => h.Sujeto == "demo-pago").Severidad);
        Assert.Contains(hallazgos, h => h.Codigo == "sin_modelo" && h.Sujeto == "demo-web");

        Assert.Equal("fuera_de_ambiente", Assert.Single(D1([Esperado("demo-web", "dev,pdn")], snapshot)).Codigo);
        // Declarado para otro ambiente y ausente aquí: es lo esperado.
        Assert.DoesNotContain(D1([Esperado("demo-api", "pdn"), Esperado("demo-web")], snapshot), h => h.Sujeto == "demo-api");
    }

    [Fact]
    public void D1_objetivo_ausente_no_es_drift_y_un_servicio_fuera_de_k3s_cuenta_como_presente()
    {
        var snapshot = Leer(Snapshot()).Snapshot!;
        Assert.Empty(D1([Esperado("demo-web"), Esperado("demo-futuro", null, "objetivo")], snapshot));

        var fuera = snapshot with { FueraDeK3s = [new("demo-api-vps", "systemd", "demo.demo-api", "2026-10-01")] };
        Assert.Empty(D1([Esperado("demo-web"), Esperado("demo-api")], fuera));
    }

    [Fact]
    public void D1_una_exclusion_vigente_calla_el_hallazgo_y_una_vencida_ya_no()
    {
        var snapshot = Leer(Snapshot()).Snapshot!;
        Assert.Empty(D1([], snapshot with { Ignorar = [new("demo-*", "Infraestructura de prueba", "2027-01-01")] }));
        Assert.Single(D1([], snapshot with { Ignorar = [new("demo-*", "Infraestructura de prueba", "2026-01-01")] }));
    }

    private static List<HallazgoCalculado> D2(IReadOnlyList<AplicacionArgo> aplicaciones) =>
        ReglasDrift.GitopsContraArgo("qa", Leer(Snapshot()).Snapshot!, aplicaciones, Ahora, TimeSpan.FromMinutes(30), null, VisibilidadArquitectura.InternoTi);

    private static AplicacionArgo Sana(string? sync = "Synced", string? salud = "Healthy", int minutos = 5, string? imagen = null) =>
        new("demo-qa", sync, salud, Ahora.AddMinutes(-minutos), [imagen ?? $"ghcr.io/flitsas-demo/app@{Digest}"]);

    [Fact]
    public void D2_sano_no_da_hallazgos() => Assert.Empty(D2([Sana()]));

    [Fact]
    public void D2_ve_sin_sincronizar_sin_salud_e_imagen_distinta_con_severidad_alta()
    {
        Assert.Equal("sin_sincronizar", Assert.Single(D2([Sana(sync: "OutOfSync")])).Codigo);
        Assert.Equal("sin_salud", Assert.Single(D2([Sana(salud: "Degraded")])).Codigo);
        var imagen = Assert.Single(D2([Sana(imagen: "ghcr.io/flitsas-demo/app@sha256:bbbb")]));
        Assert.Equal("imagen_distinta", imagen.Codigo);
        Assert.Equal(SeveridadDrift.Alta, imagen.Severidad);
        Assert.Equal("aplicacion_ausente", Assert.Single(D2([])).Codigo);
    }

    [Fact]
    public void D2_argo_ciego_que_dice_synced_pero_no_reconcilia_es_drift()
    {
        var hallazgo = Assert.Single(D2([Sana(minutos: 45)]));
        Assert.Equal("sin_reconciliar", hallazgo.Codigo);
        Assert.Single(D2([new AplicacionArgo("demo-qa", "Synced", "Healthy", null, [])]));
    }

    [Fact]
    public void D2_un_estado_raro_de_argo_no_llega_tal_cual_a_la_descripcion()
    {
        var hallazgo = Assert.Single(D2([Sana(sync: "<img src=x onerror=alert(1)>")]));
        Assert.DoesNotContain("<img", hallazgo.Descripcion, StringComparison.Ordinal);
    }

    private sealed class Respuesta(HttpStatusCode estado, string cuerpo, Action<HttpRequestMessage>? ver = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ver?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpo) });
        }
    }

    private sealed class Fabrica(HttpMessageHandler manejador) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(manejador, disposeHandler: false);
    }

    private sealed class Monitor(OpcionesArquitectura valor) : IOptionsMonitor<OpcionesArquitectura>
    {
        public OpcionesArquitectura CurrentValue => valor;

        public OpcionesArquitectura Get(string? name) => valor;

        public IDisposable? OnChange(Action<OpcionesArquitectura, string?> listener) => null;
    }

    private static LectorArgoCdHttp Lector(HttpMessageHandler manejador, params ClusterArgo[] clusteres) =>
        new(new Monitor(new OpcionesArquitectura { Argo = [.. clusteres] }), new Fabrica(manejador), NullLogger<LectorArgoCdHttp>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Argo_sin_configuracion_es_sin_datos_y_no_llama_a_nadie()
    {
        var llamadas = 0;
        var lector = Lector(new Respuesta(HttpStatusCode.OK, "{}", _ => llamadas++));

        Assert.False(lector.Configurado("cluster-demo"));
        var lectura = await lector.LeerAsync("cluster-demo", Ct);

        Assert.Equal(ResultadoLecturaArgo.SinConfigurar, lectura.Resultado);
        Assert.Equal(0, llamadas);
    }

    [Fact]
    public async Task Argo_con_url_sin_token_tampoco_cuenta_como_configurado() =>
        Assert.False(Lector(new Respuesta(HttpStatusCode.OK, "{}"), new ClusterArgo { Cluster = "c", Url = "https://argo.ejemplo.com" }).Configurado("c"));

    [Fact]
    public async Task Argo_lee_la_forma_documentada_de_la_api_con_el_token_en_la_cabecera()
    {
        HttpRequestMessage? vista = null;
        var cuerpo = """
            { "items": [ { "metadata": { "name": "demo-qa" }, "spec": { "source": { "helm": { "values": "clave: no-se-lee" } } },
              "status": { "reconciledAt": "2026-11-06T14:55:00Z", "sync": { "status": "Synced" }, "health": { "status": "Healthy" },
                          "summary": { "images": [ "ghcr.io/flitsas-demo/app@sha256:aaaa" ] } } } ] }
            """;
        var lector = Lector(new Respuesta(HttpStatusCode.OK, cuerpo, p => vista = p), new ClusterArgo { Cluster = "c", Url = "https://argo.ejemplo.com/", Token = "ficticio" });

        var lectura = await lector.LeerAsync("c", Ct);

        Assert.Equal(ResultadoLecturaArgo.Correcta, lectura.Resultado);
        var aplicacion = Assert.Single(lectura.Aplicaciones);
        Assert.Equal("Synced", aplicacion.Sync);
        Assert.Equal("Healthy", aplicacion.Salud);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 14, 55, 0, TimeSpan.Zero), aplicacion.ReconciliadaEn);
        Assert.Equal("https://argo.ejemplo.com/api/v1/applications", vista!.RequestUri!.ToString());
        Assert.Equal("Bearer", vista.Headers.Authorization!.Scheme);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", "http_401")]
    [InlineData(HttpStatusCode.OK, "esto no es json", "respuesta_invalida")]
    [InlineData(HttpStatusCode.OK, """{"items":5}""", "respuesta_invalida")]
    public async Task Una_falla_de_argo_es_sin_datos_con_un_motivo_estable(HttpStatusCode estado, string cuerpo, string motivo)
    {
        var lectura = await Lector(new Respuesta(estado, cuerpo), new ClusterArgo { Cluster = "c", Url = "https://argo.ejemplo.com", Token = "ficticio" }).LeerAsync("c", Ct);

        Assert.Equal(ResultadoLecturaArgo.Error, lectura.Resultado);
        Assert.Equal(motivo, lectura.Motivo);
        Assert.Empty(lectura.Aplicaciones);
    }

    [Fact]
    public async Task Un_token_de_argo_no_viaja_por_http_en_claro()
    {
        var llamadas = 0;
        var lectura = await Lector(new Respuesta(HttpStatusCode.OK, "{}", _ => llamadas++), new ClusterArgo { Cluster = "c", Url = "http://argo.ejemplo.com", Token = "ficticio" }).LeerAsync("c", Ct);

        Assert.Equal("url_invalida", lectura.Motivo);
        Assert.Equal(0, llamadas);
    }

    private static PermisosEfectivos Con(params string[] roles)
    {
        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, PermisosArquitectura.Manifiesto, new ManifiestoPermisos("contratos", "Contratos", [new("atlas.contratos.ver_internos", "Ver.", RolesAtlas.LiderTecnico)])]);
        return PermisosEfectivos.Desde(catalogo, [.. roles.Select(r => new ConcesionRol(r, null, AlcanceAsignacion.Global, null, "manual"))]);
    }

    [Theory]
    [InlineData(RolesAtlas.Superadmin, true, true)]
    [InlineData(RolesAtlas.LiderTecnico, true, true)]
    [InlineData(RolesAtlas.Desarrollador, true, false)]
    [InlineData(RolesAtlas.GobiernoIa, false, true)]
    [InlineData(RolesAtlas.AsesorComercial, false, false)]
    [InlineData(RolesAtlas.Integrador, false, false)]
    [InlineData(RolesAtlas.Soporte, false, false)]
    [InlineData(RolesAtlas.Lector, false, false)]
    public void Los_niveles_de_cada_rol_siguen_el_prompt_maestro(string rol, bool interno, bool restringido)
    {
        var acceso = new AccesoArquitectura(Con(rol));
        Assert.Equal(interno, acceso.Niveles.Contains(VisibilidadArquitectura.InternoTi));
        Assert.Equal(restringido, acceso.Niveles.Contains(VisibilidadArquitectura.Restringido));
    }

    [Fact]
    public void Un_asesor_comercial_tiene_el_permiso_pero_ningun_nivel()
    {
        var permisos = Con(RolesAtlas.AsesorComercial);
        Assert.True(permisos.Tiene(PermisosArquitectura.Ver));
        Assert.Empty(new AccesoArquitectura(permisos).Niveles);
        Assert.False(new AccesoArquitectura(permisos).VeDespliegue);
    }
}
