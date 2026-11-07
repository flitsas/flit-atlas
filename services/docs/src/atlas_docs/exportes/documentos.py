"""Exportación de documentos de la F5 (HU 5.6, ATLAS-ADR-0053 §7): uno o una colección, a HTML, PDF, Word y Markdown.

Como `exportar`, es una función pura: atlas-api ya aplicó la visibilidad y convirtió el MDX en Markdown estático, con
los valores vigentes al exportar. Reutiliza la portada, la marca de agua y el índice de los paquetes: cada documento
ocupa el lugar de una guía, sin revisión, cambios ni contratos.
"""

import hashlib
import json
from collections.abc import Iterable

from atlas_docs.exportes import html, pdf, word
from atlas_docs.exportes.contenido import ancla
from atlas_docs.exportes.documento import Documento, GuiaDoc, fecha_legible
from atlas_docs.exportes.empaquetar import zip_determinista
from atlas_docs.exportes.exportar import DESCRIPCIONES, Archivo, ResultadoExportacion, _version_generador
from atlas_docs.exportes.modelo import DocumentoExportar, EntradaDocumentos, Formato

FORMATOS_DOCUMENTOS = frozenset({Formato.HTML, Formato.PDF, Formato.WORD, Formato.MARKDOWN, Formato.ZIP})


def _detalle(d: DocumentoExportar) -> str:
    return f"{d.tipo} · versión {d.version} del {fecha_legible(d.publicado)} · responsable: {d.responsable}"


def hash_contenido(entrada: EntradaDocumentos) -> str:
    """SHA-256 de lo exportado, en JSON canónico: no depende del formato."""
    texto = json.dumps(entrada.model_dump(mode="json"), ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(texto.encode("utf-8")).hexdigest()


def armar(entrada: EntradaDocumentos, huella: str) -> Documento:
    m = entrada.metadatos
    return Documento(
        paquete=m.nombre,
        revision=1,
        organizacion=m.organizacion,
        fecha=fecha_legible(m.fecha),
        dia=m.fecha,
        hash=huella,
        changelog=[],
        guias=[
            GuiaDoc(ancla(f"documento-{i}", d.titulo), d.titulo, d.markdown, _detalle(d))
            for i, d in enumerate(entrada.documentos, start=1)
        ],
        contratos=[],
        es_documentacion=True,
    )


def a_markdown(entrada: EntradaDocumentos, documento: Documento) -> str:
    partes = [f"<!-- {documento.leyenda} SHA-256 del contenido exportado: {documento.hash} -->", ""]
    for d in entrada.documentos:
        partes += [f"# {d.titulo}", "", f"_{_detalle(d)}_", "", d.markdown.strip(), ""]
    return "\n".join(partes)


def _leeme(documento: Documento, archivos: list[Archivo]) -> str:
    return "\n".join(
        [
            f"# {documento.paquete}",
            "",
            documento.leyenda,
            "",
            f"- Fecha: {documento.fecha} (hora de Bogotá)",
            f"- SHA-256 del contenido exportado: `{documento.hash}`",
            "",
            "## Documentos",
            "",
            *(f"- {g.titulo}: {g.detalle}" for g in documento.guias),
            "",
            "## Archivos",
            "",
            "| Archivo | Qué es |",
            "|---|---|",
            *(f"| `{a.ruta}` | {DESCRIPCIONES.get(a.formato, '')} |" for a in archivos),
            "| `manifiesto.json` | Hash del contenido y SHA-256 de cada archivo. |",
            "",
        ]
    )


def exportar_documentos(entrada: EntradaDocumentos, formatos: Iterable[Formato] | None = None) -> ResultadoExportacion:
    """Genera los formatos pedidos (todos si no se indica). La misma entrada da los mismos bytes."""
    pedidos = set(formatos) if formatos is not None else set(FORMATOS_DOCUMENTOS)
    huella = hash_contenido(entrada)
    documento = armar(entrada, huella)
    base = documento.nombre_base

    archivos: list[Archivo] = []
    if Formato.HTML in pedidos:
        archivos.append(Archivo(f"{base}.html", Formato.HTML, html.generar(documento).encode()))
    if Formato.PDF in pedidos:
        archivos.append(Archivo(f"{base}.pdf", Formato.PDF, pdf.generar(documento)))
    if Formato.WORD in pedidos:
        archivos.append(Archivo(f"{base}.docx", Formato.WORD, word.generar(documento)))
    if Formato.MARKDOWN in pedidos:
        archivos.append(Archivo(f"{base}.md", Formato.MARKDOWN, a_markdown(entrada, documento).encode()))

    manifiesto = {
        "documentos": [{"titulo": d.titulo, "version": d.version, "sha256": d.sha256} for d in entrada.documentos],
        "organizacion": documento.organizacion,
        "fecha": documento.fecha,
        "algoritmo": "SHA-256",
        "hash_contenido": huella,
        "generador": f"atlas-docs {_version_generador()}",
        "archivos": [
            {"ruta": a.ruta, "formato": a.formato.value, "bytes": len(a.datos), "sha256": a.sha256} for a in archivos
        ],
    }
    if Formato.ZIP in pedidos:
        contenido = [
            (f"{base}/LEEME.md", _leeme(documento, archivos).encode()),
            (f"{base}/manifiesto.json", (json.dumps(manifiesto, ensure_ascii=False, indent=2) + "\n").encode()),
            *((f"{base}/{a.ruta}", a.datos) for a in archivos),
        ]
        archivos.append(Archivo(f"{base}.zip", Formato.ZIP, zip_determinista(contenido, documento.dia)))
    return ResultadoExportacion(huella, archivos, manifiesto)
