# services/ia · atlas-ia (Python FastAPI)

Orquestador del Asistente Atlas, ingesta RAG y servidor MCP. Toda llamada a LLM pasa por el gateway LiteLLM; el contexto de permisos lo firma `atlas-api` (ATLAS-ADR-0011). No se expone a internet. Fase **F7**.

## Configuración

| Variable | Uso |
|---|---|
| `ATLAS_AMBIENTE` | `local`, `dev`, `qa` o `pdn` |
| `ATLAS_IA_DATABASE_URL` | PostgreSQL en formato URI (`postgresql://…`); `/health/ready` exige la extensión `vector` |

## Comandos

```bash
uv sync
uv run ruff check . && uv run ruff format --check .
uv run pytest
uv run uvicorn --factory atlas_ia.main:crear_app --port 4034
```
