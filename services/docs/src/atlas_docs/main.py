"""Punto de entrada ASGI de atlas-docs: `uvicorn --factory atlas_docs.main:crear_app`.

Con `--factory`, la configuración se valida al arrancar el servidor y no al importar el módulo.
"""

from collections.abc import AsyncIterator, Awaitable, Callable
from contextlib import asynccontextmanager
from typing import Annotated

import anyio
from botocore.client import BaseClient
from fastapi import FastAPI, Query, Response
from fastapi.responses import JSONResponse, PlainTextResponse

from atlas_docs.almacenamiento import asegurar_bucket, crear_cliente, subir
from atlas_docs.config import Configuracion, cargar
from atlas_docs.exportes import Formato, PaqueteExportar, ResultadoExportacion, exportar
from atlas_docs.exportes.exportar import tipo_mime
from atlas_docs.exportes.pdf import WeasyPrintNoDisponibleError
from atlas_docs.exportes.specs import SpecInvalidoError

Chequeo = Callable[[], Awaitable[None]]

PATRON_UUID = r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
# Una exportación a la vez por pod: WeasyPrint y openpyxl son de CPU y memoria (plan 00 §8.7, capacidad de la 169).
CONCURRENCIA_EXPORTES = 1


def chequeo_bucket(config: Configuracion) -> Chequeo:
    """El bucket existe y las credenciales sirven. head_bucket no exige permiso para listar todos los buckets."""
    cliente = crear_cliente(config)

    async def chequear() -> None:
        await anyio.to_thread.run_sync(lambda: cliente.head_bucket(Bucket=config.DOCS_S3_BUCKET))

    return chequear


def _problema(status: int, titulo: str, codigo: str) -> JSONResponse:
    return JSONResponse(
        {"status": status, "title": titulo, "codigo": codigo}, status_code=status, media_type="application/problem+json"
    )


def crear_app(
    config: Configuracion | None = None,
    chequeos: list[Chequeo] | None = None,
    cliente_s3: BaseClient | None = None,
) -> FastAPI:
    config = config or cargar()
    chequeos = chequeos if chequeos is not None else [chequeo_bucket(config)]
    limitador = anyio.CapacityLimiter(CONCURRENCIA_EXPORTES)
    s3: dict[str, BaseClient] = {"cliente": cliente_s3} if cliente_s3 is not None else {}

    def almacen() -> BaseClient:
        if "cliente" not in s3:
            s3["cliente"] = crear_cliente(config)
        return s3["cliente"]

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

    @app.post("/exportes", response_model=None)
    async def exportes(
        paquete: PaqueteExportar,
        formatos: Annotated[list[Formato] | None, Query()] = None,
        exportacion_id: Annotated[str | None, Query(alias="exportacionId", pattern=PATRON_UUID)] = None,
    ) -> Response:
        """Exportación interna (HU 4.5, ATLAS-ADR-0042). Solo la llama atlas-api, por la red del clúster.

        Sin `exportacionId` responde el ZIP. Con `exportacionId` deja cada archivo en el S3 bajo
        `exportaciones/<exportacionId>/` y responde las claves con su SHA-256 (lo que usará el consumidor de la cola
        `atlas.exportacion.solicitada` en la ola 2).
        """
        pedidos = set(formatos) if formatos else set(Formato)
        if exportacion_id is None:
            pedidos.add(Formato.ZIP)
        try:
            resultado: ResultadoExportacion = await anyio.to_thread.run_sync(
                lambda: exportar(paquete, pedidos), limiter=limitador
            )
        except SpecInvalidoError as error:
            return _problema(422, str(error), "spec_invalido")
        except WeasyPrintNoDisponibleError:
            return _problema(503, "El PDF no está disponible en este nodo (falta Pango).", "pdf_no_disponible")

        if exportacion_id is None:
            archivo = resultado.zip
            if archivo is None:  # pragma: no cover — el ZIP se agregó a los pedidos arriba
                return _problema(500, "La exportación no generó el ZIP.", "sin_zip")
            return Response(
                archivo.datos,
                media_type="application/zip",
                headers={
                    "Content-Disposition": f'attachment; filename="{archivo.ruta}"',
                    "X-Atlas-Hash-Contenido": resultado.hash_contenido,
                },
            )

        cliente = almacen()
        objetos = []
        for a in resultado.archivos:
            clave = f"exportaciones/{exportacion_id}/{a.ruta}"
            tipo = tipo_mime(a.ruta)
            await anyio.to_thread.run_sync(
                lambda a=a, clave=clave, tipo=tipo: subir(
                    cliente, config.DOCS_S3_BUCKET, clave, a.datos, tipo, a.sha256
                )
            )
            objetos.append(
                {"clave": clave, "formato": a.formato.value, "bytes": len(a.datos), "sha256": a.sha256, "tipo": tipo}
            )
        return JSONResponse(
            {"exportacionId": exportacion_id, "hashContenido": resultado.hash_contenido, "archivos": objetos},
            status_code=201,
        )

    return app
