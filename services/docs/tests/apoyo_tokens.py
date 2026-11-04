"""Tokens de servicio de prueba (ATLAS-ADR-0044): una llave RSA generada en la prueba, su JWKS y tokens firmados."""

import time
from typing import Any

import jwt
from cryptography.hazmat.primitives.asymmetric import rsa
from jwt import PyJWK

from atlas_docs.autenticacion import ValidadorTokens

EMISOR = "https://atlas.ejemplo.com/"
AUDIENCIA = "atlas-docs"
SCOPE = "atlas.contratos.exportar"
KID = "llave-de-prueba"

LLAVE = rsa.generate_private_key(public_exponent=65537, key_size=2048)
OTRA_LLAVE = rsa.generate_private_key(public_exponent=65537, key_size=2048)
JWKS = {"keys": [{**jwt.algorithms.RSAAlgorithm.to_jwk(LLAVE.public_key(), as_dict=True), "kid": KID, "use": "sig"}]}


def _resolver(token: str) -> Any:
    kid = jwt.get_unverified_header(token).get("kid")
    for clave in JWKS["keys"]:
        if clave["kid"] == kid:
            return PyJWK(clave).key
    raise jwt.PyJWKClientError("kid desconocido")


def validador() -> ValidadorTokens:
    return ValidadorTokens(EMISOR, AUDIENCIA, SCOPE, _resolver)


def token(llave: Any = LLAVE, alg: str = "RS256", kid: str = KID, **cambios: Any) -> str:
    ahora = int(time.time())
    claims: dict[str, Any] = {
        "iss": EMISOR,
        "aud": AUDIENCIA,
        "sub": "atlas-api",
        "client_id": "atlas-api",
        "scope": SCOPE,
        "iat": ahora,
        "nbf": ahora,
        "exp": ahora + 300,
    }
    claims.update(cambios)
    claims = {k: v for k, v in claims.items() if v is not None}
    return jwt.encode(claims, llave, algorithm=alg, headers={"kid": kid, "typ": "at+jwt"})


def cabecera(valor: str | None = None) -> dict[str, str]:
    return {"Authorization": f"Bearer {valor or token()}"}
