# contracts/openapi

`atlas.v1.yaml` es el OpenAPI 3.1 de atlas-api, **generado desde el código** con `Microsoft.AspNetCore.OpenApi` (ATLAS-ADR-0021). No se edita a mano: `ContratoOpenApiTests` falla si el archivo no coincide con lo que generan los endpoints.

## Regenerarlo

Después de agregar o cambiar un endpoint (necesita Docker, por el fixture de las pruebas de la API):

```bash
ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests
```

En PowerShell: `$env:ATLAS_ACTUALIZAR_CONTRATOS = "1"` antes del `dotnet test` (y `Remove-Item Env:ATLAS_ACTUALIZAR_CONTRATOS` después). Versiona el `atlas.v1.yaml` resultante en el mismo PR.

## Cómo describir un endpoint

- `WithName("VerboSustantivo")`: es el `operationId`, único y en español (`ObtenerUsuarioActual`, `CerrarSesion`).
- `WithTags("<Módulo>")` (en el grupo de rutas del módulo), `WithSummary` y `WithDescription` en español.
- `Produces(...)` para los códigos que no se deducen del tipo de retorno (204, 302, 404…). El 401 de los endpoints con `RequireAuthorization` y el 403 de los que cambian estado (falta `X-Atlas-Csrf`) los agrega `ContratoOpenApi` solo, con su requisito de seguridad.
- `[Description]` en los records de respuesta y en los parámetros.
- Lo que solo existe en local o dev (p. ej. el proveedor simulado) lleva `ExcludeFromDescription()`.

## En tiempo de ejecución

`GET /openapi/v1.yaml` solo existe en local y dev. En qa y pdn no se sirve: el contrato se publica por el módulo de contratos (F3).
