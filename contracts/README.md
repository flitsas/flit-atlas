# contracts · Contratos propios de Atlas

| Carpeta | Contenido |
|---|---|
| `openapi/` | `atlas.v1.yaml`, API REST pública de Atlas (OpenAPI 3.1) |
| `asyncapi/` | `atlas-events.v1.yaml`, eventos del exchange `flit.atlas` |
| `proto/atlas/*/v1/` | gRPC interno entre `atlas-api` y los servicios Go y Python |

Atlas valida sus propios contratos en CI (vacuum y oasdiff, ATLAS-ADR-0010). Fase **F1**.
