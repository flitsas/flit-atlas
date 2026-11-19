"""PDF con WeasyPrint desde el HTML de impresión.

WeasyPrint necesita Pango (librería nativa). En la imagen y en el CI está instalada; en un Windows sin GTK no carga y
este módulo lo dice con `WeasyPrintNoDisponibleError` en lugar de romper el import del servicio.

Sin red: el `url_fetcher` rechaza cualquier recurso (la plantilla no pide ninguno; si un texto lo intentara, falla).
"""

from functools import cache
from typing import Any

from atlas_docs.exportes.documento import Documento
from atlas_docs.exportes.html import generar as generar_html


class WeasyPrintNoDisponibleError(RuntimeError):
    pass


class RecursoRemotoError(RuntimeError):
    pass


@cache
def _weasyprint() -> Any:
    try:
        import weasyprint  # noqa: PLC0415 — carga perezosa: depende de Pango
    except OSError as error:  # cffi no encuentra libpango/libgobject
        raise WeasyPrintNoDisponibleError(
            "WeasyPrint no encuentra Pango. Instala las librerías del sistema (ver services/docs/README.md)."
        ) from error
    return weasyprint


def disponible() -> bool:
    try:
        _weasyprint()
    except WeasyPrintNoDisponibleError:
        return False
    return True


def _sin_red(url: str, *args: object, **kwargs: object) -> dict[str, Any]:
    raise RecursoRemotoError(f"La exportación no carga recursos externos: {url[:80]}")


def generar(documento: Documento) -> bytes:
    weasyprint = _weasyprint()
    html = generar_html(documento, pdf=True)
    pagina = weasyprint.HTML(string=html, url_fetcher=_sin_red, base_url=None)
    # Metadatos fijos: el PDF no lleva la hora de generación (misma entrada, mismos bytes).
    salida = pagina.write_pdf(pdf_identifier=documento.hash[:32].encode("ascii"), uncompressed_pdf=False)
    if salida is None:  # pragma: no cover — write_pdf sin destino siempre devuelve bytes
        raise RuntimeError("WeasyPrint no devolvió el PDF")
    return bytes(salida)
