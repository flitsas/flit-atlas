# services/api/tests

Pruebas unitarias, de integración con PostgreSQL real (Testcontainers), de contrato contra `contracts/openapi/atlas.v1.yaml` y de autorización parametrizadas por rol. Fase **F1**.

`ContratoOpenApiTests` compara el OpenAPI generado con `contracts/openapi/atlas.v1.yaml` y `ContratoAsyncApiTests` compara el catálogo de `TipoEvento` con `contracts/asyncapi/atlas-events.v1.yaml` (ATLAS-ADR-0021).
