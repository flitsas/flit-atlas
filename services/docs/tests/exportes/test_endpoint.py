"""POST /exportes: responde el ZIP o deja los archivos en el S3 (con un cliente S3 de mentira)."""

import io
import zipfile

from apoyo_exportes import ICT, paquete
from apoyo_tokens import cabecera, validador
from fastapi.testclient import TestClient

from atlas_docs.config import Configuracion
from atlas_docs.main import crear_app

CONFIG = Configuracion(
    AMBIENTE="local",
    DOCS_S3_ENDPOINT="http://s3.ejemplo.com",
    DOCS_S3_ACCESS_KEY="x",
    DOCS_S3_SECRET_KEY="x",
    DOCS_S3_BUCKET="atlas",
)
EXPORTACION = "0199f0a0-7c3e-7b1a-9d4e-2f6a8b0c1d2e"


class S3Falso:
    def __init__(self) -> None:
        self.objetos: dict[str, dict] = {}

    def put_object(self, **kwargs: object) -> None:
        self.objetos[str(kwargs["Key"])] = kwargs


def _cuerpo() -> dict:
    return paquete(("core-ict", "1.1.0", ICT)).model_dump(mode="json")


def test_responde_el_zip_con_el_hash() -> None:
    cliente = TestClient(crear_app(CONFIG, [], S3Falso(), validador(), consumidor=False))  # type: ignore[arg-type]

    respuesta = cliente.post("/exportes", params={"formatos": ["html", "excel"]}, json=_cuerpo(), headers=cabecera())

    assert respuesta.status_code == 200
    assert respuesta.headers["content-type"] == "application/zip"
    assert len(respuesta.headers["x-atlas-hash-contenido"]) == 64
    with zipfile.ZipFile(io.BytesIO(respuesta.content)) as archivo:
        nombres = archivo.namelist()
    assert any(n.endswith(".html") for n in nombres) and any(n.endswith(".xlsx") for n in nombres)
    assert any(n.endswith("LEEME.md") for n in nombres)


def test_con_exportacion_id_deja_los_archivos_en_el_s3() -> None:
    s3 = S3Falso()
    cliente = TestClient(crear_app(CONFIG, [], s3, validador(), consumidor=False))  # type: ignore[arg-type]

    respuesta = cliente.post(
        "/exportes",
        params={"formatos": ["openapi", "postman"], "exportacionId": EXPORTACION},
        json=_cuerpo(),
        headers=cabecera(),
    )

    assert respuesta.status_code == 201
    cuerpo = respuesta.json()
    claves = [a["clave"] for a in cuerpo["archivos"]]
    assert claves == list(s3.objetos)
    assert all(c.startswith(f"exportaciones/{EXPORTACION}/") for c in claves)
    postman = next(o for c, o in s3.objetos.items() if c.endswith(".postman_collection.json"))
    assert postman["Bucket"] == "atlas" and postman["ContentType"] == "application/json"
    assert postman["Metadata"]["sha256"] in {a["sha256"] for a in cuerpo["archivos"]}


def test_spec_invalido_responde_422() -> None:
    cliente = TestClient(crear_app(CONFIG, [], S3Falso(), validador(), consumidor=False))  # type: ignore[arg-type]
    cuerpo = _cuerpo()
    cuerpo["specs"][0]["contenido"] = "esto: no es un contrato"

    respuesta = cliente.post("/exportes", params={"formatos": ["html"]}, json=cuerpo, headers=cabecera())

    assert respuesta.status_code == 422
    assert respuesta.json()["codigo"] == "spec_invalido"


def test_exportacion_id_que_no_es_uuid_responde_422() -> None:
    cliente = TestClient(crear_app(CONFIG, [], S3Falso(), validador(), consumidor=False))  # type: ignore[arg-type]

    respuesta = cliente.post("/exportes", params={"exportacionId": "../../otro"}, json=_cuerpo(), headers=cabecera())

    assert respuesta.status_code == 422
