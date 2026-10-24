"""Cola de exportaciones (HU 4.5, ATLAS-ADR-0042 y ATLAS-ADR-0018).

atlas-api publica `atlas.contratos.exportacion_solicitada` en `flit.atlas` (desde su outbox) después de dejar el
«paquete a exportar» en el S3. atlas-docs lo consume de `atlas.docs.exportaciones` (una a la vez por pod), genera los
formatos, los sube a `exportaciones/<exportacionId>/` y publica el resultado en su propio exchange `flit.atlas.docs`:
`atlas.contratos.exportacion_iniciada`, `atlas.contratos.exportacion_lista` o `atlas.contratos.exportacion_fallida`.
Solo atlas-api publica en `flit.atlas`.

Entrega al menos una vez: la salida es determinista, así que una reentrega regenera los mismos bytes en las mismas
claves y vuelve a publicar el resultado; atlas-api ignora el resultado de una exportación ya terminada. Un fallo de
infraestructura (S3 o el broker) se reintenta a los 30 s por la cola de reintentos; a la quinta vez el mensaje pasa a
la cola de errores. Un error del contenido (spec inválido, entrada ilegible) no se reintenta: se publica `fallida`.
"""

import asyncio
import json
import logging
import os
import re
import time
import uuid
from collections.abc import Awaitable, Callable
from datetime import UTC, datetime
from typing import Annotated, Any, Protocol

import aio_pika
import anyio
from aio_pika.abc import AbstractIncomingMessage, AbstractRobustConnection
from botocore.client import BaseClient
from pydantic import BaseModel, ConfigDict, Field, StringConstraints, ValidationError

from atlas_docs.almacenamiento import leer, subir_resultado
from atlas_docs.exportes import Formato, PaqueteExportar, exportar
from atlas_docs.exportes.pdf import WeasyPrintNoDisponibleError
from atlas_docs.exportes.specs import SpecInvalidoError

log = logging.getLogger(__name__)

PATRON_UUID = r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"

PRODUCTOR = "atlas-docs"
EXCHANGE_ATLAS = "flit.atlas"
EXCHANGE_DOCS = "flit.atlas.docs"
COLA = "atlas.docs.exportaciones"
COLA_REINTENTOS = COLA + ".reintentos"
COLA_ERRORES = COLA + ".errores"
ESPERA_REINTENTO_MS = 30_000
MAXIMO_MUERTES = 5

SOLICITADA = "atlas.contratos.exportacion_solicitada"
INICIADA = "atlas.contratos.exportacion_iniciada"
LISTA = "atlas.contratos.exportacion_lista"
FALLIDA = "atlas.contratos.exportacion_fallida"
SIN_ORGANIZACION = "00000000-0000-0000-0000-000000000000"


def uuid7() -> str:
    """uuid v7 (RFC 9562): 48 bits de milisegundos Unix y el resto al azar. Python 3.13 aún no lo trae."""
    milisegundos = time.time_ns() // 1_000_000
    azar = int.from_bytes(os.urandom(10), "big")
    valor = (milisegundos & ((1 << 48) - 1)) << 80
    valor |= 0x7 << 76  # versión
    valor |= ((azar >> 62) & 0xFFF) << 64  # rand_a (12 bits)
    valor |= 0b10 << 62  # variante
    valor |= azar & ((1 << 62) - 1)  # rand_b (62 bits)
    return str(uuid.UUID(int=valor))


def sobre(tipo: str, datos: dict[str, Any], tenant_id: str, correlacion: str) -> dict[str, Any]:
    """El sobre común de Atlas (SobreEvento de atlas-api), en camelCase."""
    return {
        "eventId": uuid7(),
        "type": tipo,
        "version": 1,
        "occurredAt": datetime.now(UTC).isoformat(),
        "tenantId": tenant_id,
        "producer": PRODUCTOR,
        "correlationId": correlacion,
        "data": datos,
    }


class Publicador(Protocol):
    async def publicar(self, evento: dict[str, Any]) -> None: ...


class MensajeIlegibleError(Exception):
    """El mensaje no trae ni siquiera un exportacionId válido: va directo a la cola de errores."""


# --- Mensaje de entrada ---

Uuid = Annotated[str, StringConstraints(pattern=PATRON_UUID)]


class DatosSolicitada(BaseModel):
    """`data` de `atlas.contratos.exportacion_solicitada` v1. Campos extra se ignoran (compatibilidad futura)."""

    model_config = ConfigDict(extra="ignore")

    exportacion_id: Uuid = Field(alias="exportacionId")
    formatos: list[Formato] = Field(min_length=1)
    entrada_clave: str = Field(alias="entradaClave")
    origen: dict[str, Any] | None = None


class SobreSolicitado(BaseModel):
    model_config = ConfigDict(extra="ignore")

    type: str
    tenant_id: str = Field(default=SIN_ORGANIZACION, alias="tenantId")
    correlation_id: str = Field(default="", alias="correlationId")
    data: dict[str, Any]


