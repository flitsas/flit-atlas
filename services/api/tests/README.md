# services/api/tests

Pruebas unitarias, de integración con PostgreSQL real (Testcontainers), de contrato contra `contracts/openapi/atlas.v1.yaml` y de autorización parametrizadas por rol. Fase **F1**.

`ContratoOpenApiTests` compara el OpenAPI generado con `contracts/openapi/atlas.v1.yaml` y `ContratoAsyncApiTests` compara el catálogo de `TipoEvento` con `contracts/asyncapi/atlas-events.v1.yaml` (ATLAS-ADR-0021).

`AutorizacionTests` recorre todos los endpoints de `EndpointDataSource` con cada rol del §7 y falla si uno no declara permiso (fuera de las listas de públicos y de solo sesión); `MatrizPermisosTests` compara `docs/permisos/matriz-rol-permiso.md` con el código; `AislamientoTests` (Plataforma) prueba el predicado obligatorio y RLS con el usuario `flit_atlas` (ATLAS-ADR-0023).
