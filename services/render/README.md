# services/render · atlas-render (Go)

Servicio interno sin estado: render de D2 a SVG/PNG con sanitización, lint de OpenAPI con vacuum y diff con oasdiff (ATLAS-ADR-0005, ATLAS-ADR-0010, ATLAS-ADR-0036). No se expone a internet. Fases **F3** y **F6**. Hoy expone las sondas de salud, las métricas y el diff de contratos (HU 3.5).

| Variable | Uso |
|---|---|
| `ATLAS_RENDER_PORT` | Puerto HTTP interno (4033 / 5033 / 6033). Obligatoria |
| `ATLAS_RENDER_DIFF_MAX_BYTES` | Tamaño máximo del cuerpo del diff, las dos specs juntas (por defecto 8 MiB) |
| `ATLAS_RENDER_DIFF_TIEMPO` | Tiempo máximo de un diff, duración de Go (por defecto `15s`) |
| `ATLAS_RENDER_DIFF_CONCURRENCIA` | Diffs calculados a la vez (por defecto, los núcleos) |
| `ATLAS_RENDER_DIFF_CACHE_ENTRADAS` | Tope de la caché LRU en memoria (por defecto 256; `0` la apaga) |
| `ATLAS_RENDER_DIFF_CACHE_MAX_BYTES` | Tope de bytes de la caché (por defecto 64 MiB) |

```bash
go vet ./... && go test ./...
ATLAS_RENDER_PORT=4033 go run ./cmd/atlas-render
```

## Endpoints

| Método y ruta | Uso |
|---|---|
| `GET /health/live` · `GET /health/ready` | Sondas |
| `GET /metrics` | Métricas Prometheus (`atlas_render_diff_*`, Go y proceso) |
| `POST /internal/v1/contratos/diff` | Diff entre dos specs (lo llama `atlas-api` con `IRenderDiff`) |

### Diff de contratos

Entrada (`application/json`):

```json
{ "tipo": "openapi", "base": "<spec completa, YAML o JSON>", "revision": "<spec completa>", "idioma": "es" }
```

Salida: `cambios` (como `oasdiff changelog`), `breaking` (como `oasdiff breaking`: nivel `error` o `warning` y id de regla en `regla`), `resumen`, `changelog` en Markdown, `hashBase` y `hashRevision` (SHA-256 de cada spec), `versionBase`, `versionRevision` y `motor`. La cabecera `X-Atlas-Cache` dice `acierto` o `fallo`. Errores en `application/problem+json`: 400, 413, 415, 422 (spec inválida, con `lado`; o «diff no soportado para este tipo» para `asyncapi` y `proto`), 503 y 504. Detalle en ATLAS-ADR-0036.

oasdiff se usa como librería (versión de `go.mod`, hoy v1.33.0) con la configuración por defecto de la CLI, salvo que **no** resuelve `$ref` externas.

## Comparar con la CLI de oasdiff

`go test ./internal/diferencias/` compila la CLI desde el mismo módulo de `go.mod` (directiva `tool`) y compara su salida JSON con la del endpoint sobre las fixtures de `testdata/diff/openapi/`. Para usarla a mano:

```bash
go tool oasdiff breaking testdata/diff/openapi/base.yaml testdata/diff/openapi/endpoint-eliminado.yaml --lang es
go tool oasdiff changelog testdata/diff/openapi/base.yaml testdata/diff/openapi/cambio-tipo.yaml --lang es -f json
```

Para comparar contra un binario publicado (por ejemplo, el de la release de GitHub que instala el job de contratos del CI), descárgalo, verifica su SHA-256 y pasa su ruta; la prueba exige que su versión sea la de `go.mod`:

```bash
OASDIFF_CLI=/ruta/a/oasdiff go test -count=1 -run TestEndpointCoincideConLaCLI -v ./internal/diferencias/
```

Con `ATLAS_RENDER_SIN_CLI=1` la prueba no compila la CLI y compara solo contra las funciones públicas de `checker` que usa la CLI.
