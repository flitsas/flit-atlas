"""Consumidor de `atlas.contratos.exportacion_solicitada` (HU 4.5, ATLAS-ADR-0042), con S3 y publicador falsos."""

import asyncio
import hashlib
import io
import json
import re
from typing import Any

import anyio
import pytest
from apoyo_exportes import ICT, paquete, requiere_pdf
from botocore.exceptions import ClientError

from atlas_docs import cola
from atlas_docs.cola import ManejadorExportaciones, MensajeIlegibleError, atender, muertes, uuid7

EXPORTACION = "0199f0a0-7c3e-7b1a-9d4e-2f6a8b0c1d2e"
ORGANIZACION = "0199f0a0-0000-7000-8000-00000000abcd"
CLAVE = f"exportaciones/{EXPORTACION}/entrada.json"
TODOS = ["openapi", "postman", "html", "pdf", "word", "excel", "zip"]


class S3Falso:
    def __init__(self) -> None:
        self.objetos: dict[str, dict] = {}
        self.caido = False

    def put_object(self, **kwargs: Any) -> None:
        if self.caido:
            raise ConnectionError("S3 caído")
        self.objetos[str(kwargs["Key"])] = kwargs

    def get_object(self, Bucket: str, Key: str) -> dict:  # noqa: N803 — firma de boto3
        if self.caido:
            raise ConnectionError("S3 caído")
        if Key not in self.objetos:
            raise ClientError({"Error": {"Code": "NoSuchKey"}}, "GetObject")
        return {"Body": io.BytesIO(self.objetos[Key]["Body"])}


class PublicadorFalso:
    def __init__(self) -> None:
        self.eventos: list[dict] = []

    async def publicar(self, evento: dict[str, Any]) -> None:
        self.eventos.append(evento)

    def tipos(self) -> list[str]:
        return [e["type"] for e in self.eventos]


def _solicitud(formatos: list[str] | None = None, **cambios: Any) -> bytes:
    datos = {
        "exportacionId": EXPORTACION,
        "formatos": formatos or ["openapi", "postman"],
        "entradaClave": CLAVE,
        "origen": {"tipo": "paquete", "paqueteId": EXPORTACION, "revision": 3},
        **cambios,
    }
    return json.dumps(
        {
            "eventId": uuid7(),
            "type": "atlas.contratos.exportacion_solicitada",
            "version": 1,
            "occurredAt": "2026-10-24T15:30:00+00:00",
            "tenantId": ORGANIZACION,
            "producer": "atlas",
            "correlationId": "4bf92f3577b34da6a3ce929d0e0e4736",
            "data": datos,
        }
    ).encode()


def _con_entrada(s3: S3Falso, cuerpo: bytes | None = None) -> None:
    contenido = cuerpo or paquete(("core-ict", "1.1.0", ICT)).model_dump_json().encode()
    s3.objetos[CLAVE] = {"Body": contenido}


def _procesar(s3: S3Falso, mensaje: bytes) -> PublicadorFalso:
    publicador = PublicadorFalso()
    manejador = ManejadorExportaciones(lambda: s3, "atlas", publicador, anyio.CapacityLimiter(1))  # type: ignore[arg-type,return-value]
    asyncio.run(manejador.procesar(mensaje))
    return publicador


def test_flujo_feliz_publica_iniciada_y_lista_y_sube_los_archivos() -> None:
    s3 = S3Falso()
    _con_entrada(s3)

    publicador = _procesar(s3, _solicitud())

    assert publicador.tipos() == ["atlas.contratos.exportacion_iniciada", "atlas.contratos.exportacion_lista"]
    lista = publicador.eventos[1]
    assert lista["producer"] == "atlas-docs"
    assert lista["tenantId"] == ORGANIZACION
    assert lista["correlationId"] == "4bf92f3577b34da6a3ce929d0e0e4736"
    assert lista["version"] == 1
    assert re.match(r"^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", lista["eventId"])
    datos = lista["data"]
    assert datos["exportacionId"] == EXPORTACION
    assert len(datos["hashContenido"]) == 64
    assert {a["formato"] for a in datos["archivos"]} == {"openapi", "postman"}
    for archivo in datos["archivos"]:
        assert archivo["clave"] == f"exportaciones/{EXPORTACION}/{archivo['ruta']}"
        subido = s3.objetos[archivo["clave"]]
        assert hashlib.sha256(subido["Body"]).hexdigest() == archivo["sha256"] == subido["Metadata"]["sha256"]
        assert archivo["bytes"] == len(subido["Body"])
        assert subido["ContentType"] == archivo["tipo"]


