"""Guardia ASGI de los endpoints internos con cuerpo (F4-SEC-12, ATLAS-ADR-0044).

FastAPI lee y parsea el cuerpo antes de correr las dependencias, así que el token de `Depends` llega tarde: alguien con
red hacia el pod podría mandar gigas sin token y tumbarlo. Este middleware va antes de FastAPI y, en las rutas que
protege, primero valida el token (sin leer el cuerpo), después exige `Content-Length` dentro del tope y, al leer,
corta si llegan más bytes de los anunciados o del tope.
"""

from collections.abc import Awaitable, Callable, MutableMapping
from typing import Any

from fastapi import HTTPException
from fastapi.responses import JSONResponse

from atlas_docs.autenticacion import ScopeInsuficienteError, TokenInvalidoError, ValidadorTokens

Scope = MutableMapping[str, Any]
Mensaje = MutableMapping[str, Any]
Recibir = Callable[[], Awaitable[Mensaje]]
Enviar = Callable[[Mensaje], Awaitable[None]]
AppAsgi = Callable[[Scope, Recibir, Enviar], Awaitable[None]]


def problema(status: int, titulo: str, codigo: str, cabeceras: dict[str, str] | None = None) -> JSONResponse:
    return JSONResponse(
        {"status": status, "title": titulo, "codigo": codigo},
        status_code=status,
        media_type="application/problem+json",
        headers=cabeceras,
    )


class CuerpoExcedidoError(HTTPException):
    """Llegaron más bytes que los anunciados o que el tope.

    Es una HTTPException porque FastAPI convierte en 400 cualquier otro error al leer el cuerpo; `crear_app` la responde
    como problema 413.
    """

    def __init__(self, tope: int) -> None:
        super().__init__(status_code=413, detail=f"El cuerpo supera el tope de {tope} bytes.")


class GuardiaCuerpo:
    """Token antes del cuerpo y tope de tamaño en `rutas` (método y ruta exactos, p. ej. `("POST", "/exportes")`)."""

    def __init__(self, app: AppAsgi, validador: ValidadorTokens, tope: int, rutas: set[tuple[str, str]]) -> None:
        self.app = app
        self.validador = validador
        self.tope = tope
        self.rutas = rutas

    async def __call__(self, scope: Scope, receive: Recibir, send: Enviar) -> None:
        if scope["type"] != "http" or (scope["method"], scope["path"]) not in self.rutas:
            await self.app(scope, receive, send)
            return
        cabeceras: dict[str, str] = {}
        for nombre, valor in scope.get("headers") or []:
            cabeceras.setdefault(nombre.decode("latin-1").lower(), valor.decode("latin-1"))

        try:
            await self.validador.validar_cabecera(cabeceras.get("authorization"))
        except TokenInvalidoError:
            respuesta = problema(
                401, "Falta un token de servicio válido.", "token_invalido", {"WWW-Authenticate": "Bearer"}
            )
            await respuesta(scope, receive, send)
            return
        except ScopeInsuficienteError:
            respuesta = problema(
                403, "El token no autoriza exportar.", "scope_insuficiente", {"WWW-Authenticate": "Bearer"}
            )
            await respuesta(scope, receive, send)
            return

        largo = cabeceras.get("content-length")
        if largo is None or not largo.isascii() or not largo.isdigit():
            await problema(411, "Falta Content-Length.", "sin_largo")(scope, receive, send)
            return
        anunciado = int(largo)
        if anunciado > self.tope:
            await problema(413, f"El cuerpo supera el tope de {self.tope} bytes.", "cuerpo_demasiado_grande")(
                scope, receive, send
            )
            return

        leidos = 0

        async def recibir() -> Mensaje:
            nonlocal leidos
            mensaje = await receive()
            if mensaje["type"] == "http.request":
                leidos += len(mensaje.get("body", b""))
                if leidos > min(anunciado, self.tope):
                    raise CuerpoExcedidoError(self.tope)
            return mensaje

        await self.app(scope, recibir, send)
