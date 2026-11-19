"""Word (.docx) con python-docx: portada, índice, cambios, guías y contratos.

La marca de agua va en el encabezado y el pie de todas las páginas (organización + fecha), en gris; el índice es la
lista de secciones y operaciones (python-docx no pagina, así que no lleva números de página).
"""

import io
import re
from collections.abc import Sequence
from datetime import datetime, time

from docx import Document as nuevo_docx
from docx.document import Document as DocumentoDocx
from docx.enum.text import WD_BREAK
from docx.oxml.ns import qn
from docx.shared import Pt, RGBColor
from docx.table import Table
from docx.text.paragraph import Paragraph
from markdown_it.token import Token

from atlas_docs.exportes.contenido import ETIQUETAS_OBLIGATORIEDAD, SIGLAS_OBLIGATORIEDAD, Campo, Operacion
from atlas_docs.exportes.documento import Documento
from atlas_docs.exportes.empaquetar import normalizar_zip
from atlas_docs.exportes.markdown import texto_plano, tokens

AZUL = RGBColor(0x47, 0x6B, 0xD9)
NOCHE = RGBColor(0x16, 0x27, 0x44)
GRIS = RGBColor(0x64, 0x74, 0x8B)
_CONTROL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f]")


def _limpio(texto: str) -> str:
    """XML no admite caracteres de control."""
    return _CONTROL.sub("", texto or "")


def _fondo(celda: object, color: str) -> None:
    propiedades = celda._tc.get_or_add_tcPr()  # type: ignore[attr-defined]
    sombra = propiedades.makeelement(qn("w:shd"), {qn("w:val"): "clear", qn("w:color"): "auto", qn("w:fill"): color})
    propiedades.append(sombra)


def _tabla(doc: DocumentoDocx, encabezados: Sequence[str], filas: Sequence[Sequence[str]]) -> Table:
    tabla = doc.add_table(rows=1, cols=len(encabezados))
    tabla.style = "Table Grid"
    for i, titulo in enumerate(encabezados):
        celda = tabla.rows[0].cells[i]
        celda.text = ""
        corrida = celda.paragraphs[0].add_run(_limpio(titulo))
        corrida.bold = True
        _fondo(celda, "DFE5ED")
    for fila in filas:
        celdas = tabla.add_row().cells
        for i, valor in enumerate(fila):
            celdas[i].text = _limpio(str(valor))
    for fila_tabla in tabla.rows:
        for celda in fila_tabla.cells:
            for parrafo in celda.paragraphs:
                for corrida in parrafo.runs:
                    corrida.font.size = Pt(8)
    doc.add_paragraph()
    return tabla


def _codigo(doc: DocumentoDocx, texto: str) -> None:
    parrafo = doc.add_paragraph()
    corrida = parrafo.add_run(_limpio(texto))
    corrida.font.name = "Consolas"
    corrida.font.size = Pt(8)


def _inline(parrafo: Paragraph, hijos: Sequence[Token]) -> None:
    negrita = cursiva = False
    enlaces: list[str] = []
    for t in hijos:
        if t.type == "strong_open":
            negrita = True
        elif t.type == "strong_close":
            negrita = False
        elif t.type == "em_open":
            cursiva = True
        elif t.type == "em_close":
            cursiva = False
        elif t.type == "link_open":
            enlaces.append(str(t.attrGet("href") or ""))
        elif t.type == "link_close":
            destino = enlaces.pop() if enlaces else ""
            if destino and not destino.startswith("#"):
                parrafo.add_run(f" ({_limpio(destino)})")
        elif t.type in ("text", "code_inline"):
            corrida = parrafo.add_run(_limpio(t.content))
            corrida.bold, corrida.italic = negrita, cursiva
            if t.type == "code_inline":
                corrida.font.name = "Consolas"
        elif t.type in ("softbreak", "hardbreak"):
            parrafo.add_run(" ")
        elif t.type == "image":
            parrafo.add_run(f"[imagen: {_limpio(t.content) or 'sin descripción'}]").italic = True


