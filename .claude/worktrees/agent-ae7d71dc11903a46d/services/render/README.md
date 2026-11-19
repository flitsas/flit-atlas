# services/render · atlas-render (Go)

Servicio interno sin estado: render de D2 a SVG/PNG con sanitización, lint de OpenAPI con vacuum y diff con oasdiff (ATLAS-ADR-0005, ATLAS-ADR-0010). No se expone a internet. Fases **F3** y **F6**; en la HU 1.1 solo expone las sondas de salud.

| Variable | Uso |
|---|---|
| `ATLAS_RENDER_PORT` | Puerto HTTP interno (4033 / 5033 / 6033) |

```bash
go vet ./... && go test ./...
ATLAS_RENDER_PORT=4033 go run ./cmd/atlas-render
```
