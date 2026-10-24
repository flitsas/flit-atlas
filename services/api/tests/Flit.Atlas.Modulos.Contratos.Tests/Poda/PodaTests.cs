using System.Text.Json.Nodes;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Poda;
using static Flit.Atlas.Modulos.Contratos.Tests.Poda.OraculoPoda;

namespace Flit.Atlas.Modulos.Contratos.Tests.Poda;

/// <summary>
/// Casos puntuales de la poda (HU 4.2, ATLAS-ADR-0038): cada regla y cada error fail-closed, con specs ficticios
/// (<c>ejemplo.com</c>) y con los reales donde aplica.
/// </summary>
public class PodaTests
{
    private static readonly DestinoPoda Pdn = new(AmbientesPoda.Pdn, [new Uri("https://api.ejemplo.com")]);

    private const string OpenApi = """
        openapi: 3.1.0
        info:
          title: API Demo
          version: 1.0.0
        servers:
          - url: https://interno.ejemplo.com
        security:
          - global: []
        tags:
          - name: tramites
            description: Trámites.
          - name: admin
          - name: sin-uso
        paths:
          /tramites/{id}:
            servers:
              - url: https://otro-interno.ejemplo.com
            parameters:
              - $ref: '#/components/parameters/Id'
            get:
              operationId: obtenerTramite
              tags: [tramites]
              security:
                - bearer: []
              responses:
                '200':
                  description: El trámite.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Tramite' }
                      examples:
                        publico: { value: { id: t-1 } }
                        secreto: { value: { id: t-2 }, x-interno: true }
                  links:
                    borrar: { operationId: borrarTramite }
            delete:
              operationId: borrarTramite
              tags: [admin]
              responses:
                '204': { description: Borrado. }
          /tramites:
            post:
              operationId: crearTramite
              tags: [tramites]
              requestBody:
                content:
                  application/json:
                    schema: { $ref: '#/components/schemas/Nuevo' }
              responses:
                '201': { description: Creado. }
          /logs:
            get:
              operationId: logs
              x-internal: true
              responses:
                '200':
                  description: Logs.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Log' }
        webhooks:
          tramiteRadicado:
            post:
              operationId: radicado
              requestBody:
                content:
                  application/json:
                    schema: { $ref: '#/components/schemas/Evento' }
              responses:
                '200': { description: Recibido. }
        components:
          parameters:
            Id: { name: id, in: path, required: true, schema: { type: string } }
          schemas:
            Tramite:
              type: object
              required: [id, costoInterno]
              x-enum-descriptions: { A: Activo }
              x-flit-obligatoriedad: { traspaso: [id] }
              properties:
                id: { type: string }
                estado: { $ref: '#/components/schemas/Estado' }
                costoInterno: { type: number, x-internal: true }
            Estado: { type: string, enum: [A] }
            Nuevo: { type: object, properties: { placa: { type: string } } }
            Log: { type: object }
            Evento: { type: object }
            Huerfano: { type: object }
          securitySchemes:
            bearer: { type: http, scheme: bearer }
            global: { type: apiKey, in: header, name: X-Llave }
            sinUso: { type: http, scheme: basic }
        """;

    private static SpecPodado Podar(string spec, params string[] claves) => Podadores.OpenApi.Podar(new SolicitudPoda(spec, claves, Pdn));

    [Fact]
    public void Conserva_solo_lo_seleccionado_y_lo_que_alcanza()
    {
        var podado = Podar(OpenApi, "GET /tramites/{id}");
        var r = Leer(podado.Contenido);

        Assert.Equal(["GET /tramites/{id}"], podado.Resumen.Operaciones);
        Assert.Equal(["/tramites/{id}"], ((JsonObject)r["paths"]!).Select(p => p.Key));
        Assert.Null(r["paths"]!["/tramites/{id}"]!["delete"]);
        Assert.Null(r["webhooks"]);
        Assert.Equal(["parameters/Id", "schemas/Tramite", "schemas/Estado", "securitySchemes/bearer", "securitySchemes/global"],
            podado.Resumen.ComponentesConservados);
        Assert.Contains("schemas/Huerfano", podado.Resumen.ComponentesEliminados);
        Assert.Contains("schemas/Log", podado.Resumen.ComponentesEliminados);
        Assert.Contains("securitySchemes/sinUso", podado.Resumen.ComponentesEliminados);
        Assert.Equal(["admin", "sin-uso"], podado.Resumen.EtiquetasEliminadas);
        Assert.Equal(["tramites"], ((JsonArray)r["tags"]!).Select(t => (string)t!["name"]!));
        Assert.Equal(FormatoSpec.Yaml, podado.Formato);
    }

