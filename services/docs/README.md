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

## Contrato de ICT de FLIT 2 enriquecido con el Excel (HU 3.8, ATLAS-ADR-0037)

`atlas_docs.ict` toma `contracts/openapi/core-ict.v1.yaml` del repo `flit` (FLIT 2) y los hosts públicos que su
Gateway YARP enruta a core-ict, lo enriquece con el Excel de contratos ICT (todas las hojas) y escribe en
`contracts/ict/` un solo OpenAPI 3.1 con una URL base por ambiente, el informe de inconsistencias, la trazabilidad de
cada campo del Excel y la propuesta para el repo `flit`. Ver `contracts/ict/README.md`.

```bash
uv run python -m atlas_docs.ict --excel <ruta del Excel> --base-flit <ruta del repo flit>
```

Las pruebas (`tests/ict/`) usan un repo `flit` mínimo (contrato de core-ict y Gateway) y un xlsx ficticio que arma
la propia prueba; nunca el Excel real.
