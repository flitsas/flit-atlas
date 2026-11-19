"""Revisa lo que ya está versionado en contracts/legado/ (corre en el CI, que no tiene el Excel real).

- Ningún archivo trae credenciales, correos reales, rutas locales, IP privadas ni hosts internos.
- Cada spec es OpenAPI 3.1 con versión semántica y visibilidad declarada.
- Cada campo de `trazabilidad.json` resuelve en su spec o aparece en el informe (CA de la HU 3.8).
"""

import json
import re
from pathlib import Path

import pytest
import yaml

from atlas_docs.legado.saneamiento import buscar_sensibles


def _carpeta() -> Path:
    for raiz in Path(__file__).resolve().parents:
        if (raiz / "contracts" / "legado").is_dir():
            return raiz / "contracts" / "legado"
    pytest.skip("contracts/legado/ no existe en este checkout")
    raise AssertionError  # pragma: no cover


CARPETA = _carpeta()
ARCHIVOS = sorted(a for a in CARPETA.rglob("*") if a.is_file())
SPECS = sorted(CARPETA.glob("*.yaml"))


def _resolver(documento: object, puntero: str) -> object:
    nodo = documento
    for parte in puntero.lstrip("/").split("/"):
        parte = parte.replace("~1", "/").replace("~0", "~")
        nodo = nodo[int(parte)] if isinstance(nodo, list) else nodo[parte]  # type: ignore[index]
    return nodo


def test_hay_specs_versionados() -> None:
    assert len(SPECS) >= 5


@pytest.mark.parametrize("archivo", ARCHIVOS, ids=lambda a: a.relative_to(CARPETA).as_posix())
def test_ningun_dato_sensible_sobrevive(archivo: Path) -> None:
    texto = archivo.read_text(encoding="utf-8")

    hallazgos = buscar_sensibles(texto)

    assert hallazgos == [], f"{archivo.name}: {sorted({h.tipo for h in hallazgos})}"
    # Rutas locales en general (no solo las de un perfil de usuario).
    assert not re.search(r"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?:Users|FLIT|OneDrive)", texto)


@pytest.mark.parametrize("spec", SPECS, ids=lambda a: a.name)
def test_specs_openapi_31_con_version_y_visibilidad(spec: Path) -> None:
    documento = yaml.safe_load(spec.read_text(encoding="utf-8"))

    assert documento["openapi"] == "3.1.0"
    assert re.fullmatch(r"\d+\.\d+\.\d+", documento["info"]["version"])
    assert documento["info"]["x-flit-visibilidad"] in ("compartible", "interna")
    for servidor in documento.get("servers", []):
        assert servidor["url"].startswith("https://")


def test_cada_campo_del_excel_esta_en_un_spec_o_en_el_informe() -> None:
    traza = json.loads((CARPETA / "trazabilidad.json").read_text(encoding="utf-8"))["campos"]
    informe = (CARPETA / "informe.md").read_text(encoding="utf-8")
    specs = {a.name: yaml.safe_load(a.read_text(encoding="utf-8")) for a in SPECS}

    assert traza
    for entrada in traza:
        if entrada["destino"] == "spec":
            archivo, puntero = entrada["detalle"].split("#", 1)
            _resolver(specs[archivo], puntero)
        else:
            assert entrada["destino"] == "informe"
            assert f"`{entrada['campo']}`" in informe
