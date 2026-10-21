# services/docs · atlas-docs (Python FastAPI)

Exportes (PDF con WeasyPrint, Word, Excel, HTML, Postman) e importadores (Excel, colecciones Postman). Trabaja desde la cola `flit.atlas`. No se expone a internet. Fases **F3** y **F4**.

## Configuración

| Variable | Uso |
|---|---|
| `ATLAS_AMBIENTE` | `local`, `dev`, `qa` o `pdn` |
| `ATLAS_DOCS_S3_ENDPOINT` | URL del S3 (MinIO o Contabo) |
| `ATLAS_DOCS_S3_ACCESS_KEY`, `ATLAS_DOCS_S3_SECRET_KEY` | Credenciales del S3 |
| `ATLAS_DOCS_S3_BUCKET` | Bucket de Atlas; `/health/ready` lo comprueba con `HeadBucket` |
| `ATLAS_DOCS_S3_REGION` | Opcional, `us-east-1` por defecto |
| `ATLAS_DOCS_S3_CREAR_BUCKET` | Opcional; solo se acepta con `ATLAS_AMBIENTE=local` |

## Comandos

```bash
uv sync
uv run ruff check . && uv run ruff format --check .
uv run pytest
uv run uvicorn --factory atlas_docs.main:crear_app --port 4035
```
