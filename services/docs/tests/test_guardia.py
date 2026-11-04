"""F4-SEC-12: atlas-docs valida el token antes de leer el cuerpo de `POST /exportes` y le pone tope de tamaño."""

import json
from typing import Any

import anyio
from apoyo_tokens import token, validador
from fastapi.testclient import TestClient

from atlas_docs.config import Configuracion
from atlas_docs.exportes.modelo import MAX_BYTES_ENTRADA
from atlas_docs.main import crear_app

CONFIG = Configuracion(
    AMBIENTE="local",
    DOCS_S3_ENDPOINT="http://s3.ejemplo.com",
    DOCS_S3_ACCESS_KEY="x",
    DOCS_S3_SECRET_KEY="x",
    DOCS_S3_BUCKET="atlas",
)


class Llamada:
    """Una petición ASGI a mano: así se puede anunciar un cuerpo enorme y saber si alguien lo leyó."""

    def __init__(self, cabeceras: dict[str, str], trozos: list[bytes] | None = None) -> None:
        self.cabeceras = cabeceras
        self.trozos = list(trozos or [])
        self.lecturas = 0
        self.enviados: list[dict[str, Any]] = []

    async def recibir(self) -> dict[str, Any]:
        self.lecturas += 1
        if not self.trozos:
            return {"type": "http.request", "body": b"", "more_body": False}
        trozo = self.trozos.pop(0)
        return {"type": "http.request", "body": trozo, "more_body": bool(self.trozos)}

    async def enviar(self, mensaje: dict[str, Any]) -> None:
        self.enviados.append(mensaje)

    @property
    def estado(self) -> int:
        return next(m["status"] for m in self.enviados if m["type"] == "http.response.start")

    @property
    def codigo(self) -> str:
        cuerpo = b"".join(m.get("body", b"") for m in self.enviados if m["type"] == "http.response.body")
        return json.loads(cuerpo).get("codigo", "")

    def correr(self) -> "Llamada":
        app = crear_app(CONFIG, [], object(), validador(), consumidor=False)  # type: ignore[arg-type]
        scope = {
            "type": "http",
            "asgi": {"version": "3.0"},
            "http_version": "1.1",
            "method": "POST",
            "scheme": "http",
            "path": "/exportes",
            "raw_path": b"/exportes",
            "query_string": b"",
            "root_path": "",
            "headers": [(k.lower().encode(), v.encode()) for k, v in self.cabeceras.items()],
            "client": ("127.0.0.1", 1),
            "server": ("127.0.0.1", 80),
        }
        anyio.run(app, scope, self.recibir, self.enviar)
        return self


ENORME = str(10 * 1024**3)  # 10 GiB anunciados


def test_sin_token_responde_401_sin_leer_el_cuerpo() -> None:
    llamada = Llamada({"Content-Length": ENORME, "Content-Type": "application/json"}, [b"{" * 1024]).correr()

    assert llamada.estado == 401
    assert llamada.lecturas == 0


def test_con_un_token_invalido_responde_401_sin_leer_el_cuerpo() -> None:
    llamada = Llamada({"Authorization": "Bearer x.y.z", "Content-Length": ENORME}, [b"{"]).correr()

    assert llamada.estado == 401
    assert llamada.lecturas == 0


def test_sin_el_scope_responde_403_sin_leer_el_cuerpo() -> None:
    llamada = Llamada({"Authorization": f"Bearer {token(scope='atlas.mcp')}", "Content-Length": "2"}, [b"{}"]).correr()

    assert llamada.estado == 403
    assert llamada.lecturas == 0


def test_con_token_y_sin_content_length_responde_411() -> None:
    llamada = Llamada({"Authorization": f"Bearer {token()}", "Transfer-Encoding": "chunked"}, [b"{}"]).correr()

    assert llamada.estado == 411
    assert llamada.lecturas == 0


def test_con_token_y_un_content_length_mayor_que_el_tope_responde_413_sin_leer() -> None:
    llamada = Llamada(
        {"Authorization": f"Bearer {token()}", "Content-Length": str(MAX_BYTES_ENTRADA + 1)}, [b"{"]
    ).correr()

    assert llamada.estado == 413
    assert llamada.codigo == "cuerpo_demasiado_grande"
    assert llamada.lecturas == 0


def test_si_llegan_mas_bytes_que_los_anunciados_corta_con_413() -> None:
    trozos = [b'{"metadatos": ', b"x" * 4096, b"x" * 4096]
    llamada = Llamada(
        {"Authorization": f"Bearer {token()}", "Content-Length": "100", "Content-Type": "application/json"}, trozos
    ).correr()

    assert llamada.estado == 413
    assert llamada.codigo == "cuerpo_demasiado_grande"
    assert llamada.lecturas < 3


def test_las_demas_rutas_no_pasan_por_la_guardia() -> None:
    app = crear_app(CONFIG, [], object(), validador(), consumidor=False)  # type: ignore[arg-type]

    assert TestClient(app).get("/health/live").status_code == 200
