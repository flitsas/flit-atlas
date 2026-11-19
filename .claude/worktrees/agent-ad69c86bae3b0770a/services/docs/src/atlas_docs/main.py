"""Punto de entrada ASGI de atlas-docs: `uvicorn --factory atlas_docs.main:crear_app`.

Con `--factory`, la configuración se valida al arrancar el servidor y no al importar el módulo.
"""

from collections.abc import AsyncIterator, Awaitable, Callable
from contextlib import asynccontextmanager
from typing import Annotated

import anyio
from botocore.client import BaseClient
from fastapi import Depends, FastAPI, Header, Query, Response
from fastapi.responses import JSONResponse, PlainTextResponse

from atlas_docs.almacenamiento import asegurar_bucket, crear_cliente, subir_resultado
from atlas_docs.autenticacion import ScopeInsuficienteError, TokenInvalidoError, ValidadorTokens
from atlas_docs.cola import PATRON_UUID, ConsumidorExportaciones, ManejadorExportaciones, Publicador
from atlas_docs.config import Configuracion, cargar
from atlas_docs.exportes import Formato, PaqueteExportar, ResultadoExportacion, exportar
from atlas_docs.exportes.pdf import WeasyPrintNoDisponibleError
from atlas_docs.exportes.specs import SpecInvalidoError

Chequeo = Callable[[], Awaitable[None]]

# Una exportación a la vez por pod: WeasyPrint y openpyxl son de CPU y memoria (plan 00 §8.7, capacidad de la 169).
# El límite lo comparten el endpoint y el consumidor de la cola.
CONCURRENCIA_EXPORTES = 1


def chequeo_bucket(config: Configuracion) -> Chequeo:
    """El bucket existe y las credenciales sirven. head_bucket no exige permiso para listar todos los buckets."""
    cliente = crear_cliente(config)

    async def chequear() -> None:
        await anyio.to_thread.run_sync(lambda: cliente.head_bucket(Bucket=config.DOCS_S3_BUCKET))

    return chequear


def chequeo_cola(consumidor: ConsumidorExportaciones) -> Chequeo:
    """La conexión con RabbitMQ está abierta (si se cae, connect_robust la reabre; mientras tanto, no listo)."""

    async def chequear() -> None:
        if not consumidor.conectado:
            raise ConnectionError("Sin conexión con RabbitMQ")

    return chequear


def _problema(status: int, titulo: str, codigo: str, cabeceras: dict[str, str] | None = None) -> JSONResponse:
    return JSONResponse(
        {"status": status, "title": titulo, "codigo": codigo},
        status_code=status,
        media_type="application/problem+json",
        headers=cabeceras,
    )


class _Rechazo(Exception):
    def __init__(self, respuesta: JSONResponse) -> None:
        self.respuesta = respuesta


def crear_app(
    config: Configuracion | None = None,
    chequeos: list[Chequeo] | None = None,
    cliente_s3: BaseClient | None = None,
    validador: ValidadorTokens | None = None,
    consumidor: ConsumidorExportaciones | bool | None = None,
) -> FastAPI:
    """Arma la app. En las pruebas se inyectan los chequeos, el cliente S3, el validador de tokens y el consumidor.

    `consumidor`: `None` lo crea si hay `ATLAS_DOCS_RABBITMQ_URL`; `False` lo apaga; o un consumidor ya armado.
    """
    config = config or cargar()
    limitador = anyio.CapacityLimiter(CONCURRENCIA_EXPORTES)
    s3: dict[str, BaseClient] = {"cliente": cliente_s3} if cliente_s3 is not None else {}
    validador = validador or ValidadorTokens.desde_jwks(
        config.DOCS_AUTH_JWKS_URL, config.DOCS_AUTH_EMISOR, config.DOCS_AUTH_AUDIENCIA, config.DOCS_AUTH_SCOPE
    )

    def almacen() -> BaseClient:
        if "cliente" not in s3:
            s3["cliente"] = crear_cliente(config)
        return s3["cliente"]

    def crear_manejador(publicador: Publicador) -> ManejadorExportaciones:
        return ManejadorExportaciones(almacen, config.DOCS_S3_BUCKET, publicador, limitador)

    cola: ConsumidorExportaciones | None = None
    if isinstance(consumidor, ConsumidorExportaciones):
        cola = consumidor
    elif consumidor is None and config.DOCS_RABBITMQ_URL is not None:
        cola = ConsumidorExportaciones(config.DOCS_RABBITMQ_URL.get_secret_value(), crear_manejador)

    if chequeos is None:
        chequeos = [chequeo_bucket(config)] + ([chequeo_cola(cola)] if cola is not None else [])

    @asynccontextmanager
    async def ciclo_de_vida(_: FastAPI) -> AsyncIterator[None]:
        if config.DOCS_S3_CREAR_BUCKET:
            await anyio.to_thread.run_sync(lambda: asegurar_bucket(crear_cliente(config), config.DOCS_S3_BUCKET))
        if cola is not None:
            await cola.iniciar()
        try:
            yield
        finally:
            if cola is not None:
                await cola.detener()

    app = FastAPI(title="atlas-docs", docs_url=None, redoc_url=None, openapi_url=None, lifespan=ciclo_de_vida)

    @app.exception_handler(_Rechazo)
    async def _rechazar(_: object, error: _Rechazo) -> JSONResponse:
        return error.respuesta

    async def token_de_servicio(authorization: Annotated[str | None, Header()] = None) -> None:
        """Token client_credentials de OpenIddict con audiencia atlas-docs (ATLAS-ADR-0044)."""
        try:
            await validador.validar_cabecera(authorization)
        except TokenInvalidoError:
            raise _Rechazo(
                _problema(401, "Falta un token de servicio válido.", "token_invalido", {"WWW-Authenticate": "Bearer"})
            ) from None
        except ScopeInsuficienteError:
            raise _Rechazo(
                _problema(403, "El token no autoriza exportar.", "scope_insuficiente", {"WWW-Authenticate": "Bearer"})
            ) from None

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

    @app.post("/exportes", response_model=None, dependencies=[Depends(token_de_servicio)])
    async def exportes(
        paquete: PaqueteExportar,
        formatos: Annotated[list[Formato] | None, Query()] = None,
        exportacion_id: Annotated[str | None, Query(alias="exportacionId", pattern=PATRON_UUID)] = None,
    ) -> Response:
        """Exportación interna síncrona (HU 4.5, ATLAS-ADR-0042), con token de servicio (ATLAS-ADR-0044).

        La vía normal es la cola (`atlas.contratos.exportacion_solicitada`); este endpoint queda para diagnósticos y
        paquetes pequeños. Sin `exportacionId` responde el ZIP. Con `exportacionId` deja cada archivo en el S3 bajo
        `exportaciones/<exportacionId>/` y responde las claves con su SHA-256.
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
        objetos = await anyio.to_thread.run_sync(
            lambda: subir_resultado(cliente, config.DOCS_S3_BUCKET, exportacion_id, resultado)
        )
        return JSONResponse(
            {"exportacionId": exportacion_id, "hashContenido": resultado.hash_contenido, "archivos": objetos},
            status_code=201,
        )

    return app
