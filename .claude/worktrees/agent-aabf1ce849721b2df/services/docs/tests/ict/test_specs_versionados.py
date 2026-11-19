"""Revisa lo que ya está versionado en contracts/ict/ (corre en el CI, que no tiene el Excel ni el repo flit).

- Ningún archivo trae credenciales, correos reales, rutas locales, IP privadas ni hosts internos.
- El spec y la propuesta para `flit` son OpenAPI 3.1 con versión semántica, visibilidad declarada y una sola URL
  base por ambiente.
- Cada campo de `trazabilidad.json` resuelve en el spec o aparece en el informe (CA de la HU 3.8).
"""

import json
import re
from pathlib import Path

import pytest
import yaml

from atlas_docs.ict import mapeo
from atlas_docs.ict.saneamiento import buscar_sensibles


def _carpeta() -> Path:
    for raiz in Path(__file__).resolve().parents:
        if (raiz / "contracts" / "ict").is_dir():
            return raiz / "contracts" / "ict"
    pytest.skip("contracts/ict/ no existe en este checkout")
    raise AssertionError  # pragma: no cover


CARPETA = _carpeta()
ARCHIVOS = sorted(a for a in CARPETA.rglob("*") if a.is_file())
SPECS = [CARPETA / mapeo.ARCHIVO_SPEC, CARPETA / mapeo.ARCHIVO_PROPUESTA]


def _resolver(documento: object, puntero: str) -> object:
    nodo = documento
    for parte in puntero.lstrip("/").split("/"):
        parte = parte.replace("~1", "/").replace("~0", "~")
        nodo = nodo[int(parte)] if isinstance(nodo, list) else nodo[parte]  # type: ignore[index]
    return nodo


def test_hay_spec_y_propuesta_versionados() -> None:
    assert all(spec.is_file() for spec in SPECS)
    assert not (CARPETA.parent / "legado").exists()


@pytest.mark.parametrize("archivo", ARCHIVOS, ids=lambda a: a.relative_to(CARPETA).as_posix())
def test_ningun_dato_sensible_sobrevive(archivo: Path) -> None:
    texto = archivo.read_text(encoding="utf-8")

    hallazgos = buscar_sensibles(texto)

    assert hallazgos == [], f"{archivo.name}: {sorted({h.tipo for h in hallazgos})}"
    # Rutas locales en general (no solo las de un perfil de usuario).
    assert not re.search(r"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?:Users|FLIT|OneDrive)", texto)


@pytest.mark.parametrize("spec", SPECS, ids=lambda a: a.relative_to(CARPETA).as_posix())
def test_openapi_31_con_version_visibilidad_y_url_base_unica(spec: Path) -> None:
    documento = yaml.safe_load(spec.read_text(encoding="utf-8"))

    assert documento["openapi"] == "3.1.0"
    assert re.fullmatch(r"\d+\.\d+\.\d+", documento["info"]["version"])
    assert documento["info"]["x-flit-visibilidad"] in ("compartible", "interna")
    ambientes = [servidor["x-flit-ambiente"] for servidor in documento["servers"]]
    assert len(ambientes) == len(set(ambientes)), "una sola URL base por ambiente"
    for servidor in documento["servers"]:
        assert servidor["url"] == "https://{host}"
        assert re.match(mapeo.HOSTS_PUBLICOS, servidor["variables"]["host"]["default"])


def test_la_propuesta_es_el_mismo_spec() -> None:
    spec, propuesta = (yaml.safe_load(a.read_text(encoding="utf-8")) for a in SPECS)

    assert spec == propuesta


def test_cada_campo_del_excel_esta_en_el_spec_o_en_el_informe() -> None:
    traza = json.loads((CARPETA / "trazabilidad.json").read_text(encoding="utf-8"))["campos"]
    informe = (CARPETA / "informe.md").read_text(encoding="utf-8")
    spec = yaml.safe_load((CARPETA / mapeo.ARCHIVO_SPEC).read_text(encoding="utf-8"))

    assert traza
    for entrada in traza:
        if entrada["destino"] == "spec":
            archivo, puntero = entrada["detalle"].split("#", 1)
            assert archivo == mapeo.ARCHIVO_SPEC
            _resolver(spec, puntero)
        else:
            assert entrada["destino"] == "informe"
            assert f"`{entrada['campo']}`" in informe
