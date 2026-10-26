"""La misma entrada da el mismo hash y los mismos bytes; un cambio en la entrada cambia el hash."""

from apoyo_exportes import EVENTOS, ICT, formatos_disponibles, paquete

from atlas_docs.exportes import Formato, PaqueteExportar, ResultadoExportacion, exportar
from atlas_docs.exportes.contenido import ancla


def test_misma_entrada_mismo_hash_y_mismos_bytes(paquete_ict: PaqueteExportar, ict: ResultadoExportacion) -> None:
    otra = exportar(paquete_ict, formatos_disponibles())

    assert otra.hash_contenido == ict.hash_contenido
    assert [(a.ruta, a.sha256) for a in otra.archivos] == [(a.ruta, a.sha256) for a in ict.archivos]


def test_el_hash_no_depende_del_formato_del_spec(paquete_ict: PaqueteExportar, ict: ResultadoExportacion) -> None:
    json_ = next(a for a in ict.por_formato(Formato.OPENAPI) if a.ruta.endswith(".json"))
    datos = paquete_ict.model_dump()
    datos["specs"][0]["contenido"] = json_.datos.decode()

    assert exportar(PaqueteExportar.model_validate(datos), [Formato.OPENAPI]).hash_contenido == ict.hash_contenido


def test_otra_organizacion_cambia_el_hash_y_la_marca(ict: ResultadoExportacion) -> None:
    otra = exportar(paquete(("core-ict", "1.1.0", ICT), organizacion="Secretaría Ficticia"), [Formato.HTML])

    assert otra.hash_contenido != ict.hash_contenido
    assert "Secretaría Ficticia · 24/10/2026" in otra.archivos[0].datos.decode()


def test_asyncapi_va_en_los_documentos_y_no_en_postman() -> None:
    resultado = exportar(
        paquete(("atlas-eventos", "1.0.0", EVENTOS)), [Formato.OPENAPI, Formato.POSTMAN, Formato.HTML, Formato.EXCEL]
    )

    assert [a.ruta for a in resultado.por_formato(Formato.OPENAPI)] == [
        "asyncapi/atlas-eventos-1.0.0.yaml",
        "asyncapi/atlas-eventos-1.0.0.json",
    ]
    assert resultado.por_formato(Formato.POSTMAN) == []
    html = resultado.por_formato(Formato.HTML)[0].datos.decode()
    assert "atlas.contratos.version_publicada" in html and "AsyncAPI" in html


def test_nombres_de_archivo_ascii() -> None:
    assert ancla("Integración ICT", "r3") == "integracion-ict-r3"