def clave_entrada(exportacion_id: str) -> str:
    return f"exportaciones/{exportacion_id}/entrada.json"


class ManejadorExportaciones:
    """Procesa una `atlas.contratos.exportacion_solicitada` sin saber del broker: recibe el cuerpo y publica con
    `publicador`.

    Lanza una excepción solo ante fallos de infraestructura (para reintentar) y `MensajeIlegibleError` si el mensaje no
    se puede atribuir a una exportación.
    """

    def __init__(
        self,
        cliente_s3: Callable[[], BaseClient],
        bucket: str,
        publicador: Publicador,
        limitador: anyio.CapacityLimiter,
    ) -> None:
        self._s3 = cliente_s3
        self._bucket = bucket
        self._publicador = publicador
        self._limitador = limitador

    async def procesar(self, cuerpo: bytes) -> None:
        try:
            recibido = SobreSolicitado.model_validate_json(cuerpo)
        except ValidationError as error:
            raise MensajeIlegibleError("El mensaje no es un sobre de Atlas.") from error
        if recibido.type != SOLICITADA:
            raise MensajeIlegibleError(f"Tipo inesperado en la cola: {recibido.type[:100]}")
        exportacion_id = recibido.data.get("exportacionId")
        if not isinstance(exportacion_id, str) or not re.match(PATRON_UUID, exportacion_id):
            raise MensajeIlegibleError("El mensaje no trae un exportacionId válido.")

        async def publicar(tipo: str, datos: dict[str, Any]) -> None:
            await self._publicador.publicar(sobre(tipo, datos, recibido.tenant_id, recibido.correlation_id))

        async def fallar(codigo: str, mensaje: str) -> None:
            log.info("Exportación %s fallida: %s", exportacion_id, codigo)
            await publicar(FALLIDA, {"exportacionId": exportacion_id, "codigo": codigo, "mensaje": mensaje})

        try:
            datos = DatosSolicitada.model_validate(recibido.data)
        except ValidationError:
            await fallar("entrada_invalida", "La solicitud de exportación no es válida (formatos o datos).")
            return
        if datos.entrada_clave != clave_entrada(exportacion_id):
            # Nunca se lee otra clave del bucket: la entrada vive junto a su exportación.
            await fallar("entrada_invalida", "La clave de la entrada no corresponde a la exportación.")
            return

        await publicar(INICIADA, {"exportacionId": exportacion_id})
        cliente = self._s3()
        contenido = await anyio.to_thread.run_sync(lambda: leer(cliente, self._bucket, datos.entrada_clave))
        if contenido is None:
            await fallar("entrada_invalida", "La entrada de la exportación no está en el almacenamiento.")
            return
        try:
            paquete = PaqueteExportar.model_validate_json(contenido)
        except ValidationError as error:
            # Solo los campos con error, nunca los valores (pueden traer el spec).
            campos = sorted({".".join(str(p) for p in e["loc"]) for e in error.errors()})[:5]
            await fallar("entrada_invalida", "La entrada de la exportación no es válida: " + ", ".join(campos))
            return

        try:
            resultado = await anyio.to_thread.run_sync(
                lambda: exportar(paquete, set(datos.formatos)), limiter=self._limitador
            )
        except SpecInvalidoError as error:
            await fallar("spec_invalido", str(error)[:500])
            return
        except WeasyPrintNoDisponibleError:
            await fallar("pdf_no_disponible", "El PDF no está disponible en este nodo (falta Pango).")
            return

        archivos = await anyio.to_thread.run_sync(
            lambda: subir_resultado(cliente, self._bucket, exportacion_id, resultado)
        )
        await publicar(
            LISTA,
            {"exportacionId": exportacion_id, "hashContenido": resultado.hash_contenido, "archivos": archivos},
        )
        log.info("Exportación %s lista: %d archivos", exportacion_id, len(archivos))


# --- Broker ---


def muertes(cabeceras: dict[str, Any] | None) -> int:
    """Veces que el mensaje murió en la cola principal (cabecera `x-death` de RabbitMQ)."""
    for entrada in (cabeceras or {}).get("x-death") or []:
        if isinstance(entrada, dict) and entrada.get("queue") == COLA:
            try:
                return int(entrada.get("count", 0))
            except (TypeError, ValueError):
                return 0
    return 0


class PublicadorAmqp:
    """Publica en `flit.atlas.docs` con confirmación del broker (el canal tiene publisher confirms)."""

    def __init__(self, exchange: aio_pika.abc.AbstractExchange) -> None:
        self._exchange = exchange

    async def publicar(self, evento: dict[str, Any]) -> None:
        mensaje = aio_pika.Message(
            json.dumps(evento, ensure_ascii=False).encode("utf-8"),
            message_id=evento["eventId"],
            type=evento["type"],
            content_type="application/json",
            content_encoding="utf-8",
            timestamp=datetime.now(UTC),
            delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
        )
        await self._exchange.publish(mensaje, routing_key=evento["type"])