def test_una_reentrega_da_las_mismas_claves_y_bytes() -> None:
    s3 = S3Falso()
    _con_entrada(s3)

    primero = _procesar(s3, _solicitud()).eventos[1]["data"]
    segundo = _procesar(s3, _solicitud()).eventos[1]["data"]

    assert primero == segundo


def test_sin_entrada_publica_fallida() -> None:
    publicador = _procesar(S3Falso(), _solicitud())

    assert publicador.tipos() == ["atlas.contratos.exportacion_iniciada", "atlas.contratos.exportacion_fallida"]
    assert publicador.eventos[1]["data"]["codigo"] == "entrada_invalida"


def test_entrada_invalida_publica_fallida_sin_el_contenido() -> None:
    s3 = S3Falso()
    _con_entrada(s3, b'{"metadatos": {"secreto": "valor-que-no-debe-salir"}}')

    fallida = _procesar(s3, _solicitud()).eventos[-1]["data"]

    assert fallida["codigo"] == "entrada_invalida"
    assert "valor-que-no-debe-salir" not in fallida["mensaje"]


def test_una_clave_de_entrada_ajena_no_se_lee() -> None:
    s3 = S3Falso()
    s3.objetos["exportaciones/otra/entrada.json"] = {"Body": b"{}"}

    publicador = _procesar(s3, _solicitud(entradaClave="exportaciones/otra/entrada.json"))

    assert publicador.tipos() == ["atlas.contratos.exportacion_fallida"]
    assert publicador.eventos[0]["data"]["codigo"] == "entrada_invalida"


def test_formatos_invalidos_publica_fallida() -> None:
    publicador = _procesar(S3Falso(), _solicitud(formatos=["docx"]))

    assert publicador.tipos() == ["atlas.contratos.exportacion_fallida"]


def test_spec_invalido_publica_fallida() -> None:
    s3 = S3Falso()
    entrada = paquete(("core-ict", "1.1.0", ICT)).model_dump(mode="json")
    entrada["specs"][0]["contenido"] = "esto: no es un contrato"
    _con_entrada(s3, json.dumps(entrada).encode())

    fallida = _procesar(s3, _solicitud(formatos=["html"])).eventos[-1]["data"]

    assert fallida["codigo"] == "spec_invalido"


@pytest.mark.parametrize(
    "mensaje",
    [
        b"no es json",
        json.dumps({"type": "atlas.contratos.exportacion_solicitada", "data": {"exportacionId": "../otro"}}).encode(),
        json.dumps({"type": "atlas.otra.cosa", "data": {"exportacionId": EXPORTACION}}).encode(),
    ],
)
def test_un_sobre_ilegible_no_se_atribuye(mensaje: bytes) -> None:
    with pytest.raises(MensajeIlegibleError):
        _procesar(S3Falso(), mensaje)


def test_s3_caido_se_propaga_para_reintentar() -> None:
    s3 = S3Falso()
    s3.caido = True

    with pytest.raises(ConnectionError):
        _procesar(s3, _solicitud())


# --- Confirmación, reintentos y errores ---


class MensajeFalso:
    def __init__(self, cuerpo: bytes, cabeceras: dict | None = None) -> None:
        self.body = cuerpo
        self.headers = cabeceras or {}
        self.message_id = "x"
        self.type = "atlas.contratos.exportacion_solicitada"
        self.content_type = "application/json"
        self.content_encoding = "utf-8"
        self.acciones: list[str] = []

    async def ack(self) -> None:
        self.acciones.append("ack")

    async def nack(self, requeue: bool) -> None:
        self.acciones.append(f"nack:{requeue}")


class ExchangeFalso:
    def __init__(self) -> None:
        self.publicados: list[str] = []

    async def publish(self, mensaje: Any, routing_key: str) -> None:
        self.publicados.append(routing_key)


def _atender(s3: S3Falso, mensaje: MensajeFalso) -> ExchangeFalso:
    manejador = ManejadorExportaciones(lambda: s3, "atlas", PublicadorFalso(), anyio.CapacityLimiter(1))  # type: ignore[arg-type,return-value]
    por_defecto = ExchangeFalso()
    asyncio.run(atender(mensaje, manejador, por_defecto))  # type: ignore[arg-type]
    return por_defecto


