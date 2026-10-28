"""PDF con WeasyPrint desde el HTML de impresión, dibujado por partes para acotar la memoria.

WeasyPrint diagrama el documento entero y guarda todas las páginas antes de escribirlo: el contrato de Atlas (215
operaciones, unas 470 páginas) pasaba de 1 GiB y el pod se caía por memoria. Aquí cada parte (unas guías, un bloque de
operaciones, unos trozos de tablas) se dibuja y se escribe por separado; de ella solo quedan su PDF y sus anclas,
enlaces y marcadores. Después:

1. el frente (portada, índice y cambios) se dibuja con el número de página de cada ancla, que ya se conoce;
2. pypdf une el frente y las partes y superpone la capa del encabezado y el pie («Página X de Y», marca y huella),
   también dibujada por tramos;
3. se rehacen los enlaces internos y los marcadores sobre el documento entero.

El pico de memoria depende del tamaño de una parte, no del contrato (ATLAS-ADR-0042). La salida sigue siendo
determinista: misma entrada, mismos bytes.

WeasyPrint necesita Pango (librería nativa). En la imagen y en el CI está instalada; en un Windows sin GTK no carga y
este módulo lo dice con `WeasyPrintNoDisponibleError` en lugar de romper el import del servicio.

Sin red: el `url_fetcher` rechaza cualquier recurso (la plantilla no pide ninguno; si un texto lo intentara, falla).
"""

import gc
import io
import logging
from collections.abc import Iterator
from dataclasses import dataclass
from functools import cache
from typing import Any

from pypdf import PdfReader, PdfWriter
from pypdf.annotations import Link
from pypdf.generic import Fit, NameObject, TextStringObject

from atlas_docs.exportes.contenido import Contrato, Operacion
from atlas_docs.exportes.documento import Documento
from atlas_docs.exportes.html import Bloque, TrozoTabla, Vista, capa
from atlas_docs.exportes.html import generar as generar_html

# Tamaño de una parte en «filas» (una fila de tabla o una línea de ejemplo; ver `_peso`). Con 600, el contrato de Atlas
# sale en 17 partes de unas 30 páginas y el pico de memoria baja de 1,14 GiB a 214 MiB (ATLAS-ADR-0042).
PESO_MAXIMO_PARTE = 600
# Filas de una tabla de referencia por trozo: una tabla de miles de filas no cabe en una parte.
FILAS_POR_TROZO = 400
# Páginas de la capa del encabezado y el pie que se dibujan de una vez.
PAGINAS_POR_TRAMO = 100
# De píxeles CSS (1/96 in, los de WeasyPrint) a puntos PDF (1/72 in).
PUNTOS_POR_PIXEL = 0.75


class WeasyPrintNoDisponibleError(RuntimeError):
    pass


class RecursoRemotoError(RuntimeError):
    pass


class _SinAnclasDeOtrasPartes(logging.Filter):
    """El índice enlaza anclas que están en otras partes: WeasyPrint lo registra como error en cada parte, pero esos
    enlaces se rehacen al unir (`_enlazar`). El resto de sus mensajes pasa igual."""

    def filter(self, registro: logging.LogRecord) -> bool:
        return not str(registro.msg).startswith("No anchor ")


@cache
def _weasyprint() -> Any:
    try:
        import weasyprint  # noqa: PLC0415 — carga perezosa: depende de Pango
    except OSError as error:  # cffi no encuentra libpango/libgobject
        raise WeasyPrintNoDisponibleError(
            "WeasyPrint no encuentra Pango. Instala las librerías del sistema (ver services/docs/README.md)."
        ) from error
    logging.getLogger("weasyprint").addFilter(_SinAnclasDeOtrasPartes())
    return weasyprint


def disponible() -> bool:
    try:
        _weasyprint()
    except WeasyPrintNoDisponibleError:
        return False
    return True


def _sin_red(url: str, *args: object, **kwargs: object) -> dict[str, Any]:
    raise RecursoRemotoError(f"La exportación no carga recursos externos: {url[:80]}")


# --- Partes ---


def _lineas(texto: str | None) -> int:
    return texto.count("\n") + 1 if texto else 0


def _peso(op: Operacion) -> int:
    """Filas que dibuja una operación, aproximadas: lo que más pesa al diagramar son las tablas y los ejemplos."""
    peso = 10 + len(op.parametros) + len(op.cuerpo) + _lineas(op.ejemplo_entrada) + len(op.respuestas)
    peso += sum(len(r.campos) + _lineas(r.ejemplo) for r in op.respuestas)
    if op.documentos:
        peso += len(op.documentos.filas)
    return peso


