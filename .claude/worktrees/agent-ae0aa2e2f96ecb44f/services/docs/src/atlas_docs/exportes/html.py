"""HTML autocontenido (Jinja2) y su variante para imprimir en PDF.

Autoescape siempre activo: todo lo que llega del spec, de las guías o de los metadatos se escapa. El Markdown pasa
por `markdown.a_html` (HTML crudo escapado, sin enlaces ni imágenes remotas). CSS embebido y sin scripts.
"""

from functools import cache
from importlib.resources import files

from jinja2 import Environment, StrictUndefined
from markupsafe import Markup, escape

from atlas_docs.exportes.contenido import ETIQUETAS_OBLIGATORIEDAD, SIGLAS_OBLIGATORIEDAD
from atlas_docs.exportes.documento import Documento
from atlas_docs.exportes.markdown import a_html

_PLANTILLAS = files("atlas_docs.exportes") / "plantillas"


def cortable(texto: str) -> Markup:
    """Escapa y permite cortar los nombres de campo largos después de «/» y «_» (`<wbr>`)."""
    return Markup(str(escape(texto)).replace("/", "/<wbr>").replace("_", "_<wbr>"))  # noqa: S704 — ya escapado


@cache
def _entorno() -> Environment:
    entorno = Environment(autoescape=True, undefined=StrictUndefined, trim_blocks=True, lstrip_blocks=True)  # noqa: S701
    entorno.globals.update(md=a_html, siglas=SIGLAS_OBLIGATORIEDAD, etiquetas=ETIQUETAS_OBLIGATORIEDAD)
    entorno.filters["cortable"] = cortable
    return entorno


@cache
def _recurso(nombre: str) -> str:
    return (_PLANTILLAS / nombre).read_text(encoding="utf-8")


def generar(documento: Documento, pdf: bool = False) -> str:
    plantilla = _entorno().from_string(_recurso("paquete.html.j2"))
    # Las hojas de estilo son archivos propios del paquete, no datos de entrada: van sin escapar.
    estilos = Markup(_recurso("estilos.css"))  # noqa: S704
    impresion = Markup(_recurso("impresion.css"))  # noqa: S704
    return plantilla.render(d=documento, pdf=pdf, estilos=estilos, impresion=impresion)
