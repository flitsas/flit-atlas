namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>
/// Specs ficticios de prueba (OpenAPI 3.1, AsyncAPI 3.0 y proto), compartidos con <c>Flit.Atlas.Api.Tests</c>. Sin datos
/// reales: repos y paquetes <c>flitsas-demo</c>.
/// </summary>
public static class SpecsDeEjemplo
{
    public const string OpenApiYaml = """
        openapi: 3.1.0
        info:
          title: API Demo
          description: Contrato ficticio de prueba.
          version: 1.2.0
        paths:
          /tramites/{id}:
            get:
              operationId: obtenerTramite
              summary: Obtener un trámite
              tags: [tramites, public]
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: El trámite.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Tramite' }
            delete:
              operationId: eliminarTramite
              x-public: false
              responses:
                '204': { description: Eliminado. }
        webhooks:
          tramiteRadicado:
            post:
              operationId: avisarRadicado
              x-public: true
              responses:
                '200': { description: Recibido. }
        components:
          schemas:
            Tramite:
              type: object
              properties:
                id: { type: string }
        """;

    public const string AsyncApi3 = """
        asyncapi: 3.0.0
        info:
          title: Eventos Demo
          version: 2.0.0
        channels:
          radicados:
            address: tramites.radicado
            messages:
              radicado: { $ref: '#/components/messages/Radicado' }
        operations:
          publicarRadicado:
            action: send
            summary: Se radicó un trámite.
            channel: { $ref: '#/channels/radicados' }
            tags: [{ name: public }]
          escucharRadicado:
            action: receive
            channel: { $ref: '#/channels/radicados' }
        components:
          messages:
            Radicado:
              payload: { type: object }
        """;

    public const string Proto = """
        syntax = "proto3";
        package flitsas_demo.tramites.v1;
        import "google/protobuf/timestamp.proto";
        import "comun/v1/sobre.proto";

        service TramitesService {
          rpc Obtener (ObtenerRequest) returns (Tramite);
          rpc Seguir (ObtenerRequest) returns (stream Tramite);
        }
        message ObtenerRequest { string id = 1; comun.v1.Sobre sobre = 2; }
        message Tramite { string id = 1; google.protobuf.Timestamp creado = 2; }
        """;
}
