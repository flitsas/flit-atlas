"""El PDF se dibuja por partes para acotar la memoria (exportes/pdf.py): las partes cubren todo el documento, en orden,
y el PDF unido conserva el encabezado y el pie, el índice con su número de página, los enlaces y los marcadores."""

import io
import logging
import re

import pytest
from apoyo_exportes import MARCA, requiere_pdf

from atlas_docs.exportes import Formato, PaqueteExportar, ResultadoExportacion
from atlas_docs.exportes import pdf as modulo_pdf
from atlas_docs.exportes.documento import Documento, armar
from atlas_docs.exportes.html import Vista, generar
from atlas_docs.exportes.specs import hash_contenido, leer


def _documento(paquete: PaqueteExportar) -> Documento:
    specs = [leer(s) for s in paquete.specs]
    return armar(paquete, specs, hash_contenido(paquete, specs))


def _ids(html: str) -> list[str]:
    return re.findall(r'\bid="([^"]+)"', html)


@pytest.mark.parametrize("nombre", ["paquete_ict", "paquete_atlas"])
def test_las_partes_cubren_el_documento_entero_y_en_orden(nombre: str, request: pytest.FixtureRequest) -> None:
    documento = _documento(request.getfixturevalue(nombre))
    partes = modulo_pdf._partes(documento)

    completo = _ids(generar(documento, pdf=True))
    por_partes = _ids(generar(documento, pdf=True, vista=Vista(frente=True)))
    for vista in partes:
        por_partes += _ids(generar(documento, pdf=True, vista=vista))
    assert por_partes == completo

    operaciones = [op.ancla for v in partes for b in v.bloques for op in b.operaciones]
    assert operaciones == [op.ancla for c in documento.contratos for op in c.operaciones]
    assert [v.pie for v in partes] == [False] * (len(partes) - 1) + [True]
    for c in documento.contratos:
        bloques = [b for v in partes for b in v.bloques if b.contrato is c]
        assert [b.cabecera for b in bloques] == [True] + [False] * (len(bloques) - 1)
        for t in c.tablas:
            trozos = [z for b in bloques for z in b.tablas if z.tabla is t]
            assert [z.primero for z in trozos] == [True] + [False] * (len(trozos) - 1)
            assert [f for z in trozos for f in z.filas] == t.filas


def test_el_contrato_de_atlas_se_parte_en_varias_partes_acotadas(paquete_atlas: PaqueteExportar) -> None:
    partes = modulo_pdf._partes(_documento(paquete_atlas))

    assert len(partes) > 5
    for vista in partes:
        pesos = [modulo_pdf._peso(op) for b in vista.bloques for op in b.operaciones]
        # Una operación sola puede pasar del tope; varias juntas no.
        assert len(pesos) <= 1 or sum(pesos) <= modulo_pdf.PESO_MAXIMO_PARTE


def test_una_tabla_larga_se_parte_en_trozos(paquete_ict: PaqueteExportar, monkeypatch: pytest.MonkeyPatch) -> None:
    documento = _documento(paquete_ict)
    tabla = max((t for c in documento.contratos for t in c.tablas), key=lambda t: len(t.filas))
    monkeypatch.setattr(modulo_pdf, "FILAS_POR_TROZO", max(1, len(tabla.filas) // 3))

    trozos = [z for v in modulo_pdf._partes(documento) for b in v.bloques for z in b.tablas if z.tabla is tabla]

    assert len(trozos) >= 3
    assert [f for z in trozos for f in z.filas] == tabla.filas


# --- El PDF unido (necesita Pango) ---


def _lector(resultado: ResultadoExportacion):  # noqa: ANN202 — PdfReader se importa solo con Pango
    from pypdf import PdfReader  # noqa: PLC0415

    return PdfReader(io.BytesIO(resultado.por_formato(Formato.PDF)[0].datos))


def test_encabezado_y_pie_con_pagina_x_de_y(atlas: ResultadoExportacion) -> None:
    requiere_pdf()
    lector = _lector(atlas)
    total = len(lector.pages)

    portada = lector.pages[0].extract_text()
    assert f"Página 1 de {total}" in portada and "FLIT · Atlas" not in portada
    for n in (2, total // 2, total):
        texto = lector.pages[n - 1].extract_text()
        assert f"Página {n} de {total}" in texto
        assert "FLIT · Atlas" in texto and MARCA in texto


def test_el_indice_lleva_la_pagina_de_cada_operacion_y_enlaza_a_ella(atlas: ResultadoExportacion) -> None:
    requiere_pdf()
    lector = _lector(atlas)
    textos = [p.extract_text().replace("\n", " ") for p in lector.pages]

    indice = "\n".join(lector.pages[i].extract_text() for i in range(1, 8))
    # Las entradas que caben en una línea: «GET /api/v1/x · Título ........ 40».
    entradas = re.findall(r"(GET|POST|PUT|PATCH|DELETE) (/\S+) · [^\n]*?(\d+)\s*$", indice, re.MULTILINE)
    assert len(entradas) > 100
    for metodo, ruta, pagina in entradas:
        assert f"{metodo} {ruta}" in textos[int(pagina) - 1], (metodo, ruta, pagina)

    # Los enlaces del índice siguen el orden del documento y cada número del índice tiene el suyo.
    destinos = [
        lector.get_page_number(anotacion.get_object()["/Dest"][0].get_object()) + 1
        for i in range(1, 8)
        for anotacion in lector.pages[i].get("/Annots") or []
    ]
    assert destinos == sorted(destinos)
    assert {int(p) for _, _, p in entradas} <= set(destinos)


def test_el_frente_no_registra_errores_por_anclas_de_otras_partes(
    paquete_ict: PaqueteExportar, caplog: pytest.LogCaptureFixture
) -> None:
    requiere_pdf()
    from atlas_docs.exportes import exportar  # noqa: PLC0415

    with caplog.at_level(logging.WARNING):
        exportar(paquete_ict, [Formato.PDF])

    assert not [r for r in caplog.records if "anchor" in r.getMessage()]


def test_el_pdf_unido_tiene_marcadores(atlas: ResultadoExportacion) -> None:
    requiere_pdf()
    lector = _lector(atlas)

    def titulos(marcadores: list) -> list[str]:
        return [t for m in marcadores for t in (titulos(m) if isinstance(m, list) else [m.title])]

    todos = titulos(lector.outline)
    assert "Índice" in todos
    assert len(todos) > 200  # contratos, operaciones y sus secciones
