"""Piezas sueltas: Markdown seguro, lectura de specs, modelo de entrada y ZIP determinista."""

import io
import zipfile
from datetime import date

import pytest
from pydantic import ValidationError

from atlas_docs.exportes.empaquetar import normalizar_zip, zip_determinista
from atlas_docs.exportes.markdown import a_html
from atlas_docs.exportes.modelo import PaqueteExportar, SpecExportar
from atlas_docs.exportes.specs import SpecInvalidoError, leer


def test_markdown_escapa_html_y_no_enlaza_afuera() -> None:
    html = a_html('# Título\n\n<img src="https://x.ejemplo.com/a.png"> [ir](https://ejemplo.com) [aquí](#seccion)')

    assert "<img" not in html and "&lt;img" in html
    assert 'href="https://' not in html
    assert '<span class="enlace-externo">ir</span> (https://ejemplo.com)' in html
    assert '<a href="#seccion">aquí</a>' in html
    assert "<h4>Título</h4>" in html


def test_markdown_rechaza_javascript() -> None:
    html = a_html("[x](javascript:alert(1))")

    assert "javascript:" not in html.split("</p>")[0].split(">", 1)[1] or "href" not in html
    assert "<a " not in html


def test_spec_que_no_es_openapi_ni_asyncapi() -> None:
    with pytest.raises(SpecInvalidoError):
        leer(SpecExportar(contrato="x", version="1.0.0", contenido="hola: mundo"))


def test_spec_con_tipo_equivocado() -> None:
    with pytest.raises(SpecInvalidoError):
        leer(SpecExportar(contrato="x", version="1.0.0", tipo="asyncapi", contenido="openapi: 3.1.0\ninfo: {}"))


def test_el_paquete_rechaza_campos_desconocidos_y_contratos_raros() -> None:
    base = {
        "metadatos": {"paquete": "P", "revision": 1, "organizacion": "Organismo Demo", "fecha": "2026-10-24"},
        "specs": [{"contrato": "x", "version": "1.0.0", "contenido": "openapi: 3.1.0"}],
    }
    PaqueteExportar.model_validate(base)
    with pytest.raises(ValidationError):
        PaqueteExportar.model_validate({**base, "extra": 1})
    with pytest.raises(ValidationError):
        PaqueteExportar.model_validate({**base, "specs": [{**base["specs"][0], "contrato": "../x"}]})
    with pytest.raises(ValidationError):
        PaqueteExportar.model_validate({**base, "specs": []})


def test_zip_determinista_sin_hora_de_generacion() -> None:
    fecha = date(2026, 10, 24)
    uno = zip_determinista([("a.txt", b"hola")], fecha)

    assert uno == zip_determinista([("a.txt", b"hola")], fecha)
    with zipfile.ZipFile(io.BytesIO(uno)) as archivo:
        assert archivo.infolist()[0].date_time == (2026, 10, 24, 0, 0, 0)
    otro = io.BytesIO()
    with zipfile.ZipFile(otro, "w") as archivo:
        archivo.writestr("a.txt", b"hola")  # con la hora actual
    assert normalizar_zip(otro.getvalue(), fecha) == uno
