"""CLI de la conversión del legado ICT.

    uv run python -m atlas_docs.legado --excel <Contratos_Integraciones_*.xlsx> --repos <carpeta> [--salida <carpeta>]

`--repos` es la carpeta que contiene los `BackApiExternal*` (solo se leen su `swagger.json` y su `tsoa.json`).
`--salida` es `contracts/legado/` del repo por defecto. Ninguna ruta local llega a los archivos generados.
"""

import argparse
import json
import sys
from pathlib import Path

import yaml

from atlas_docs.legado import informe, mapeo, propuestas
from atlas_docs.legado.conversion import convertir
from atlas_docs.legado.excel import leer_libro
from atlas_docs.legado.saneamiento import buscar_sensibles


class _Dumper(yaml.SafeDumper):
    """YAML estable y legible: sin anclas, textos largos en bloque."""

    def ignore_aliases(self, data) -> bool:  # noqa: ARG002
        return True


def _texto(dumper: yaml.SafeDumper, valor: str):
    estilo = "|" if "\n" in valor else None
    return dumper.represent_scalar("tag:yaml.org,2002:str", valor, style=estilo)


_Dumper.add_representer(str, _texto)


def a_yaml(datos: dict) -> str:
    return yaml.dump(datos, Dumper=_Dumper, allow_unicode=True, sort_keys=False, width=120)


def _raiz_repo() -> Path:
    for carpeta in Path(__file__).resolve().parents:
        if (carpeta / "contracts").is_dir() and (carpeta / "services").is_dir():
            return carpeta
    raise SystemExit("no se encontró la raíz del repo flit-atlas; usa --salida")


def ejecutar(excel: Path | None, repos: Path, salida: Path) -> dict[str, str]:
    """Convierte y escribe todo. Devuelve {archivo relativo: contenido} de lo escrito."""
    swaggers, tsoas = {}, {}
    for api in mapeo.APIS:
        carpeta = repos / api.repo
        if (carpeta / "swagger.json").is_file():
            swaggers[api.repo] = json.loads((carpeta / "swagger.json").read_text(encoding="utf-8"))
            if (carpeta / "tsoa.json").is_file():
                tsoas[api.repo] = json.loads((carpeta / "tsoa.json").read_text(encoding="utf-8"))
    if not swaggers:
        raise SystemExit(f"no hay ningún BackApiExternal*/swagger.json en {repos.name}")
    libro = leer_libro(excel) if excel else None
    resultado = convertir(libro, swaggers)

    archivos: dict[str, str] = {}
    for codigo, spec in resultado.specs.items():
        archivos[f"{codigo}.yaml"] = a_yaml(spec)
    archivos["informe.md"] = informe.generar(resultado.registro, libro.archivo if libro else None)
    archivos["trazabilidad.json"] = propuestas.escribir_json(
        {"excel": libro.archivo if libro else None, "campos": resultado.registro.traza}
    )
    versiones = []
    for api in mapeo.APIS:
        if api.codigo not in resultado.specs or api.repo not in tsoas:
            continue
        version = resultado.specs[api.codigo]["info"]["version"]
        versiones.append((api, version))
        base = f"propuestas/{api.repo}"
        archivos[f"{base}/tsoa.json"] = propuestas.escribir_json(
            propuestas.tsoa_corregido(tsoas[api.repo], api, version)
        )
        archivos[f"{base}/.github/workflows/atlas-publicar.yml"] = propuestas.workflow(api)
    archivos["propuestas/README.md"] = propuestas.leeme(versiones)

    sensibles = {nombre: buscar_sensibles(texto) for nombre, texto in archivos.items()}
    if any(sensibles.values()):
        detalle = {n: sorted({h.tipo for h in hs}) for n, hs in sensibles.items() if hs}
        raise SystemExit(f"la salida conserva datos sensibles y no se escribió: {detalle}")
    for nombre, texto in archivos.items():
        destino = salida / nombre
        destino.parent.mkdir(parents=True, exist_ok=True)
        destino.write_text(texto, encoding="utf-8", newline="\n")
    return archivos


def main(argumentos: list[str] | None = None) -> int:
    lector = argparse.ArgumentParser(prog="python -m atlas_docs.legado", description=__doc__.split("\n")[0])
    lector.add_argument("--excel", type=Path, help="Excel de contratos ICT (no está en git)")
    lector.add_argument("--repos", type=Path, required=True, help="carpeta con los BackApiExternal*")
    lector.add_argument("--salida", type=Path, help="carpeta de salida (por defecto contracts/legado/)")
    opciones = lector.parse_args(argumentos)
    salida = opciones.salida or _raiz_repo() / "contracts" / "legado"
    archivos = ejecutar(opciones.excel, opciones.repos, salida)
    print(f"{len(archivos)} archivos escritos en {salida.name}/")  # noqa: T201
    return 0


if __name__ == "__main__":
    sys.exit(main())
