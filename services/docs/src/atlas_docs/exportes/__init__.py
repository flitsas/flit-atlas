"""Exportaciones de paquetes de contratos (HU 4.5, ATLAS-ADR-0042).

`exportar(paquete)` es una función pura: recibe el paquete ya podado por atlas-api (metadatos, specs OpenAPI o AsyncAPI
como texto y guías en Markdown ya resueltas) y devuelve los archivos en memoria. No usa red ni reloj: la misma
entrada da los mismos bytes y el mismo hash.

Formatos: OpenAPI (YAML y JSON), colección Postman v2.1, HTML autocontenido, PDF (WeasyPrint), Word (python-docx),
Excel (openpyxl, con el formato del Excel de contratos) y un ZIP con todo, un `LEEME.md` y un manifiesto.
"""

from atlas_docs.exportes.exportar import ResultadoExportacion, exportar
from atlas_docs.exportes.modelo import Formato, PaqueteExportar

__all__ = ["Formato", "PaqueteExportar", "ResultadoExportacion", "exportar"]
