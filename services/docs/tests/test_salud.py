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


async def _ok() -> None:
    return None


async def _falla() -> None:
    raise ConnectionError("sin S3")


def test_vida_no_depende_del_almacenamiento() -> None:
    cliente = TestClient(crear_app(CONFIG, [_falla]))

    respuesta = cliente.get("/health/live")

    assert respuesta.status_code == 200
    assert respuesta.text == "Healthy"


def test_listo_responde_503_si_una_dependencia_falla() -> None:
    cliente = TestClient(crear_app(CONFIG, [_ok, _falla]))

    assert cliente.get("/health/ready").status_code == 503


def test_listo_responde_200_si_todo_responde() -> None:
    cliente = TestClient(crear_app(CONFIG, [_ok]))

    assert cliente.get("/health/ready").text == "Healthy"
