"""Exportación de documentos de la F5 (HU 5.6, ATLAS-ADR-0053 §7): formatos, determinismo y su camino por la cola."""

import io
import json
import zipfile

import anyio
import pytest
from apoyo_exportes import requiere_pdf
from docx import Document
from pydantic import ValidationError
from test_cola import CLAVE, EXPORTACION, ORGANIZACION, PublicadorFalso, S3Falso

from atlas_docs import cola
from atlas_docs.cola import ManejadorExportaciones, uuid7
from atlas_docs.exportes import EntradaDocumentos, Formato, exportar_documentos

SIN_PDF = {Formato.HTML, Formato.WORD, Formato.MARKDOWN, Formato.ZIP}


def _entrada(**cambios: object) -> dict:
    return {
        "metadatos": {
            "nombre": "Trámites en línea",
            "organizacion": "FLIT S.A.S. · uso interno",
            "fecha": "2026-11-06",
        },
        "documentos": [
            {
                "titulo": "Radicar un trámite",
                "tipo": "Guía funcional",
                "version": 3,
                "responsable": "Persona Demo",
                "publicado": "2026-11-01",
                "sha256": "a" * 64,
                "markdown": "## Pasos\n\n1. Entra.\n\n| Campo | Largo |\n|---|---|\n| placa | 6 |\n\n"
                "<script>alert(1)</script>\n",
            },
            {
                "titulo": "Consultar un trámite",
                "tipo": "Manual de usuario",
                "version": 1,
                "responsable": "Persona Demo",
                "publicado": "2026-10-20",
                "sha256": "b" * 64,
                "markdown": "Texto con [enlace](https://ejemplo.com/x).",
            },
        ],
        **cambios,
    }


def _modelo(**cambios: object) -> EntradaDocumentos:
    return EntradaDocumentos.model_validate(_entrada(**cambios))


def test_html_trae_los_documentos_sin_lo_propio_de_un_paquete() -> None:
    r = exportar_documentos(_modelo(), {Formato.HTML})
    html = r.archivo("tramites-en-linea.html").datos.decode()
    assert "Documentación técnica y funcional" in html
    assert "Radicar un trámite" in html and "Consultar un trámite" in html
    assert "Guía funcional · versión 3 del 01/11/2026" in html
    assert "FLIT S.A.S. · uso interno · 06/11/2026" in html
    assert r.hash_contenido in html
    assert "Cambios del paquete" not in html
    assert "Paquete de contratos" not in html


def test_el_html_crudo_del_cuerpo_sale_escapado() -> None:
    html = exportar_documentos(_modelo(), {Formato.HTML}).archivo("tramites-en-linea.html").datos.decode()
    assert "<script>alert" not in html
    assert "&lt;script&gt;alert" in html


def test_word_trae_titulos_y_sin_cambios_del_paquete() -> None:
    datos = exportar_documentos(_modelo(), {Formato.WORD}).archivo("tramites-en-linea.docx").datos
    textos = [p.text for p in Document(io.BytesIO(datos)).paragraphs]
    assert "Radicar un trámite" in textos
    assert "Documentación técnica y funcional" in textos
    assert "Cambios del paquete" not in textos


def test_markdown_lleva_cada_documento_y_el_hash() -> None:
    r = exportar_documentos(_modelo(), {Formato.MARKDOWN})
    texto = r.archivo("tramites-en-linea.md").datos.decode()
    assert "# Radicar un trámite" in texto and "# Consultar un trámite" in texto
    assert "[enlace](https://ejemplo.com/x)" in texto
    assert r.hash_contenido in texto


def test_el_zip_trae_todo_con_su_manifiesto() -> None:
    r = exportar_documentos(_modelo(), SIN_PDF)
    with zipfile.ZipFile(io.BytesIO(r.zip.datos)) as z:
        nombres = set(z.namelist())
        manifiesto = json.loads(z.read("tramites-en-linea/manifiesto.json"))
    assert {"tramites-en-linea/LEEME.md", "tramites-en-linea/tramites-en-linea.md"} <= nombres
    assert manifiesto["hash_contenido"] == r.hash_contenido
    assert [d["version"] for d in manifiesto["documentos"]] == [3, 1]


