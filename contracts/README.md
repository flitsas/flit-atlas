# contracts · Contratos propios de Atlas

| Carpeta | Contenido |
|---|---|
| `openapi/` | `atlas.v1.yaml`, API REST de atlas-api (OpenAPI 3.1), **generado desde el código** |
| `asyncapi/` | `atlas-events.v1.yaml`, eventos del exchange `flit.atlas` (AsyncAPI 3.0), escrito a mano y atado al código por pruebas |
| `vacuum/` | Rulesets de vacuum (`openapi.yaml` y `asyncapi.yaml`) |
| `proto/atlas/*/v1/` | gRPC interno entre `atlas-api` y los servicios Go y Python |

Atlas valida sus propios contratos en CI (ATLAS-ADR-0010 y ATLAS-ADR-0021): el job `contratos` de `ci.yml` corre vacuum sobre los dos archivos y, en los PR, oasdiff contra la rama base (solo advierte en F1). Que el OpenAPI coincida con el código y que el AsyncAPI tenga los eventos del código lo comprueban las pruebas de `services/api/tests/Flit.Atlas.Api.Tests` (job `dotnet`).

## Lint local

Con vacuum v0.32.0 (el binario del release, verificado con su `checksums.txt`):

```bash
vacuum lint -d -r contracts/vacuum/openapi.yaml contracts/openapi/atlas.v1.yaml
vacuum lint -d -r contracts/vacuum/asyncapi.yaml contracts/asyncapi/atlas-events.v1.yaml
```