def _markdown(doc: DocumentoDocx, texto: str, nivel_base: int = 3) -> None:
    """Bloques de Markdown a párrafos de Word (títulos, párrafos, listas, código y tablas)."""
    listas: list[str] = []
    celdas: list[list[str]] = []
    fila: list[str] = []
    en_celda = False
    titulo: int | None = None
    for t in tokens(texto):
        if t.type == "heading_open":
            titulo = min(9, nivel_base + int(t.tag[1]) - 1)
        elif t.type in ("bullet_list_open", "ordered_list_open"):
            listas.append("List Bullet" if t.type == "bullet_list_open" else "List Number")
        elif t.type in ("bullet_list_close", "ordered_list_close"):
            listas.pop()
        elif t.type in ("th_open", "td_open"):
            en_celda = True
        elif t.type in ("th_close", "td_close"):
            en_celda = False
        elif t.type == "tr_open":
            fila = []
        elif t.type == "tr_close":
            celdas.append(fila)
        elif t.type == "table_close" and celdas:
            ancho = max(len(f) for f in celdas)
            filas = [f + [""] * (ancho - len(f)) for f in celdas]
            _tabla(doc, filas[0], filas[1:])
            celdas = []
        elif t.type in ("fence", "code_block"):
            _codigo(doc, t.content.rstrip("\n"))
        elif t.type == "inline":
            if en_celda:
                fila.append(_limpio(texto_plano(t.children or [])))
            elif titulo is not None:
                doc.add_heading(_limpio(texto_plano(t.children or [])), level=titulo)
                titulo = None
            else:
                _inline(doc.add_paragraph(style=listas[-1] if listas else "Normal"), t.children or [])


def _marca_de_agua(doc: DocumentoDocx, documento: Documento) -> None:
    for seccion in doc.sections:
        for parte, texto in ((seccion.header, documento.marca_agua), (seccion.footer, f"SHA-256 {documento.hash}")):
            parrafo = parte.paragraphs[0]
            parrafo.text = ""
            corrida = parrafo.add_run(_limpio(texto))
            corrida.font.size = Pt(8)
            corrida.font.color.rgb = GRIS


def _campos(doc: DocumentoDocx, op: Operacion, campos: list[Campo], con_matriz: bool) -> None:
    m = op.matriz if con_matriz else None
    if m:
        encabezados = ["Campo", "Descripción", "Tipo de dato", *(f"Oblig. {t.nombre}" for t in m.tipos), "Observ."]
        filas = []
        for c in campos:
            f = m.fila(c.nombre)
            valores = [
                SIGLAS_OBLIGATORIEDAD[f.valores[t.clave]] if f else ("S" if c.obligatorio else "N") for t in m.tipos
            ]
            filas.append(["  " * c.nivel + c.nombre, c.descripcion, c.tipo, *valores, f.condicion if f else ""])
        _tabla(doc, encabezados, filas)
    else:
        _tabla(
            doc,
            ["Campo", "Descripción", "Tipo de dato", "Obligatoriedad"],
            [
                [
                    "  " * c.nivel + c.nombre,
                    c.descripcion + (f" ({c.restricciones})" if c.restricciones else ""),
                    c.tipo,
                    "S" if c.obligatorio else "N",
                ]
                for c in campos
            ],
        )


def _operacion(doc: DocumentoDocx, op: Operacion, asincrono: bool) -> None:
    doc.add_heading(_limpio(f"{op.metodo} {op.ruta}"), level=3)
    p = doc.add_paragraph()
    p.add_run(_limpio(op.titulo)).bold = True
    p.add_run(_limpio(f" · {op.etiqueta} · {op.operation_id}" + (" · Deprecada" if op.deprecada else "")))
    if op.descripcion:
        _markdown(doc, op.descripcion, nivel_base=4)
    doc.add_paragraph().add_run("Header").bold = True
    _codigo(doc, "\n".join(op.cabeceras))
    if op.parametros:
        doc.add_paragraph().add_run("Parámetros").bold = True
        _tabla(
            doc,
            ["Campo", "Ubicación", "Descripción", "Tipo de dato", "Obligatoriedad"],
            [[c.nombre, c.ubicacion, c.descripcion, c.tipo, "S" if c.obligatorio else "N"] for c in op.parametros],
        )
    if op.cuerpo:
        doc.add_paragraph().add_run("Mensaje" if asincrono else "Cuerpo").bold = True
        _campos(doc, op, op.cuerpo, con_matriz=True)
    if op.ejemplo_entrada:
        doc.add_paragraph().add_run("Ejemplo").bold = True
        _codigo(doc, op.ejemplo_entrada)
    if op.respuestas:
        doc.add_paragraph().add_run("Salida").bold = True
        _tabla(doc, ["Código", "Descripción"], [[r.codigo, r.descripcion] for r in op.respuestas])
        for r in op.respuestas:
            if r.campos:
                doc.add_paragraph().add_run(f"Respuesta {r.codigo}").bold = True
                _campos(doc, op, r.campos, con_matriz=False)
            if r.ejemplo:
                _codigo(doc, r.ejemplo)
    if op.documentos:
        dm = op.documentos
        doc.add_paragraph().add_run(dm.titulo).bold = True
        _tabla(
            doc,
            ["ID", "Documento", *(t.nombre for t in dm.tipos)],
            [[f.campo, f.detalle, *(ETIQUETAS_OBLIGATORIEDAD[f.valores[t.clave]] for t in dm.tipos)] for f in dm.filas],
        )


