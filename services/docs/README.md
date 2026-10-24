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

## Conversión del legado ICT (HU 3.8, ATLAS-ADR-0037)

`atlas_docs.legado` toma el `swagger.json` de tsoa de cada `BackApiExternal*` y el Excel de contratos ICT (todas las
hojas) y escribe en `contracts/legado/` un OpenAPI 3.1 enriquecido por repo, el informe de inconsistencias, la
trazabilidad de cada campo del Excel y los PR propuestos para los repos legados. Ver `contracts/legado/README.md`.

```bash
uv run python -m atlas_docs.legado --excel <ruta del Excel> --repos <carpeta con los BackApiExternal*>
```

Las pruebas (`tests/legado/`) usan un swagger mínimo y un xlsx ficticio que arma la propia prueba; nunca el Excel real.
