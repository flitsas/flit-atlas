# services/api/tests

Pruebas unitarias, de integración con PostgreSQL real (Testcontainers), de contrato contra `contracts/openapi/atlas.v1.yaml` y de autorización parametrizadas por rol. Fase **F1**.

`ContratoOpenApiTests` compara el OpenAPI generado con `contracts/openapi/atlas.v1.yaml` y `ContratoAsyncApiTests` compara el catálogo de `TipoEvento` con `contracts/asyncapi/atlas-events.v1.yaml` (ATLAS-ADR-0021).

`AutorizacionTests` recorre todos los endpoints de `EndpointDataSource` con cada rol del §7 y falla si uno no declara permiso (fuera de las listas de públicos y de solo sesión); `MatrizPermisosTests` compara `docs/permisos/matriz-rol-permiso.md` con el código; `AislamientoTests` (Plataforma) prueba el predicado obligatorio y RLS con el usuario `flit_atlas` (ATLAS-ADR-0023).

`ArquitecturaModulosTests` hace cumplir el contrato de módulo (ATLAS-ADR-0026): cada `Flit.Atlas.Modulos.*` solo depende de `Flit.Atlas.Plataforma`, solo `Flit.Atlas.Composicion` lo referencia, sus tablas llevan su prefijo y toda tabla con `organizacion_id` tiene RLS forzada. `ModuloHolaTests` (API) prueba la bandera, los permisos y el aislamiento del módulo de ejemplo; `Flit.Atlas.Modulos.Hola.Tests`, su migración y RLS con `flit_atlas`.