def _muerto(veces: int) -> dict:
    return {"x-death": [{"queue": cola.COLA, "count": veces, "reason": "rejected"}]}


def test_el_exito_se_confirma() -> None:
    s3 = S3Falso()
    _con_entrada(s3)
    mensaje = MensajeFalso(_solicitud())

    assert _atender(s3, mensaje).publicados == []
    assert mensaje.acciones == ["ack"]


def test_un_fallo_de_infraestructura_va_a_reintentos() -> None:
    s3 = S3Falso()
    s3.caido = True
    mensaje = MensajeFalso(_solicitud(), _muerto(2))

    assert _atender(s3, mensaje).publicados == []
    assert mensaje.acciones == ["nack:False"]


def test_a_la_quinta_falla_pasa_a_errores() -> None:
    s3 = S3Falso()
    s3.caido = True
    mensaje = MensajeFalso(_solicitud(), _muerto(4))

    assert _atender(s3, mensaje).publicados == [cola.COLA_ERRORES]
    assert mensaje.acciones == ["ack"]


def test_un_mensaje_ilegible_va_directo_a_errores() -> None:
    mensaje = MensajeFalso(b"basura")

    assert _atender(S3Falso(), mensaje).publicados == [cola.COLA_ERRORES]
    assert mensaje.acciones == ["ack"]


def test_muertes_lee_solo_la_cola_principal() -> None:
    assert muertes(None) == 0
    assert muertes({"x-death": [{"queue": "otra", "count": 9}]}) == 0
    assert muertes(_muerto(3)) == 3


# --- Criterio de aceptación de la HU 4.5 ---


def test_exportar_el_paquete_ict_por_la_cola_genera_los_7_formatos() -> None:
    requiere_pdf()
    s3 = S3Falso()
    _con_entrada(s3)

    publicador = _procesar(s3, _solicitud(formatos=TODOS))

    lista = publicador.eventos[-1]
    assert lista["type"] == "atlas.contratos.exportacion_lista"
    assert {a["formato"] for a in lista["data"]["archivos"]} == set(TODOS)
    assert all(a["clave"] in s3.objetos for a in lista["data"]["archivos"])


# --- Topes (revisión de seguridad F4: F4-SEC-12 y F4-SEC-30) ---


def test_un_mensaje_mas_grande_que_el_tope_es_ilegible_sin_parsearlo() -> None:
    enorme = _solicitud(relleno="x" * cola.MAX_BYTES_MENSAJE)

    with pytest.raises(MensajeIlegibleError, match="tope"):
        _procesar(S3Falso(), enorme)


def test_una_entrada_mas_grande_que_el_tope_publica_fallida_sin_leerla_entera(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(cola, "MAX_BYTES_ENTRADA", 1024)
    s3 = S3Falso()
    _con_entrada(s3)

    fallida = _procesar(s3, _solicitud()).eventos[-1]

    assert fallida["type"] == "atlas.contratos.exportacion_fallida"
    assert fallida["data"]["codigo"] == "entrada_invalida"
    assert "tope" in fallida["data"]["mensaje"]


def _bomba_de_alias() -> str:
    """Alias que se multiplican (billion laughs): 10^9 nodos al expandir."""
    lineas = ["openapi: 3.1.0", "a: &a [x, x, x, x, x, x, x, x, x, x]"]
    for i in range(1, 9):
        anterior, actual = chr(97 + i - 1), chr(97 + i)
        lineas.append(f"{actual}: &{actual} [{', '.join(['*' + anterior] * 10)}]")
    return "\n".join(lineas) + "\n"


@pytest.mark.parametrize(
    "contenido",
    [
        _bomba_de_alias(),
        "openapi: 3.1.0\nx: " + "[" * 500 + "]" * 500 + "\n",  # más hondo que el tope
        "openapi: 3.1.0\nx: &x [*x]\n",  # alias recursivo
    ],
    ids=["alias", "profundidad", "recursivo"],
)
def test_un_spec_patologico_es_spec_invalido_y_no_un_fallo_de_infraestructura(contenido: str) -> None:
    s3 = S3Falso()
    entrada = paquete(("core-ict", "1.1.0", ICT)).model_dump(mode="json")
    entrada["specs"][0]["contenido"] = contenido
    _con_entrada(s3, json.dumps(entrada).encode())

    fallida = _procesar(s3, _solicitud()).eventos[-1]

    assert fallida["type"] == "atlas.contratos.exportacion_fallida"
    assert fallida["data"]["codigo"] == "spec_invalido"