class ConsumidorExportaciones:
    """Conexión robusta a RabbitMQ, topología y consumo con prefetch 1."""

    def __init__(
        self,
        url: str,
        crear_manejador: Callable[[Publicador], ManejadorExportaciones],
        conectar: Callable[[str], Awaitable[AbstractRobustConnection]] = aio_pika.connect_robust,
    ) -> None:
        self._url = url
        self._crear_manejador = crear_manejador
        self._conectar = conectar
        self._conexion: AbstractRobustConnection | None = None
        self._tarea: asyncio.Task[None] | None = None

    @property
    def conectado(self) -> bool:
        return self._conexion is not None and not self._conexion.is_closed

    async def iniciar(self) -> None:
        self._tarea = asyncio.create_task(self._correr())

    async def _correr(self) -> None:
        espera = 1.0
        while True:
            try:
                self._conexion = await self._conectar(self._url)
                await self._suscribir(self._conexion)
                return
            except Exception:  # noqa: BLE001 — el broker puede no estar al arrancar: se reintenta sin morir
                log.warning("RabbitMQ no responde o rechazó la topología; se reintenta en %.0f s", espera)
                if self._conexion is not None:
                    await self._conexion.close()
                    self._conexion = None
                await asyncio.sleep(espera)
                espera = min(espera * 2, 60)

    async def _suscribir(self, conexion: AbstractRobustConnection) -> None:
        canal = await conexion.channel(publisher_confirms=True)
        await canal.set_qos(prefetch_count=1)
        atlas = await canal.declare_exchange(EXCHANGE_ATLAS, aio_pika.ExchangeType.TOPIC, durable=True)
        docs = await canal.declare_exchange(EXCHANGE_DOCS, aio_pika.ExchangeType.TOPIC, durable=True)
        reintentos = await canal.declare_exchange(COLA_REINTENTOS, aio_pika.ExchangeType.FANOUT, durable=True)
        cola = await canal.declare_queue(COLA, durable=True, arguments={"x-dead-letter-exchange": COLA_REINTENTOS})
        await cola.bind(atlas, routing_key=SOLICITADA)
        espera_cola = await canal.declare_queue(
            COLA_REINTENTOS,
            durable=True,
            arguments={
                "x-message-ttl": ESPERA_REINTENTO_MS,
                "x-dead-letter-exchange": "",
                "x-dead-letter-routing-key": COLA,
            },
        )
        await espera_cola.bind(reintentos)
        await canal.declare_queue(COLA_ERRORES, durable=True)
        manejador = self._crear_manejador(PublicadorAmqp(docs))

        async def al_recibir(mensaje: AbstractIncomingMessage) -> None:
            await atender(mensaje, manejador, canal.default_exchange)

        await cola.consume(al_recibir)

    async def detener(self) -> None:
        if self._tarea is not None:
            self._tarea.cancel()
            try:
                await self._tarea
            except asyncio.CancelledError:
                pass  # noqa: S110 — es lo esperado al cancelarla
            except Exception:  # noqa: BLE001 — al cerrar no importa cómo terminó, pero queda en el log
                log.warning("El consumidor terminó con error al cerrar", exc_info=True)
        if self._conexion is not None:
            await self._conexion.close()


async def atender(mensaje: AbstractIncomingMessage, manejador: ManejadorExportaciones, por_defecto: Any) -> None:
    """Procesa, confirma o devuelve un mensaje según el resultado."""
    try:
        await manejador.procesar(mensaje.body)
    except MensajeIlegibleError as error:
        log.warning("Mensaje ilegible en %s: %s", COLA, error)
        await _a_errores(mensaje, por_defecto)
        return
    except Exception:  # noqa: BLE001 — fallo de infraestructura: reintento o errores
        veces = muertes(mensaje.headers)
        if veces + 1 >= MAXIMO_MUERTES:
            log.exception("La exportación falló %d veces; pasa a %s", veces + 1, COLA_ERRORES)
            await _a_errores(mensaje, por_defecto)
        else:
            log.exception("Fallo de infraestructura; se reintenta en %d s", ESPERA_REINTENTO_MS // 1000)
            await mensaje.nack(requeue=False)
        return
    await mensaje.ack()


async def _a_errores(mensaje: AbstractIncomingMessage, por_defecto: Any) -> None:
    copia = aio_pika.Message(
        mensaje.body,
        headers=dict(mensaje.headers or {}),
        message_id=mensaje.message_id,
        type=mensaje.type,
        content_type=mensaje.content_type,
        content_encoding=mensaje.content_encoding,
        delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
    )
    await por_defecto.publish(copia, routing_key=COLA_ERRORES)
    await mensaje.ack()