def generar(documento: Documento) -> bytes:
    doc = nuevo_docx()
    estilo = doc.styles["Normal"]
    estilo.font.name = "Calibri"  # type: ignore[union-attr]
    estilo.font.size = Pt(10)  # type: ignore[union-attr]
    # Propiedades fijas: el archivo no lleva la hora de generación ni un autor real.
    fecha = datetime.combine(documento.dia, time())
    propiedades = doc.core_properties
    propiedades.author = "Flit Atlas"
    propiedades.last_modified_by = "Flit Atlas"
    propiedades.title = _limpio(documento.titulo)
    propiedades.subject = _limpio(documento.marca_agua)
    propiedades.keywords = documento.hash
    propiedades.created = fecha
    propiedades.modified = fecha
    propiedades.revision = documento.revision

    # Portada
    marca = doc.add_paragraph().add_run("FLIT · ATLAS")
    marca.bold, marca.font.color.rgb = True, AZUL
    doc.add_heading(_limpio(documento.paquete), level=0)
    doc.add_paragraph(f"Paquete de contratos de integración · revisión {documento.revision}")
    for etiqueta, valor in (
        ("Organización", documento.organizacion),
        ("Fecha", f"{documento.fecha} (hora de Bogotá)"),
        ("Contratos", "; ".join(f"{c.titulo} {c.version} ({c.tipo})" for c in documento.contratos)),
        ("SHA-256", documento.hash),
    ):
        p = doc.add_paragraph()
        p.add_run(f"{etiqueta}: ").bold = True
        p.add_run(_limpio(valor))
    doc.add_paragraph(_limpio(documento.leyenda)).runs[0].italic = True
    doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)

    # Índice
    doc.add_heading("Índice", level=1)
    doc.add_paragraph("Cambios del paquete", style="List Number")
    if documento.guias:
        doc.add_paragraph("Guías", style="List Number")
        for g in documento.guias:
            doc.add_paragraph(_limpio(g.titulo), style="List Bullet 2")
    for c in documento.contratos:
        doc.add_paragraph(_limpio(f"{c.titulo} {c.version}"), style="List Number")
        for op in c.operaciones:
            doc.add_paragraph(_limpio(f"{op.metodo} {op.ruta} · {op.titulo}"), style="List Bullet 2")
    doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)

    # Cambios
    doc.add_heading("Cambios del paquete", level=1)
    if documento.changelog:
        _tabla(doc, ["Revisión", "Fecha", "Cambio"], [[e.revision, e.fecha, e.cambio] for e in documento.changelog])
    else:
        doc.add_paragraph("Primera revisión del paquete.")

    if documento.guias:
        doc.add_heading("Guías", level=1)
        for g in documento.guias:
            doc.add_heading(_limpio(g.titulo), level=2)
            _markdown(doc, g.markdown)

    for c in documento.contratos:
        doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)
        doc.add_heading(_limpio(f"{c.titulo} {c.version}"), level=1)
        doc.add_paragraph(_limpio(f"Contrato {c.contrato} · {c.tipo}"))
        if c.descripcion:
            _markdown(doc, c.descripcion)
        if c.ambientes:
            doc.add_heading("URL por ambiente", level=2)
            _tabla(doc, ["Ambiente", "URL"], [[a.nombre, a.url] for a in c.ambientes])
        if c.historial:
            doc.add_heading("Historial del contrato", level=2)
            _tabla(doc, ["Versión", "Fecha", "Cambio"], [[h["version"], h["fecha"], h["cambio"]] for h in c.historial])
        doc.add_heading("Operaciones", level=2)
        for op in c.operaciones:
            _operacion(doc, op, c.tipo == "AsyncAPI")
        if c.tablas:
            doc.add_heading("Tablas de referencia", level=2)
            for t in c.tablas:
                doc.add_heading(_limpio(t.titulo), level=3)
                _tabla(doc, t.columnas, t.filas)

    _marca_de_agua(doc, documento)
    salida = io.BytesIO()
    doc.save(salida)
    return normalizar_zip(salida.getvalue(), documento.dia)
