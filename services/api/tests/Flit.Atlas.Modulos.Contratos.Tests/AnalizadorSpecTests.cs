using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Specs;

namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>
/// Validación e índice de los specs (HU 3.2, ATLAS-ADR-0035): OpenAPI 3.x, AsyncAPI 2.x y 3.x y proto, con datos
/// ficticios. Sin red ni base.
/// </summary>
public class AnalizadorSpecTests
{
    [Fact]
    public void Un_openapi_valido_trae_sus_operaciones_y_webhooks_con_lo_publico_marcado()
    {
        var spec = AnalizadorSpec.Analizar(TipoContrato.Openapi, SpecsDeEjemplo.OpenApiYaml);

        Assert.True(spec.Valido, string.Join('\n', spec.Errores));
        Assert.Equal((FormatoSpec.Yaml, "API Demo", "1.2.0"), (spec.Formato, spec.Titulo, spec.VersionDelSpec));
        Assert.Equal(["DELETE /tramites/{id}", "GET /tramites/{id}", "POST webhook:tramiteRadicado"], spec.Operaciones.Select(o => o.Clave).Order(StringComparer.Ordinal));
        var obtener = spec.Operaciones.Single(o => o.OperationId == "obtenerTramite");
        Assert.Equal(("operacion", "GET", "/tramites/{id}", "Obtener un trámite", true), (obtener.Clase, obtener.Metodo, obtener.Ruta, obtener.Resumen, obtener.Publica));
        Assert.Equal(["tramites", "public"], obtener.Etiquetas);
        Assert.False(spec.Operaciones.Single(o => o.OperationId == "eliminarTramite").Publica);
        Assert.True(spec.Operaciones.Single(o => o.Clase == "webhook").Publica);
    }

    [Fact]
    public void El_mismo_openapi_en_json_tambien_vale()
    {
        const string json = """
            { "openapi": "3.0.3", "info": { "title": "API Demo", "version": "0.1.0" },
              "paths": { "/salud": { "get": { "responses": { "200": { "description": "Bien." } } } } } }
            """;

        var spec = AnalizadorSpec.Analizar(TipoContrato.Openapi, json);

        Assert.True(spec.Valido, string.Join('\n', spec.Errores));
        Assert.Equal(FormatoSpec.Json, spec.Formato);
        Assert.Equal("GET /salud", Assert.Single(spec.Operaciones).Clave);
    }

