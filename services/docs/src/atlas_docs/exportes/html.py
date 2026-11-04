"""HTML autocontenido (Jinja2) y su variante para imprimir en PDF.

Autoescape siempre activo: todo lo que llega del spec, de las guías o de los metadatos se escapa. El Markdown pasa
por `markdown.a_html` (HTML crudo escapado, sin enlaces ni imágenes remotas). CSS embebido y sin scripts.
"""

from dataclasses import dataclass, field
from functools import cache
from importlib.resources import files

from jinja2 import Environment, StrictUndefined
from markupsafe import Markup, escape

from atlas_docs.exportes.contenido import ETIQUETAS_OBLIGATORIEDAD, SIGLAS_OBLIGATORIEDAD, Contrato, Operacion, Tabla
from atlas_docs.exportes.documento import Documento, GuiaDoc
from atlas_docs.exportes.markdown import a_html

_PLANTILLAS = files("atlas_docs.exportes") / "plantillas"


@dataclass
class TrozoTabla:
    """Filas de una tabla de referencia; una tabla larga se parte en varios trozos y solo el primero lleva el ancla."""

    tabla: Tabla
    filas: list[list[str]]
    primero: bool = True


@dataclass
class Bloque:
    """Lo que va de un contrato en una parte: su cabecera (si es la primera), unas operaciones y unas tablas."""

    contrato: Contrato
    operaciones: list[Operacion] = field(default_factory=list)
    cabecera: bool = True
    tablas: list[TrozoTabla] = field(default_factory=list)
    titulo_tablas: bool = False


@dataclass
class Vista:
    """Qué partes del documento se dibujan. El HTML va completo; el PDF se dibuja por partes (pdf.py)."""

    frente: bool = False  # portada, índice y cambios del paquete
    guias: list[GuiaDoc] = field(default_factory=list)
    titulo_guias: bool = False
    bloques: list[Bloque] = field(default_factory=list)
    pie: bool = False


def vista_completa(documento: Documento) -> Vista:
    return Vista(
        frente=True,
        guias=documento.guias,
        titulo_guias=bool(documento.guias),
        bloques=[
            Bloque(c, c.operaciones, tablas=[TrozoTabla(t, t.filas) for t in c.tablas], titulo_tablas=bool(c.tablas))
            for c in documento.contratos
        ],
        pie=True,
    )


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


def generar(
    documento: Documento, pdf: bool = False, vista: Vista | None = None, paginas: dict[str, int] | None = None
) -> str:
    """El documento entero (o la `vista` que pida el PDF); `paginas` pone el número de página en el índice."""
    plantilla = _entorno().from_string(_recurso("paquete.html.j2"))
    # Las hojas de estilo son archivos propios del paquete, no datos de entrada: van sin escapar.
    estilos = Markup(_recurso("estilos.css"))  # noqa: S704
    impresion = Markup(_recurso("impresion.css"))  # noqa: S704
    return plantilla.render(
        d=documento,
        pdf=pdf,
        estilos=estilos,
        impresion=impresion,
        vista=vista or vista_completa(documento),
        paginas=paginas,
    )


def capa(documento: Documento, desde: int, hasta: int, total: int) -> str:
    """Páginas vacías `desde`..`hasta` (de `total`) con solo el encabezado y el pie, para superponer en el PDF."""
    plantilla = _entorno().from_string(_recurso("capa.html.j2"))
    return plantilla.render(
        marca=documento.marca_agua,
        huella=documento.hash[:16],
        numeros=range(desde, hasta + 1),
        total=total,
        portada=desde == 1,
    )