class _Partidor:
    """Junta guías, operaciones y trozos de tablas en partes de hasta `PESO_MAXIMO_PARTE`, en el orden del HTML."""

    def __init__(self) -> None:
        self.partes: list[Vista] = []
        self.actual = Vista()
        self.peso = 0
        self._con_cabecera: set[str] = set()

    def hacer_sitio(self, peso: int) -> None:
        """Cierra la parte si `peso` ya no cabe. Una pieza más grande que una parte va sola en la suya."""
        if (self.actual.guias or self.actual.bloques) and self.peso + peso > PESO_MAXIMO_PARTE:
            self.cerrar()
        self.peso += peso

    def cerrar(self) -> None:
        if self.actual.guias or self.actual.bloques:
            self.partes.append(self.actual)
        self.actual, self.peso = Vista(), 0

    def bloque(self, contrato: Contrato) -> Bloque:
        """El bloque del contrato en la parte actual; la cabecera solo va en el primero."""
        if not self.actual.bloques or self.actual.bloques[-1].contrato is not contrato:
            cabecera = contrato.ancla not in self._con_cabecera
            self._con_cabecera.add(contrato.ancla)
            self.actual.bloques.append(Bloque(contrato, cabecera=cabecera))
        return self.actual.bloques[-1]


def _partes(documento: Documento) -> list[Vista]:
    """El cuerpo del documento (guías, operaciones y tablas de referencia) en partes."""
    partidor = _Partidor()
    for g in documento.guias:
        partidor.hacer_sitio(10 + _lineas(g.markdown))
        partidor.actual.titulo_guias = partidor.actual.titulo_guias or g is documento.guias[0]
        partidor.actual.guias.append(g)

    for c in documento.contratos:
        partidor.hacer_sitio(10 + len(c.ambientes) + len(c.historial))
        partidor.bloque(c)
        for op in c.operaciones:
            partidor.hacer_sitio(_peso(op))
            partidor.bloque(c).operaciones.append(op)
        for k, t in enumerate(c.tablas):
            for desde in range(0, max(len(t.filas), 1), FILAS_POR_TROZO):
                filas = t.filas[desde : desde + FILAS_POR_TROZO]
                partidor.hacer_sitio(5 + len(filas))
                bloque = partidor.bloque(c)
                bloque.titulo_tablas = bloque.titulo_tablas or (k == 0 and desde == 0)
                bloque.tablas.append(TrozoTabla(t, filas, primero=desde == 0))
    partidor.cerrar()
    partidor.partes[-1].pie = True
    return partidor.partes


# --- Dibujo ---


@dataclass
class _Dibujada:
    """Lo que queda de una parte después de escribirla: su PDF y, por página local, anclas, enlaces y marcadores."""

    pdf: bytes
    paginas: int
    alto: float  # alto de la página en píxeles CSS (todas son A4)
    anclas: dict[str, tuple[int, float, float]]
    enlaces: list[tuple[int, tuple[float, float, float, float], str]]
    marcadores: list[tuple[int, int, str, float, float]]


def _dibujar(html: str) -> _Dibujada:
    documento = _weasyprint().HTML(string=html, url_fetcher=_sin_red, base_url=None).render()
    anclas: dict[str, tuple[int, float, float]] = {}
    enlaces: list[tuple[int, tuple[float, float, float, float], str]] = []
    marcadores: list[tuple[int, int, str, float, float]] = []
    for i, pagina in enumerate(documento.pages):
        for nombre, posicion in pagina.anchors.items():
            anclas.setdefault(nombre, (i, posicion[0], posicion[1]))
        # Solo enlaces internos: la plantilla y el Markdown no dejan enlaces a otros sitios.
        enlaces.extend((i, tuple(rect), destino) for tipo, destino, rect, _ in pagina.links if tipo == "internal")
        marcadores.extend((i, nivel, titulo, x, y) for nivel, titulo, (x, y), _ in pagina.bookmarks)
    dibujada = _Dibujada(
        pdf=bytes(documento.write_pdf(uncompressed_pdf=False)),
        paginas=len(documento.pages),
        alto=documento.pages[0].height,
        anclas=anclas,
        enlaces=enlaces,
        marcadores=marcadores,
    )
    # El árbol de cajas de WeasyPrint tiene ciclos: sin esto, la parte anterior sigue en memoria durante la siguiente.
    del documento
    gc.collect()
    return dibujada


def _numerar(frente: _Dibujada, cuerpo: list[_Dibujada]) -> dict[str, int]:
    """Número de página (desde 1) de cada ancla del documento unido."""
    paginas: dict[str, int] = {}
    inicio = 0
    for parte in (frente, *cuerpo):
        for nombre, (i, _, _) in parte.anclas.items():
            paginas.setdefault(nombre, inicio + i + 1)
        inicio += parte.paginas
    return paginas


