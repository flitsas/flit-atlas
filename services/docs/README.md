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

## Exportaciones de paquetes (HU 4.5, ATLAS-ADR-0042)

`atlas_docs.exportes.exportar(paquete, formatos)` es una función pura: recibe el paquete ya podado por atlas-api
(metadatos, specs OpenAPI o AsyncAPI como texto y guías en Markdown) y devuelve en memoria OpenAPI YAML y JSON,
colección Postman v2.1, HTML autocontenido, PDF, Word, Excel (con el formato del Excel de contratos) y un ZIP con
`LEEME.md` y `manifiesto.json`. Sin red ni reloj: la misma entrada da los mismos bytes y el mismo hash.

Endpoint interno (solo lo llama atlas-api):

```bash
# Responde el ZIP (cabecera X-Atlas-Hash-Contenido)
curl -X POST "http://localhost:4035/exportes?formatos=html&formatos=excel" -H "Content-Type: application/json" -d @paquete.json
# Deja cada archivo en el S3 bajo exportaciones/<exportacionId>/ y responde las claves
curl -X POST "http://localhost:4035/exportes?exportacionId=<uuid>" -H "Content-Type: application/json" -d @paquete.json
```

El PDF usa WeasyPrint, que necesita Pango. En Debian o Ubuntu:
`apt-get install libpango-1.0-0 libpangoft2-1.0-0 libharfbuzz-subset0 fonts-dejavu-core`. En Windows sin GTK el
PDF no carga: el endpoint responde 503 para ese formato y las pruebas del PDF se saltan (con
`ATLAS_DOCS_EXIGIR_PDF=1`, como en el CI, fallan). Para correrlas con Pango en local:

```bash
docker build -f services/docs/Dockerfile --target pruebas -t atlas-docs-pruebas . && docker run --rm atlas-docs-pruebas
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
