"""Token de servicio para los endpoints internos de atlas-docs (ATLAS-ADR-0044).

Quien llama pide a atlas-api un token `client_credentials` de OpenIddict con audiencia `atlas-docs`. Esos tokens van
firmados y sin cifrar (JWS), así que aquí se validan con el JWKS publicado por atlas-api: firma por `kid`, algoritmo
asimétrico, emisor, audiencia, vigencia y scope. Sin JWKS o sin emisor configurados se rechaza todo (fail closed).
"""

from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

import anyio
import jwt
from jwt import PyJWKClient

ALGORITMOS = ["RS256", "PS256", "ES256"]
TOLERANCIA_SEGUNDOS = 60


class TokenInvalidoError(Exception):
    """401: falta el token o no vale. El mensaje no repite el token."""


class ScopeInsuficienteError(Exception):
    """403: el token vale, pero no trae el scope que exige el endpoint."""


ResolverClave = Callable[[str], Any]


@dataclass
class ValidadorTokens:
    """Valida tokens Bearer. `resolver_clave` recibe el token y devuelve la llave pública de su `kid`."""

    emisor: str | None
    audiencia: str
    scope: str
    resolver_clave: ResolverClave | None

    @classmethod
    def desde_jwks(cls, jwks_url: str | None, emisor: str | None, audiencia: str, scope: str) -> "ValidadorTokens":
        resolver: ResolverClave | None = None
        if jwks_url:
            # PyJWKClient guarda en caché el JWKS y lo vuelve a pedir si llega un `kid` desconocido (rotación).
            cliente = PyJWKClient(jwks_url, cache_keys=True, lifespan=300, timeout=5)
            resolver = lambda token: cliente.get_signing_key_from_jwt(token).key  # noqa: E731
        return cls(emisor, audiencia, scope, resolver)

    def validar(self, token: str) -> dict[str, Any]:
        if not self.emisor or self.resolver_clave is None:
            raise TokenInvalidoError("La autenticación de servicio no está configurada.")
        try:
            encabezado = jwt.get_unverified_header(token)
            if encabezado.get("alg") not in ALGORITMOS:
                raise TokenInvalidoError("Algoritmo no permitido.")
            clave = self.resolver_clave(token)
            claims = jwt.decode(
                token,
                clave,
                algorithms=ALGORITMOS,
                audience=self.audiencia,
                issuer=self.emisor,
                leeway=TOLERANCIA_SEGUNDOS,
                options={"require": ["exp", "iss", "aud"]},
            )
        except TokenInvalidoError:
            raise
        except Exception as error:  # noqa: BLE001 — cualquier fallo de lectura, firma o JWKS es un token inválido
            raise TokenInvalidoError("El token no es válido.") from error
        if self.scope not in _scopes(claims.get("scope")):
            raise ScopeInsuficienteError(f"El token no trae el scope {self.scope}.")
        return claims

    async def validar_cabecera(self, autorizacion: str | None) -> dict[str, Any]:
        if not autorizacion or not autorizacion.lower().startswith("bearer "):
            raise TokenInvalidoError("Falta el token Bearer.")
        token = autorizacion[7:].strip()
        if not token:
            raise TokenInvalidoError("Falta el token Bearer.")
        # Pedir el JWKS es de red y bloqueante: va en un hilo.
        return await anyio.to_thread.run_sync(lambda: self.validar(token))


def _scopes(valor: object) -> set[str]:
    if isinstance(valor, str):
        return set(valor.split())
    if isinstance(valor, list):
        return {str(s) for s in valor}
    return set()
