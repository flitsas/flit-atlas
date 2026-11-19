"""Punto de entrada ASGI de atlas-ia: `uvicorn --factory atlas_ia.main:crear_app`.

Con `--factory`, la configuración se valida al arrancar el servidor y no al importar el módulo.
"""

from collections.abc import Awaitable, Callable

import psycopg
from fastapi import FastAPI
from fastapi.responses import PlainTextResponse

from atlas_ia.config import Configuracion, cargar

Chequeo = Callable[[], Awaitable[None]]


def chequeo_base_de_datos(config: Configuracion) -> Chequeo:
    """La base responde y tiene la extensión vector (la crea el administrador, prompt maestro A-06)."""

    async def chequear() -> None:
        async with await psycopg.AsyncConnection.connect(config.IA_DATABASE_URL, connect_timeout=3) as conexion:
            cursor = await conexion.execute("SELECT 1 FROM pg_extension WHERE extname = 'vector'")
            if await cursor.fetchone() is None:
                raise RuntimeError("la extensión vector no está instalada")

    return chequear


def crear_app(config: Configuracion | None = None, chequeos: list[Chequeo] | None = None) -> FastAPI:
    config = config or cargar()
    chequeos = chequeos if chequeos is not None else [chequeo_base_de_datos(config)]

    app = FastAPI(title="atlas-ia", docs_url=None, redoc_url=None, openapi_url=None)

    @app.get("/health/live", response_class=PlainTextResponse)
    async def vida() -> str:
        return "Healthy"

    @app.get("/health/ready", response_class=PlainTextResponse)
    async def listo() -> PlainTextResponse:
        for chequeo in chequeos:
            try:
                await chequeo()
            except Exception:  # noqa: BLE001 — cualquier fallo de una dependencia es "no listo"
                return PlainTextResponse("Unhealthy", status_code=503)
        return PlainTextResponse("Healthy")

    return app