    [Theory]
    [InlineData("openapi: 3.1.0\ninfo:\n  title: Sin version\npaths: {}\n", "version")]
    [InlineData("openapi: 3.1.0\ninfo:\n  title: Demo\n  version: 1.0.0\npaths:\n  /a:\n    get:\n      responses:\n        '200':\n          description: d\n          content:\n            application/json:\n              schema: { $ref: '#/components/schemas/NoExiste' }\n", "NoExiste")]
    [InlineData("swagger: '2.0'\ninfo:\n  title: Viejo\n  version: 1.0.0\npaths: {}\n", "OpenAPI 3")]
    [InlineData("hola: mundo\n", "openapi")]
    [InlineData("openapi: 3.1.0\ninfo: [\n", "leer")]
    public void Un_openapi_invalido_dice_por_que(string contenido, string menciona)
    {
        var spec = AnalizadorSpec.Analizar(TipoContrato.Openapi, contenido);

        Assert.False(spec.Valido);
        Assert.Contains(spec.Errores, e => e.Contains(menciona, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Un_asyncapi_3_valido_trae_sus_operaciones_con_la_direccion_del_canal()
    {
        var spec = AnalizadorSpec.Analizar(TipoContrato.Asyncapi, SpecsDeEjemplo.AsyncApi3);

        Assert.True(spec.Valido, string.Join('\n', spec.Errores));
        Assert.Equal(("Eventos Demo", "2.0.0"), (spec.Titulo, spec.VersionDelSpec));
        Assert.Equal(["receive tramites.radicado", "send tramites.radicado"], spec.Operaciones.Select(o => o.Clave).Order(StringComparer.Ordinal));
        var enviar = spec.Operaciones.Single(o => o.Metodo == "send");
        Assert.Equal(("canal", "tramites.radicado", "publicarRadicado", true), (enviar.Clase, enviar.Ruta, enviar.OperationId, enviar.Publica));
    }

    [Fact]
    public void Un_asyncapi_2_valido_trae_publish_y_subscribe_por_canal()
    {
        const string v2 = """
            { "asyncapi": "2.6.0", "info": { "title": "Eventos v2", "version": "1.0.0" },
              "channels": { "pagos.aprobado": { "subscribe": { "operationId": "pagoAprobado", "summary": "Pago aprobado." } },
                            "pagos.pedido": { "publish": { "operationId": "pedirPago" }, "x-public": true } } }
            """;

        var spec = AnalizadorSpec.Analizar(TipoContrato.Asyncapi, v2);

        Assert.True(spec.Valido, string.Join('\n', spec.Errores));
        Assert.Equal(["publish pagos.pedido", "subscribe pagos.aprobado"], spec.Operaciones.Select(o => o.Clave).Order(StringComparer.Ordinal));
        Assert.True(spec.Operaciones.Single(o => o.OperationId == "pedirPago").Publica);
    }

    [Theory]
    [InlineData("asyncapi: 1.2.0\ninfo: { title: x, version: '1' }\n", "AsyncAPI 2.x o 3.x")]
    [InlineData("asyncapi: 2.6.0\ninfo: { title: x, version: 1.0.0 }\n", "/channels")]
    [InlineData("asyncapi: 3.0.0\ninfo: { version: 1.0.0 }\n", "/info/title")]
    [InlineData("asyncapi: 3.0.0\ninfo: { title: x, version: 1.0.0 }\noperations:\n  a: { action: enviar, channel: { $ref: '#/channels/a' } }\n", "send o receive")]
    [InlineData("asyncapi: 3.0.0\ninfo: { title: x, version: 1.0.0 }\noperations:\n  a: { action: send, channel: { $ref: '#/channels/noexiste' } }\n", "no apunta")]
    [InlineData("- una\n- lista\n", "objeto")]
    public void Un_asyncapi_invalido_dice_por_que(string contenido, string menciona)
    {
        var spec = AnalizadorSpec.Analizar(TipoContrato.Asyncapi, contenido);

        Assert.False(spec.Valido);
        Assert.Contains(spec.Errores, e => e.Contains(menciona, StringComparison.Ordinal));
    }

    [Fact]
    public void Un_yaml_con_bomba_de_alias_no_tumba_el_servidor()
    {
        var bomba = "asyncapi: 3.0.0\ninfo: { title: x, version: 1.0.0 }\na: &a [x, x, x, x, x, x, x, x, x, x]\n"
            + string.Concat(Enumerable.Range(0, 12).Select(i => $"{(char)('b' + i)}: &{(char)('b' + i)} [*{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}, *{(char)('a' + i)}]\n"));

        var spec = AnalizadorSpec.Analizar(TipoContrato.Asyncapi, bomba);

        Assert.False(spec.Valido);
    }

    [Fact]
    public void Un_proto_trae_sus_rpc_y_los_imports_de_otros_archivos_son_advertencias()
    {
        var spec = AnalizadorSpec.Analizar(TipoContrato.Proto, SpecsDeEjemplo.Proto);

        Assert.True(spec.Valido, string.Join('\n', spec.Errores));
        Assert.Equal(("flitsas_demo.tramites.v1", "1"), (spec.Titulo, spec.VersionDelSpec));
        Assert.Equal(
            ["rpc:flitsas_demo.tramites.v1.TramitesService/Obtener", "rpc:flitsas_demo.tramites.v1.TramitesService/Seguir"],
            spec.Operaciones.Select(o => o.Clave).Order(StringComparer.Ordinal));
        Assert.Contains("stream flitsas_demo.tramites.v1.Tramite", spec.Operaciones.Single(o => o.OperationId == "Seguir").Resumen, StringComparison.Ordinal);
        Assert.Contains(spec.Advertencias, a => a.Contains("comun/v1/sobre.proto", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("syntax = \"proto3\"; service X { rpc A (B) returns (C) ")]
    [InlineData("esto no es proto")]
    public void Un_proto_con_errores_de_sintaxis_no_vale(string contenido)
    {
        Assert.False(AnalizadorSpec.Analizar(TipoContrato.Proto, contenido).Valido);
    }

    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("3.4.0-dev.828c9fe", 3, 4, 0, "dev.828c9fe")]
    [InlineData("0.1.0-rc.1", 0, 1, 0, "rc.1")]
    public void Lee_versiones_semanticas(string texto, int mayor, int menor, int parche, string? prerelease)
    {
        Assert.Equal(new VersionSemantica(mayor, menor, parche, prerelease), VersionSemantica.Leer(texto));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3+build.5")]
    [InlineData("1.2.3-")]
    [InlineData("9999999999.0.0")]
    // Un sha7 de solo dígitos con cero inicial no es un identificador numérico válido: el workflow usa dev.g<sha7> si hace falta.
    [InlineData("1.0.0-dev.0123456")]
    public void Rechaza_lo_que_no_es_semver_sin_metadatos(string texto)
    {
        Assert.Null(VersionSemantica.Leer(texto));
    }

    [Fact]
    public void La_version_de_la_peticion_debe_cuadrar_con_la_del_spec()
    {
        var openapi = AnalizadorSpec.Analizar(TipoContrato.Openapi, SpecsDeEjemplo.OpenApiYaml);
        var proto = AnalizadorSpec.Analizar(TipoContrato.Proto, SpecsDeEjemplo.Proto);

        Assert.Equal("1.2.0", EndpointsPublicacion.Versionar(TipoContrato.Openapi, null, openapi).Version?.ToString());
        Assert.Equal("1.2.0-dev.abc1234", EndpointsPublicacion.Versionar(TipoContrato.Openapi, "1.2.0-dev.abc1234", openapi).Version?.ToString());
        Assert.Equal("version_distinta_del_spec", EndpointsPublicacion.Versionar(TipoContrato.Openapi, "1.3.0", openapi).Problema?.Codigo);
        Assert.Equal("version_invalida", EndpointsPublicacion.Versionar(TipoContrato.Openapi, "uno", openapi).Problema?.Codigo);
        Assert.Equal("1.0.0-dev.abc1234", EndpointsPublicacion.Versionar(TipoContrato.Proto, "1.0.0-dev.abc1234", proto).Version?.ToString());
        Assert.Equal("version_distinta_del_spec", EndpointsPublicacion.Versionar(TipoContrato.Proto, "2.0.0", proto).Problema?.Codigo);
        Assert.Equal("version_invalida", EndpointsPublicacion.Versionar(TipoContrato.Proto, null, proto).Problema?.Codigo);
    }

    [Fact]
    public void Con_el_prerelease_de_la_rama_la_version_es_la_del_spec_mas_el_sufijo()
    {
        var openapi = AnalizadorSpec.Analizar(TipoContrato.Openapi, SpecsDeEjemplo.OpenApiYaml);
        var proto = AnalizadorSpec.Analizar(TipoContrato.Proto, SpecsDeEjemplo.Proto);

        // HU 3.3: el workflow manda solo el prerelease de la rama; Atlas lo pega a info.version.
        Assert.Equal("1.2.0-dev.abc1234", EndpointsPublicacion.Versionar(TipoContrato.Openapi, null, "dev.abc1234", openapi).Version?.ToString());
        Assert.Equal("1.2.0-rc.g0123456", EndpointsPublicacion.Versionar(TipoContrato.Openapi, "1.2.0", "rc.g0123456", openapi).Version?.ToString());
        Assert.Equal("1.2.0", EndpointsPublicacion.Versionar(TipoContrato.Openapi, null, " ", openapi).Version?.ToString());
        Assert.Equal("version_invalida", EndpointsPublicacion.Versionar(TipoContrato.Openapi, null, "dev.0123456", openapi).Problema?.Codigo);
        Assert.Equal("version_invalida", EndpointsPublicacion.Versionar(TipoContrato.Openapi, "1.2.0-rc.1", "dev.abc1234", openapi).Problema?.Codigo);
        Assert.Equal("version_distinta_del_spec", EndpointsPublicacion.Versionar(TipoContrato.Openapi, "1.3.0", "dev.abc1234", openapi).Problema?.Codigo);
        Assert.Equal("1.0.0-dev.abc1234", EndpointsPublicacion.Versionar(TipoContrato.Proto, "1.0.0", "dev.abc1234", proto).Version?.ToString());
        Assert.Equal("version_invalida", EndpointsPublicacion.Versionar(TipoContrato.Proto, null, "dev.abc1234", proto).Problema?.Codigo);
    }

    [Fact]
    public void El_hash_es_sha256_de_los_bytes_utf8()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", AnalizadorSpec.Sha256(string.Empty));
        Assert.NotEqual(AnalizadorSpec.Sha256("a\n"), AnalizadorSpec.Sha256("a\r\n"));
    }
}
