"""CLI del enriquecimiento del contrato de ICT de FLIT 2 con el Excel.

    uv run python -m atlas_docs.ict --excel <Contratos_Integraciones_*.xlsx> --base-flit <repo flit> [--salida <dir>]

`--base-flit` es la carpeta del repo `flit` (FLIT 2): solo se leen `contracts/openapi/core-ict.v1.yaml` y la
configuración del Gateway YARP. `--salida` es `contracts/ict/` del repo por defecto. Ninguna ruta local llega a los
archivos generados.
"""

import argparse
import sys
from pathlib import Path

import yaml

from atlas_docs.ict import informe, mapeo, propuesta
from atlas_docs.ict.base import leer_base
from atlas_docs.ict.conversion import convertir
from atlas_docs.ict.excel import leer_libro
from atlas_docs.ict.saneamiento import buscar_sensibles


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


def ejecutar(excel: Path | None, base_flit: Path, salida: Path) -> dict[str, str]:
    """Enriquece y escribe todo. Devuelve {archivo relativo: contenido} de lo escrito."""
    base = leer_base(base_flit)
    libro = leer_libro(excel) if excel else None
    resultado = convertir(libro, base)

    spec = a_yaml(resultado.spec)
    archivos: dict[str, str] = {
        mapeo.ARCHIVO_SPEC: spec,
        "informe.md": informe.generar(resultado.registro, libro.archivo if libro else None),
        "trazabilidad.json": propuesta.escribir_json(
            {"excel": libro.archivo if libro else None, "spec": mapeo.ARCHIVO_SPEC, "campos": resultado.registro.traza}
        ),
        mapeo.ARCHIVO_PROPUESTA: propuesta.contrato_para_flit(spec),
        "propuesta-flit/README.md": propuesta.leeme(resultado.registro.resumen),
    }

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
    lector = argparse.ArgumentParser(prog="python -m atlas_docs.ict", description=__doc__.split("\n")[0])
    lector.add_argument("--excel", type=Path, help="Excel de contratos ICT (no está en git)")
    lector.add_argument("--base-flit", type=Path, required=True, help="carpeta del repo flit (FLIT 2)")
    lector.add_argument("--salida", type=Path, help="carpeta de salida (por defecto contracts/ict/)")
    opciones = lector.parse_args(argumentos)
    salida = opciones.salida or _raiz_repo() / "contracts" / "ict"
    archivos = ejecutar(opciones.excel, opciones.base_flit, salida)
    print(f"{len(archivos)} archivos escritos en {salida.name}/")  # noqa: T201
    return 0


if __name__ == "__main__":
    sys.exit(main())
