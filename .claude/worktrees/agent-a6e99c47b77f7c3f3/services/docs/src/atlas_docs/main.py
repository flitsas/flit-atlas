"""Punto de entrada ASGI de atlas-docs: `uvicorn --factory atlas_docs.main:crear_app`.

Con `--factory`, la configuración se valida al arrancar el servidor y no al importar el módulo.
"""

from collections.abc import AsyncIterator, Awaitable, Callable
from contextlib import asynccontextmanager

import anyio
from fastapi import FastAPI
from fastapi.responses import PlainTextResponse

from atlas_docs.almacenamiento import asegurar_bucket, crear_cliente
from atlas_docs.config import Configuracion, cargar

Chequeo = Callable[[], Awaitable[None]]


def chequeo_bucket(config: Configuracion) -> Chequeo:
    """El bucket existe y las credenciales sirven. head_bucket no exige permiso para listar todos los buckets."""
    cliente = crear_cliente(config)

    async def chequear() -> None:
        await anyio.to_thread.run_sync(lambda: cliente.head_bucket(Bucket=config.DOCS_S3_BUCKET))

    return chequear


def crear_app(config: Configuracion | None = None, chequeos: list[Chequeo] | None = None) -> FastAPI:
    config = config or cargar()
    chequeos = chequeos if chequeos is not None else [chequeo_bucket(config)]

    @asynccontextmanager
    async def ciclo_de_vida(_: FastAPI) -> AsyncIterator[None]:
        if config.DOCS_S3_CREAR_BUCKET:
            await anyio.to_thread.run_sync(lambda: asegurar_bucket(crear_cliente(config), config.DOCS_S3_BUCKET))
        yield

    app = FastAPI(title="atlas-docs", docs_url=None, redoc_url=None, openapi_url=None, lifespan=ciclo_de_vida)

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
