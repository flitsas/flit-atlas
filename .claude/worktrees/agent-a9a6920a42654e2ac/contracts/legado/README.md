# contracts/legado · Contratos legados ICT (OpenAPI 3.1)

Specs de las APIs externas legadas (`BackApiExternal*`) que hoy usan los clientes, convertidos desde el swagger de
tsoa de cada repo y enriquecidos con el Excel de contratos ICT (HU 3.8, ATLAS-ADR-0037, prompt maestro §8.4).
**Todo lo de esta carpeta, salvo este README, es generado**: no se edita a mano.

| Archivo | Contenido |
|---|---|
| `<contrato>.yaml` | Un OpenAPI 3.1 por repo (`external-auth`, `external-transact`, …). `x-public` marca lo compartible; `x-internal`, lo que la poda de Atlas quita |
| `informe.md` | Inconsistencias para el líder técnico, campos del Excel que no quedaron en un spec, changelog inicial y saneamiento |
| `trazabilidad.json` | Dónde quedó cada campo del Excel: archivo y puntero JSON del spec, o el motivo por el que solo está en el informe |
| `propuestas/` | PR propuesto para cada repo legado (`tsoa.json` corregido y workflow de publicación). No se abren solos |

## Regenerar

El Excel no está en git (ver `docs/atlas/insumos/README.md`). Con una copia local del Excel y los repos legados
clonados en una misma carpeta:

```bash
cd services/docs
uv run python -m atlas_docs.legado --excel <ruta del Excel> --repos <carpeta con los BackApiExternal*>
```

La conversión falla y no escribe nada si algún archivo conserva un token, un correo que no sea `@ejemplo.com`, una
ruta local, una IP privada o un host interno. Antes de commitear, revisa el diff a mano (gitleaks lo revisa también).

## Validación en CI

- Job `contratos`: vacuum con `contracts/vacuum/openapi-compartible.yaml` (las reglas de lo compartible, incluida la
  de ejemplos obligatorios) sobre cada `*.yaml` de esta carpeta.
- Job `python` (`services/docs`): `tests/legado/test_specs_versionados.py` recorre esta carpeta y falla si encuentra
  datos sensibles o un campo de `trazabilidad.json` que no resuelve en su spec ni aparece en el informe.
