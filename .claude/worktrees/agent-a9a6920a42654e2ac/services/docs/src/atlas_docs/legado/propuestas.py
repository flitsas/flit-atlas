"""PR propuesto para cada repo legado: `tsoa.json` corregido y el workflow que publica en Atlas.

Se escriben como archivos dentro de flit-atlas (`contracts/legado/propuestas/<repo>/`). Abrir el PR en cada repo
legado lo decide el líder técnico (prompt maestro §1, regla 4c).
"""

import copy
import json

from atlas_docs.legado.mapeo import Api

# Nombre previsto del workflow reutilizable de la HU 3.3. [por verificar] la versión que fije esa HU.
WORKFLOW_REUTILIZABLE = "flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@v1"


def tsoa_corregido(original: dict, api: Api, version: str) -> dict:
    corregido = copy.deepcopy(original)
    spec = corregido.setdefault("spec", {})
    spec["name"] = f"{api.titulo} ({api.repo})"
    spec["version"] = version
    spec["description"] = api.descripcion
    spec["contact"] = {"name": "FLIT S.A.S."}
    spec.setdefault("specVersion", 3)
    return corregido


def workflow(api: Api) -> str:
    return f"""# Propuesta (HU 3.8, ATLAS-ADR-0037): publica el contrato de {api.repo} en Flit Atlas.
# Copiar a .github/workflows/atlas-publicar.yml del repo {api.repo}, junto con el spec enriquecido
# (contracts/legado/{api.codigo}.yaml de flit-atlas → contracts/openapi/{api.codigo}.yaml de este repo).
# El workflow reutilizable lo entrega la HU 3.3: valida con vacuum, compara con oasdiff contra la versión de pdn y
# publica con el token OIDC de GitHub (ATLAS-ADR-0009). [por verificar] versión fijada del workflow y variable
# ATLAS_URL de la organización.
name: atlas-publicar

on:
  push:
    branches: [develop, staging, release]
  pull_request:
    branches: [develop, staging, release]

permissions:
  contents: read

jobs:
  publicar:
    permissions:
      contents: read
      id-token: write
    uses: {WORKFLOW_REUTILIZABLE}
    with:
      aplicativo: {api.aplicativo}
      contrato: {api.codigo}
      tipo: openapi
      ruta: contracts/openapi/{api.codigo}.yaml
      atlas_url: ${{{{ vars.ATLAS_URL }}}}
"""


def leeme(apis: list[tuple[Api, str]]) -> str:
    filas = "\n".join(
        f"| `{api.repo}` | `{api.codigo}` | {version} | [`tsoa.json`]({api.repo}/tsoa.json) · "
        f"[`atlas-publicar.yml`]({api.repo}/.github/workflows/atlas-publicar.yml) |"
        for api, version in apis
    )
    return f"""# PR propuestos para los repos legados

Generados por `python -m atlas_docs.legado` (HU 3.8, ATLAS-ADR-0037). **No se abren solos**: abrir un PR en un repo
legado requiere el permiso del líder técnico.

| Repo | Contrato | Versión | Archivos |
|---|---|---|---|
{filas}

## Qué cambia en cada repo

1. `tsoa.json`: título del servicio en lugar de `api_express_typescript_boilerplate`, versión semántica tomada del
   Excel de contratos ICT, descripción y un contacto que no es una persona. El swagger que genera tsoa queda igual en
   lo demás.
2. `contracts/openapi/<contrato>.yaml`: copia del spec enriquecido de `contracts/legado/` de flit-atlas.
3. `.github/workflows/atlas-publicar.yml`: llama al workflow reutilizable de la HU 3.3 con el aplicativo del catálogo
   y la ruta del spec.

## Pendientes

- [por verificar] Nombre y versión fijada del workflow reutilizable (HU 3.3) y la variable `ATLAS_URL`.
- [por verificar] Que los repos privados de la organización puedan usar workflows reutilizables de `flit-atlas`
  (ajuste de acceso de Actions en el repo `flit-atlas`).
- [por verificar] Si el líder prefiere un repo de contratos legados, el workflow vive allí y no en cada repo.
"""


def escribir_json(datos: dict) -> str:
    return json.dumps(datos, ensure_ascii=False, indent=2) + "\n"