def test_la_misma_entrada_da_los_mismos_bytes() -> None:
    a, b = exportar_documentos(_modelo(), SIN_PDF), exportar_documentos(_modelo(), SIN_PDF)
    assert a.hash_contenido == b.hash_contenido
    assert [(x.ruta, x.datos) for x in a.archivos] == [(x.ruta, x.datos) for x in b.archivos]


def test_otro_contenido_da_otro_hash() -> None:
    otra = _entrada()
    otra["documentos"][0]["markdown"] = "Otro texto"
    assert (
        exportar_documentos(_modelo(), SIN_PDF).hash_contenido
        != exportar_documentos(EntradaDocumentos.model_validate(otra), SIN_PDF).hash_contenido
    )


def test_pdf_se_genera() -> None:
    requiere_pdf()
    datos = exportar_documentos(_modelo(), {Formato.PDF}).archivo("tramites-en-linea.pdf").datos
    assert datos.startswith(b"%PDF")


@pytest.mark.parametrize(
    "cambios",
    [
        {"documentos": []},
        {"otro": 1},
        {"metadatos": {"nombre": "x", "organizacion": "y", "fecha": "no-es-fecha"}},
    ],
)
def test_una_entrada_invalida_no_pasa(cambios: dict) -> None:
    with pytest.raises(ValidationError):
        _modelo(**cambios)


# --- Por la cola ---


def _solicitud(formatos: list[str], tipo: str = cola.DOCUMENTOS_SOLICITADA) -> bytes:
    return json.dumps(
        {
            "eventId": uuid7(),
            "type": tipo,
            "version": 1,
            "occurredAt": "2026-11-06T15:30:00+00:00",
            "tenantId": ORGANIZACION,
            "producer": "atlas",
            "correlationId": "4bf92f3577b34da6a3ce929d0e0e4736",
            "data": {"exportacionId": EXPORTACION, "formatos": formatos, "entradaClave": CLAVE},
        }
    ).encode()


def _procesar(s3: S3Falso, mensaje: bytes) -> PublicadorFalso:
    publicador = PublicadorFalso()
    manejador = ManejadorExportaciones(lambda: s3, "atlas", publicador, anyio.CapacityLimiter(1))
    anyio.run(manejador.procesar, mensaje)
    return publicador


def _con_entrada(s3: S3Falso, entrada: dict | None = None) -> None:
    s3.objetos[CLAVE] = {"Body": json.dumps(entrada or _entrada()).encode()}


def test_por_la_cola_publica_sus_propios_eventos() -> None:
    s3 = S3Falso()
    _con_entrada(s3)
    publicador = _procesar(s3, _solicitud(["html", "markdown", "zip"]))
    assert publicador.tipos() == [cola.DOCUMENTOS_INICIADA, cola.DOCUMENTOS_LISTA]
    datos = publicador.eventos[-1]["data"]
    assert {a["formato"] for a in datos["archivos"]} == {"html", "markdown", "zip"}
    assert f"exportaciones/{EXPORTACION}/tramites-en-linea.zip" in s3.objetos


def test_un_formato_de_contratos_no_vale_para_documentos() -> None:
    s3 = S3Falso()
    _con_entrada(s3)
    publicador = _procesar(s3, _solicitud(["postman", "zip"]))
    assert publicador.tipos() == [cola.DOCUMENTOS_INICIADA, cola.DOCUMENTOS_FALLIDA]
    assert publicador.eventos[-1]["data"]["codigo"] == "entrada_invalida"


def test_una_entrada_ilegible_falla_sin_mostrar_su_contenido() -> None:
    s3 = S3Falso()
    s3.objetos[CLAVE] = {"Body": b'{"metadatos": "SECRETO"}'}
    publicador = _procesar(s3, _solicitud(["html"]))
    assert publicador.tipos() == [cola.DOCUMENTOS_INICIADA, cola.DOCUMENTOS_FALLIDA]
    assert "SECRETO" not in publicador.eventos[-1]["data"]["mensaje"]


def test_abandonar_publica_la_fallida_de_documentos() -> None:
    publicador = PublicadorFalso()
    manejador = ManejadorExportaciones(lambda: S3Falso(), "atlas", publicador, anyio.CapacityLimiter(1))
    anyio.run(manejador.abandonar, _solicitud(["html"]))
    assert publicador.tipos() == [cola.DOCUMENTOS_FALLIDA]