def _frente(documento: Documento, cuerpo: list[_Dibujada]) -> _Dibujada:
    """Portada, índice y cambios con el número de página de cada ancla. Los números dependen del largo del propio
    frente, así que se dibuja hasta que no cambian (dos o tres veces: es corto)."""
    paginas: dict[str, int] = {}
    frente = _dibujar(generar_html(documento, pdf=True, vista=Vista(frente=True), paginas=paginas))
    for _ in range(4):
        nuevas = _numerar(frente, cuerpo)
        if nuevas == paginas:
            return frente
        paginas = nuevas
        frente = _dibujar(generar_html(documento, pdf=True, vista=Vista(frente=True), paginas=paginas))
    return frente


# --- Unión ---


def _punto(x: float, y: float, alto: float) -> tuple[float, float]:
    """De la posición de WeasyPrint (píxeles desde arriba a la izquierda) a puntos PDF (desde abajo a la izquierda)."""
    return x * PUNTOS_POR_PIXEL, (alto - y) * PUNTOS_POR_PIXEL


def _unir(partes: list[_Dibujada]) -> PdfWriter:
    escritor = PdfWriter()
    for parte in partes:
        lector = PdfReader(io.BytesIO(parte.pdf))
        # Los enlaces de cada parte apuntan a su propio PDF: se rehacen sobre el documento unido (`_enlazar`).
        for pagina in lector.pages:
            if "/Annots" in pagina:
                del pagina[NameObject("/Annots")]
        escritor.append(lector, import_outline=False)
    return escritor


def _tramos(total: int) -> Iterator[tuple[int, int]]:
    for desde in range(1, total + 1, PAGINAS_POR_TRAMO):
        yield desde, min(desde + PAGINAS_POR_TRAMO - 1, total)


def _superponer_capa(escritor: PdfWriter, documento: Documento) -> None:
    total = len(escritor.pages)
    for desde, hasta in _tramos(total):
        html = capa(documento, desde, hasta, total)
        datos = _weasyprint().HTML(string=html, url_fetcher=_sin_red, base_url=None).write_pdf()
        hojas = PdfReader(io.BytesIO(datos)).pages
        if len(hojas) != hasta - desde + 1:  # pragma: no cover — la plantilla pone una hoja por página
            raise RuntimeError(f"La capa del PDF tiene {len(hojas)} páginas y se esperaban {hasta - desde + 1}")
        for k, hoja in enumerate(hojas):
            pagina = escritor.pages[desde - 1 + k]
            pagina.merge_page(hoja)
            pagina.compress_content_streams()


def _enlazar(escritor: PdfWriter, partes: list[_Dibujada]) -> None:
    """Enlaces internos (índice, tablas de referencia) y marcadores (h1 a h4) sobre el documento unido."""
    destinos: dict[str, tuple[int, float, float, float]] = {}
    inicio = 0
    for parte in partes:
        for nombre, (i, x, y) in parte.anclas.items():
            destinos.setdefault(nombre, (inicio + i, x, y, parte.alto))
        inicio += parte.paginas

    inicio = 0
    pila: list[tuple[int, Any]] = []
    for parte in partes:
        for i, (x1, y1, x2, y2), destino in parte.enlaces:
            if destino not in destinos:
                continue
            j, x, y, alto = destinos[destino]
            izquierda, abajo = _punto(x1, y2, parte.alto)
            derecha, arriba = _punto(x2, y1, parte.alto)
            enlace = Link(
                rect=(izquierda, abajo, derecha, arriba),
                border=[0, 0, 0],
                target_page_index=j,
                fit=Fit.xyz(*_punto(x, y, alto)),
            )
            escritor.add_annotation(inicio + i, enlace)
        for i, nivel, titulo, x, y in parte.marcadores:
            while pila and pila[-1][0] >= nivel:
                pila.pop()
            padre = pila[-1][1] if pila else None
            pila.append(
                (
                    nivel,
                    escritor.add_outline_item(titulo, inicio + i, parent=padre, fit=Fit.xyz(*_punto(x, y, parte.alto))),
                )
            )
        inicio += parte.paginas


def generar(documento: Documento) -> bytes:
    _weasyprint()
    cuerpo = [_dibujar(generar_html(documento, pdf=True, vista=v)) for v in _partes(documento)]
    partes = [_frente(documento, cuerpo), *cuerpo]

    escritor = _unir(partes)
    _superponer_capa(escritor, documento)
    _enlazar(escritor, partes)
    # merge_page copia los recursos de la capa (las fuentes) en cada página: sin esto el PDF pesa diez veces más.
    escritor.compress_identical_objects(remove_duplicates=True, remove_unreferenced=True)
    # Metadatos fijos: el PDF no lleva la hora de generación (misma entrada, mismos bytes).
    escritor.add_metadata(
        {"/Title": documento.titulo, "/Creator": "Flit Atlas (atlas-docs)", "/Producer": "WeasyPrint"}
    )
    escritor.root_object[NameObject("/Lang")] = TextStringObject("es-CO")
    salida = io.BytesIO()
    escritor.write(salida)
    return salida.getvalue()
