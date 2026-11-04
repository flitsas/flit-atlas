"""Apoyo de las pruebas de exportación (HU 4.5): paquetes con el contrato ICT enriquecido y el de Atlas.

Los specs salen de `contracts/` del propio repo; los metadatos y las guías son ficticios (Organismo Demo).

El PDF necesita Pango. En un Windows sin GTK las pruebas que lo usan se saltan; con `ATLAS_DOCS_EXIGIR_PDF=1` (lo
pone el CI) fallan en lugar de saltarse, para que en el CI corran sí o sí.
"""

import os
from pathlib import Path

import pytest

from atlas_docs.exportes import Formato, PaqueteExportar
from atlas_docs.exportes import pdf as modulo_pdf

ORGANIZACION = "Organismo Demo"
FECHA = "2026-10-24"
MARCA = "Organismo Demo · 24/10/2026"

GUIA = """# Primeros pasos

1. Pide tus credenciales al equipo de integraciones.
2. Autentícate con `POST /api/v1/auth/login` y guarda el token.

Más detalles en la [guía pública](https://ejemplo.com/guia) y en [la sección de cambios](#changelog).

![diagrama](https://ejemplo.com/diagrama.png)

<script>alert("no")</script>

| Ambiente | Uso |
|---|---|
| QA | Pruebas de integración |
"""


def _contratos() -> Path:
    for raiz in Path(__file__).resolve().parents:
        if (raiz / "contracts" / "ict").is_dir():
            return raiz / "contracts"
    pytest.skip("contracts/ no existe en este checkout")
    raise AssertionError  # pragma: no cover


CONTRATOS = _contratos()
ICT = CONTRATOS / "ict" / "core-ict.enriquecido.v1.yaml"
ATLAS = CONTRATOS / "openapi" / "atlas.v1.yaml"
EVENTOS = CONTRATOS / "asyncapi" / "atlas-events.v1.yaml"

PDF_DISPONIBLE = modulo_pdf.disponible()
EXIGIR_PDF = os.environ.get("ATLAS_DOCS_EXIGIR_PDF") == "1"


def requiere_pdf() -> None:
    if PDF_DISPONIBLE:
        return
    if EXIGIR_PDF:
        pytest.fail("ATLAS_DOCS_EXIGIR_PDF=1 y WeasyPrint no carga: faltan las librerías de Pango")
    pytest.skip("WeasyPrint no encuentra Pango en esta máquina (en el CI corre)")


def paquete(*specs: tuple[str, str, Path], organizacion: str = ORGANIZACION) -> PaqueteExportar:
    return PaqueteExportar.model_validate(
        {
            "metadatos": {
                "paquete": "Integración ICT",
                "revision": 3,
                "organizacion": organizacion,
                "fecha": FECHA,
                "changelog": [
                    {"revision": "3", "fecha": "2026-10-20", "cambio": "Se agrega la anulación de trámites."},
                    {"revision": "1", "fecha": "2026-09-01", "cambio": "Primera revisión del paquete."},
                ],
            },
            "specs": [{"contrato": c, "version": v, "contenido": r.read_text(encoding="utf-8")} for c, v, r in specs],
            "guias": [{"titulo": "Primeros pasos", "markdown": GUIA}],
        }
    )


def formatos_disponibles() -> set[Formato]:
    return set(Formato) if PDF_DISPONIBLE else set(Formato) - {Formato.PDF}