    [Fact]
    public void Quita_lo_interno_y_su_required_y_conserva_las_extensiones()
    {
        var podado = Podar(OpenApi, "GET /tramites/{id}");
        var tramite = Leer(podado.Contenido)["components"]!["schemas"]!["Tramite"]!;
        var ejemplos = (JsonObject)Leer(podado.Contenido)["paths"]!["/tramites/{id}"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["examples"]!;

        Assert.Null(tramite["properties"]!["costoInterno"]);
        Assert.Equal(["id"], ((JsonArray)tramite["required"]!).Select(x => (string)x!));
        Assert.Equal("Activo", (string)tramite["x-enum-descriptions"]!["A"]!);
        Assert.NotNull(tramite["x-flit-obligatoriedad"]);
        Assert.Equal(["publico"], ejemplos.Select(e => e.Key));
        Assert.Equal(2, podado.Resumen.NodosInternosEliminados);
    }

    [Fact]
    public void Quita_la_procedencia_interna_en_cualquier_nivel()
    {
        var spec = OpenApi
            .Replace("  version: 1.0.0\n", "  version: 1.0.0\n  x-flit-fuente: { repo: flitsas-demo/demo }\n  x-flit-ambientes: { qa: qa.ejemplo.com }\n", StringComparison.Ordinal)
            .Replace("Estado: { type: string, enum: [A] }", "Estado: { type: string, enum: [A], x-flit-fuente: hoja 2 }", StringComparison.Ordinal);
        Assert.Contains("x-flit-fuente: { repo", spec, StringComparison.Ordinal);

        var podado = Podar(spec, "GET /tramites/{id}");

        Assert.DoesNotContain("x-flit-fuente", podado.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("x-flit-ambientes", podado.Contenido, StringComparison.Ordinal);
        Assert.Contains("x-flit-obligatoriedad", podado.Contenido, StringComparison.Ordinal);
        Assert.Contains("x-enum-descriptions", podado.Contenido, StringComparison.Ordinal);
    }

    [Fact]
    public void Reescribe_los_servers_y_quita_los_anidados_y_los_links_a_lo_no_compartido()
    {
        var podado = Podar(OpenApi, "GET /tramites/{id}");
        var r = Leer(podado.Contenido);

        Assert.Equal("https://api.ejemplo.com", (string)r["servers"]![0]!["url"]!);
        Assert.Single((JsonArray)r["servers"]!);
        Assert.Null(r["paths"]!["/tramites/{id}"]!["servers"]);
        Assert.Null(r["paths"]!["/tramites/{id}"]!["get"]!["responses"]!["200"]!["links"]!["borrar"]);
        Assert.Contains(podado.Resumen.Advertencias, a => a.Contains("borrar", StringComparison.Ordinal));
        Assert.DoesNotContain("interno.ejemplo.com", podado.Contenido, StringComparison.Ordinal);

        var conLink = Podar(OpenApi, "GET /tramites/{id}", "DELETE /tramites/{id}");
        Assert.NotNull(Leer(conLink.Contenido)["paths"]!["/tramites/{id}"]!["get"]!["responses"]!["200"]!["links"]!["borrar"]);
    }

    [Fact]
    public void Sin_url_publica_el_resultado_no_tiene_servers()
    {
        var podado = Podadores.OpenApi.Podar(new SolicitudPoda(OpenApi, ["POST /tramites"], new DestinoPoda(AmbientesPoda.Qa, [])));

        Assert.False(Leer(podado.Contenido).ContainsKey("servers"));
        Assert.Empty(podado.Resumen.Servidores);
    }

    [Fact]
    public void Los_webhooks_de_31_se_seleccionan_por_su_clave()
    {
        var podado = Podar(OpenApi, "POST webhook:tramiteRadicado");
        var r = Leer(podado.Contenido);

        Assert.Empty((JsonObject)r["paths"]!);
        Assert.NotNull(r["webhooks"]!["tramiteRadicado"]!["post"]);
        Assert.Contains("schemas/Evento", podado.Resumen.ComponentesConservados);
    }

    [Fact]
    public void Una_clave_que_no_existe_es_error_y_dice_cual()
    {
        var error = Assert.Throws<PodaException>(() => Podar(OpenApi, "GET /tramites/{id}", "GET /no-existe", "get /tramites"));

        Assert.Equal(("claves_desconocidas", MotivoPoda.ClavesDesconocidas), (error.Codigo, error.Motivo));
        Assert.Equal(["GET /no-existe", "get /tramites"], error.Detalles);
    }

    [Fact]
    public void La_seleccion_vacia_es_error() =>
        Assert.Equal(MotivoPoda.SeleccionVacia, Assert.Throws<PodaException>(() => Podar(OpenApi)).Motivo);

    [Fact]
    public void Seleccionar_algo_interno_es_error()
    {
        var error = Assert.Throws<PodaException>(() => Podar(OpenApi, "GET /logs"));

        Assert.Equal(MotivoPoda.OperacionInterna, error.Motivo);
        Assert.Contains(Podadores.OpenApi.Operaciones(OpenApi), o => o is { Clave: "GET /logs", Interna: true });
    }

    [Fact]
    public void Una_referencia_externa_alcanzable_falla_y_una_no_alcanzable_no_importa()
    {
        var spec = OpenApi.Replace("schema: { $ref: '#/components/schemas/Nuevo' }", "schema: { $ref: 'https://ejemplo.com/nuevo.json' }", StringComparison.Ordinal);

        Assert.Equal(MotivoPoda.ReferenciaExterna, Assert.Throws<PodaException>(() => Podar(spec, "POST /tramites")).Motivo);
        Assert.NotNull(Podar(spec, "GET /tramites/{id}"));
    }

    [Theory]
    [InlineData("$ref: '#/components/schemas/Estado'", "$ref: '#/components/schemas/NoExiste'")]
    [InlineData("Estado: { type: string, enum: [A] }", "Estado: { type: string, enum: [A], x-internal: true }")]
    [InlineData("- bearer: []", "- bearer: []\n        - interno: []")]
    public void Una_referencia_rota_o_a_algo_interno_falla(string original, string reemplazo)
    {
        var spec = OpenApi.Replace(original, reemplazo, StringComparison.Ordinal)
            .Replace("sinUso: { type: http, scheme: basic }", "sinUso: { type: http, scheme: basic }\n    interno: { type: http, scheme: basic, x-internal: true }", StringComparison.Ordinal);

        var error = Assert.Throws<PodaException>(() => Podar(spec, "GET /tramites/{id}"));

        Assert.Equal(MotivoPoda.ReferenciaRota, error.Motivo);
    }

    [Theory]
    [InlineData("staging", "https://api.ejemplo.com")]
    [InlineData("pdn", "http://api.ejemplo.com")]
    [InlineData("pdn", "https://10.0.0.8")]
    [InlineData("pdn", "https://localhost:5031")]
    [InlineData("pdn", "https://core-api.flit2-pdn.svc")]
    [InlineData("pdn", "https://usuario:clave@api.ejemplo.com")]
    [InlineData("pdn", "https://api.ejemplo.com/?token=x")]
    public void Un_destino_que_no_es_publico_es_error(string ambiente, string url)
    {
        var error = Assert.Throws<PodaException>(() => Podadores.OpenApi.Podar(new SolicitudPoda(OpenApi, ["POST /tramites"], new DestinoPoda(ambiente, [new Uri(url)]))));

        Assert.Equal(MotivoPoda.DestinoInvalido, error.Motivo);
    }

    [Theory]
    [InlineData("hola: mundo\n")]
    [InlineData("swagger: '2.0'\ninfo: { title: Viejo, version: 1.0.0 }\npaths: {}\n")]
    [InlineData("openapi: 3.2.0\ninfo: { title: Nuevo, version: 1.0.0 }\npaths: {}\n")]
    [InlineData("openapi: 3.1.0\ninfo: [\n")]
    [InlineData("[1, 2]")]
    public void Un_spec_que_no_es_openapi_3_0_o_3_1_es_error(string spec) =>
        Assert.Equal(MotivoPoda.SpecInvalida, Assert.Throws<PodaException>(() => Podar(spec, "GET /a")).Motivo);

    [Fact]
    public void Un_path_item_por_referencia_se_copia_en_linea()
    {
        const string spec = """
            {"openapi":"3.1.0","info":{"title":"Demo","version":"1.0.0"},
             "paths":{"/a":{"$ref":"#/components/pathItems/A","summary":"Ruta A"}},
             "components":{"pathItems":{"A":{"get":{"responses":{"200":{"description":"ok"}}},"delete":{"responses":{"204":{"description":"ok"}}}}}}}
            """;

        var podado = Podar(spec, "GET /a");
        var r = Leer(podado.Contenido);

        Assert.Equal(FormatoSpec.Json, podado.Formato);
        Assert.Equal(["get", "summary"], ((JsonObject)r["paths"]!["/a"]!).Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Null(r["components"]);
    }

    [Fact]
    public void Discriminator_mapping_conserva_los_schemas_que_nombra()
    {
        const string spec = """
            openapi: 3.0.3
            info: { title: Demo, version: 1.0.0 }
            paths:
              /pagos:
                get:
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            oneOf: [{ $ref: '#/components/schemas/Tarjeta' }]
                            discriminator: { propertyName: tipo, mapping: { tarjeta: Tarjeta, pse: '#/components/schemas/Pse' } }
            components:
              schemas:
                Tarjeta: { type: object }
                Pse: { type: object }
                Otro: { type: object }
            """;

        var podado = Podar(spec, "GET /pagos");

        Assert.Equal(["schemas/Tarjeta", "schemas/Pse"], podado.Resumen.ComponentesConservados);
        Assert.Equal(["schemas/Otro"], podado.Resumen.ComponentesEliminados);
    }

    [Fact]
    public void El_ict_enriquecido_compartido_conserva_obligatoriedad_y_tablas_de_referencia()
    {
        var spec = Spec("contracts/ict/core-ict.enriquecido.v1.yaml");
        var publicas = AnalizadorSpecPublicas(spec);

        var podado = Podadores.OpenApi.Podar(new SolicitudPoda(spec, publicas, Pdn));

        Assert.Equal(publicas.Order(StringComparer.Ordinal), podado.Resumen.Operaciones.Order(StringComparer.Ordinal));
        Assert.Contains("x-flit-obligatoriedad", podado.Contenido, StringComparison.Ordinal);
        Assert.Contains("x-enum-descriptions", podado.Contenido, StringComparison.Ordinal);
        Assert.Contains("x-flit-fuente", spec, StringComparison.Ordinal);
        Assert.DoesNotContain("x-flit-fuente", podado.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("x-flit-ambientes", podado.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings", podado.Contenido, StringComparison.Ordinal);
        Assert.Empty(Internos(Leer(podado.Contenido)));
        Assert.Equal(["https://api.ejemplo.com"], ((JsonArray)Leer(podado.Contenido)["servers"]!).Select(s => (string)s!["url"]!));
    }

    private static List<string> AnalizadorSpecPublicas(string spec) =>
        [.. Flit.Atlas.Modulos.Contratos.Specs.AnalizadorSpec.Analizar(TipoContrato.Openapi, spec).Operaciones.Where(o => o.Publica).Select(o => o.Clave)];

    // --- AsyncAPI ---

    private const string AsyncApi3 = """
        asyncapi: 3.0.0
        info:
          title: Eventos Demo
          version: 1.0.0
          tags:
            - name: tramites
            - $ref: '#/components/tags/internos'
        servers:
          rabbit:
            host: rabbitmq.interno:5672
            protocol: amqp
            security:
              - $ref: '#/components/securitySchemes/usuario'
        channels:
          tramites:
            address: tramites.eventos
            servers:
              - $ref: '#/servers/rabbit'
            messages:
              radicado: { $ref: '#/components/messages/Radicado' }
              anulado: { $ref: '#/components/messages/Anulado' }
          auditoria:
            address: auditoria
            x-internal: true
            messages:
              registro: { $ref: '#/components/messages/Registro' }
        operations:
          publicarRadicado:
            action: send
            channel: { $ref: '#/channels/tramites' }
            messages:
              - $ref: '#/channels/tramites/messages/radicado'
            tags: [{ name: tramites }]
          publicarAnulado:
            action: send
            channel: { $ref: '#/channels/tramites' }
            messages:
              - $ref: '#/channels/tramites/messages/anulado'
            tags: [{ $ref: '#/components/tags/internos' }]
          auditar:
            action: send
            channel: { $ref: '#/channels/auditoria' }
        components:
          tags:
            internos: { name: internos }
          messages:
            Radicado: { payload: { $ref: '#/components/schemas/Radicado' } }
            Anulado: { payload: { type: object } }
            Registro: { payload: { type: object } }
          schemas:
            Radicado: { type: object, properties: { id: { type: string } } }
          securitySchemes:
            usuario: { type: userPassword }
        """;

    private static readonly DestinoPoda Broker = new(AmbientesPoda.Qa, [new Uri("amqps://broker.ejemplo.com:5671/atlas")]);

    [Fact]
    public void AsyncApi_3_conserva_la_operacion_su_canal_y_solo_sus_mensajes()
    {
        var podado = Podadores.AsyncApi.Podar(new SolicitudPoda(AsyncApi3, ["send tramites.eventos"], Broker));
        var r = Leer(podado.Contenido);

        Assert.Equal(["send tramites.eventos"], podado.Resumen.Operaciones);
        Assert.Equal(["publicarRadicado"], ((JsonObject)r["operations"]!).Select(o => o.Key));
        Assert.Equal(["radicado"], ((JsonObject)r["channels"]!["tramites"]!["messages"]!).Select(m => m.Key));
        Assert.Null(r["channels"]!["tramites"]!["servers"]);
        Assert.Null(r["channels"]!["auditoria"]);
        Assert.Equal(["channels/tramites", "messages/Radicado", "schemas/Radicado", "securitySchemes/usuario"], podado.Resumen.ComponentesConservados.Order(StringComparer.Ordinal));
        Assert.Contains("tags/internos", podado.Resumen.ComponentesEliminados);
        Assert.Equal(["internos"], podado.Resumen.EtiquetasEliminadas);
        var servidor = r["servers"]!["qa"]!;
        Assert.Equal(("broker.ejemplo.com:5671", "amqps", "/atlas"), ((string)servidor["host"]!, (string)servidor["protocol"]!, (string)servidor["pathname"]!));
        Assert.Equal("#/components/securitySchemes/usuario", (string)servidor["security"]![0]!["$ref"]!);
        Assert.DoesNotContain("rabbitmq.interno", podado.Contenido, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncApi_la_clave_repetida_lleva_el_id_y_tambien_se_acepta_al_podar_lo_podado()
    {
        var claves = Podadores.AsyncApi.Operaciones(AsyncApi3).Select(o => o.Clave).ToList();
        Assert.Equal(["send tramites.eventos", "send tramites.eventos (publicarAnulado)", "send auditoria"], claves);

        var podado = Podadores.AsyncApi.Podar(new SolicitudPoda(AsyncApi3, ["send tramites.eventos (publicarAnulado)"], Broker));
        var otraVez = Podadores.AsyncApi.Podar(new SolicitudPoda(podado.Contenido, ["send tramites.eventos (publicarAnulado)"], Broker));

        Assert.Equal(podado.Contenido, otraVez.Contenido);
        Assert.Equal(MotivoPoda.OperacionInterna, Assert.Throws<PodaException>(() => Podadores.AsyncApi.Podar(new SolicitudPoda(AsyncApi3, ["send auditoria"], Broker))).Motivo);
    }

    [Fact]
    public void AsyncApi_2_conserva_solo_la_accion_elegida_del_canal()
    {
        var spec = Spec("services/api/tests/Flit.Atlas.Modulos.Contratos.Tests/Poda/Specs/domain-events.v1.yaml");

        var podado = Podadores.AsyncApi.Podar(new SolicitudPoda(spec, ["publish flito-webhook/tramite.cambio"], Broker));
        var r = Leer(podado.Contenido);

        Assert.Equal(["flito-webhook/tramite.cambio"], ((JsonObject)r["channels"]!).Select(c => c.Key));
        Assert.Equal("broker.ejemplo.com:5671/atlas", (string)r["servers"]!["qa"]!["url"]!);
        Assert.Contains("messages/VehicleStateChanged", podado.Resumen.ComponentesEliminados);
    }

    [Fact]
    public void AsyncApi_2_con_referencia_a_otro_archivo_falla_cerrado()
    {
        var spec = Spec("services/api/tests/Flit.Atlas.Modulos.Contratos.Tests/Poda/Specs/tramites-events.v1.yaml");
        var claves = Podadores.AsyncApi.Operaciones(spec).Select(o => o.Clave).ToList();

        var error = Assert.Throws<PodaException>(() => Podadores.AsyncApi.Podar(new SolicitudPoda(spec, claves, Broker)));

        Assert.Equal(MotivoPoda.ReferenciaExterna, error.Motivo);
        Assert.Contains(error.Detalles, d => d.StartsWith("./comun/", StringComparison.Ordinal));
    }

    // --- Lectura y escritura ---

    [Fact]
    public void El_yaml_escrito_se_lee_igual_y_no_cambia_tipos()
    {
        var original = new JsonObject
        {
            ["si"] = "yes",
            ["no"] = "no",
            ["version"] = "1.0",
            ["fecha"] = "2026-10-24",
            ["nulo"] = "null",
            ["vacio"] = "",
            ["comentario"] = "a #b",
            ["dos puntos"] = "a: b",
            ["multilinea"] = "línea 1\nlínea 2\n",
            ["espacio final"] = "con espacio \nfin",
            ["unicode"] = "Organismo Demo — trámite ñ",
            ["numero"] = JsonNode.Parse("1.50"),
            ["entero"] = 200,
            ["booleano"] = true,
            ["ninguno"] = null,
            ["200"] = "respuesta",
            ["$ref"] = "#/components/schemas/A",
            ["/tramites/{id}"] = "/ruta/{id}",
            ["indicadores"] = new JsonArray("~", "-", "- a", "*alias", "&ancla", "!etiqueta", "%x", "?", "|", ">", "@x", "`x", "'x'", "\"x\"", "{a}", "[a]", "a,b", "Yes", "NO", "On", ".inf", "0x1F", "1_000", "12:30", "1e3", "+1", "a\tb", " a", "a "),
            ["lista"] = new JsonArray("a", 1, false, new JsonObject()),
            ["vacios"] = new JsonObject { ["o"] = new JsonObject(), ["a"] = new JsonArray() },
        };

        var yaml = TextoSpec.Escribir(original, FormatoSpec.Yaml);
        var leido = TextoSpec.Leer(yaml, out var formato);

        Assert.Equal(FormatoSpec.Yaml, formato);
        Assert.True(JsonNode.DeepEquals(original, leido), yaml);
        Assert.Equal(original.Select(p => p.Key), leido.Select(p => p.Key));
        Assert.Contains("numero: 1.50", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_yaml_con_alias_que_se_multiplican_se_rechaza()
    {
        var bomba = "openapi: 3.1.0\na: &a [x, x, x, x, x, x, x, x, x, x]\n";
        for (var i = 0; i < 9; i++)
        {
            var anterior = (char)('a' + i);
            var siguiente = (char)('a' + i + 1);
            bomba += $"{siguiente}: &{siguiente} [*{anterior}, *{anterior}, *{anterior}, *{anterior}, *{anterior}, *{anterior}, *{anterior}, *{anterior}, *{anterior}, *{anterior}]\n";
        }

        Assert.Equal(MotivoPoda.SpecInvalida, Assert.Throws<PodaException>(() => TextoSpec.Leer(bomba, out _)).Motivo);
    }

    // --- El oráculo sí detecta fallas (si no, las pruebas de propiedades no probarían nada) ---

    [Fact]
    public void El_oraculo_detecta_huerfanos_referencias_rotas_e_internos()
    {
        var malo = Leer("""
            {"openapi":"3.1.0","paths":{"/a":{"get":{"x-internal":true,"responses":{"200":{"$ref":"#/components/responses/NoExiste"}}}}},
             "components":{"schemas":{"Solo":{"type":"object"}}}}
            """);

        Assert.Equal(["components/schemas/Solo"], Huerfanos(malo, false));
        Assert.Equal(["#/components/responses/NoExiste"], ReferenciasRotas(malo));
        Assert.Equal(["/paths//a/get"], Internos(malo));
        Assert.Equal(["/info/x-flit-fuente"], Internos(Leer("""{"info":{"x-flit-fuente":{"repo":"x"},"x-flit-obligatoriedad":{}}}""")));
        Assert.False(Subconjunto(JsonNode.Parse("""{"a":[1,3]}"""), JsonNode.Parse("""{"a":[3,1]}""")));
        Assert.True(Subconjunto(JsonNode.Parse("""{"a":[1,3]}"""), JsonNode.Parse("""{"a":[1,2,3],"b":0}""")));
    }
}
