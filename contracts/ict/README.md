# contracts/ict · Contrato de ICT de FLIT 2 enriquecido (OpenAPI 3.1)

La API de Integración con Terceros (ICT) que usan los clientes es hoy `services/core-ict` del repo `flit` (FLIT 2),
que reemplazó a los `BackApiExternal*` de FLIT 1 y recibe todo por una sola URL base por ambiente. Esta carpeta trae
su contrato (`contracts/openapi/core-ict.v1.yaml` de `flit`) enriquecido con el Excel de contratos ICT (HU 3.8,
ATLAS-ADR-0037, prompt maestro §8.4). **Todo lo de esta carpeta, salvo este README, es generado**: no se edita a mano.

| Archivo | Contenido |
|---|---|
| `core-ict.enriquecido.v1.yaml` | El OpenAPI 3.1 enriquecido. `x-public` marca lo compartible; `x-internal`, lo que la poda de Atlas quita |
| `informe.md` | Inconsistencias para el líder técnico, hojas y campos del Excel sin equivalente en core-ict, changelog y saneamiento |
| `trazabilidad.json` | Dónde quedó cada campo del Excel: puntero JSON en el spec, o el motivo por el que solo está en el informe |
| `propuesta-flit/` | El contrato listo para reemplazar a `core-ict.v1.yaml` en `flit` y su README. El PR va en la ola 3 (HU 3.10) |

## Regenerar

El Excel no está en git (ver `docs/atlas/insumos/README.md`). Con una copia local del Excel y el repo `flit` clonado
(solo se leen su contrato de core-ict y la configuración del Gateway):

```bash
cd services/docs
uv run python -m atlas_docs.ict --excel <ruta del Excel> --base-flit <ruta del repo flit>
```

El script falla y no escribe nada si algún archivo conserva un token, un correo que no sea `@ejemplo.com`, una ruta
local, una IP privada o un host interno. Antes de commitear, revisa el diff a mano (gitleaks lo revisa también).

## Validación en CI

- Job `contratos`: vacuum con `contracts/vacuum/openapi-compartible.yaml` (las reglas de lo compartible, incluida la
  de ejemplos obligatorios) sobre el spec enriquecido y la propuesta para `flit`.
- Job `python` (`services/docs`): `tests/ict/test_specs_versionados.py` recorre esta carpeta y falla si encuentra
  datos sensibles, más de una URL base por ambiente o un campo de `trazabilidad.json` que no resuelve en el spec ni
  aparece en el informe.
