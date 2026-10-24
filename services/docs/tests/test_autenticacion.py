"""POST /exportes exige un token de servicio de OpenIddict (ATLAS-ADR-0044)."""

import time

import jwt
import pytest
from apoyo_tokens import AUDIENCIA, EMISOR, JWKS, OTRA_LLAVE, SCOPE, cabecera, token, validador
from fastapi.testclient import TestClient
from jwt import PyJWKClient, jwk_set_cache

from atlas_docs.autenticacion import (
    ESPERA_REFRESCO_SEGUNDOS,
    VIDA_JWKS_SEGUNDOS,
    TokenInvalidoError,
    ValidadorTokens,
)
from atlas_docs.config import Configuracion
from atlas_docs.main import crear_app

CONFIG = Configuracion(
    AMBIENTE="local",
    DOCS_S3_ENDPOINT="http://s3.ejemplo.com",
    DOCS_S3_ACCESS_KEY="x",
    DOCS_S3_SECRET_KEY="x",
    DOCS_S3_BUCKET="atlas",
)
# Un cuerpo inválido: si el token pasa, la respuesta es 422 (no se llega a exportar nada).
CUERPO: dict = {}


def _cliente(v: ValidadorTokens | None = None) -> TestClient:
    return TestClient(crear_app(CONFIG, [], validador=v or validador(), consumidor=False))


def test_un_token_valido_pasa() -> None:
    respuesta = _cliente().post("/exportes", json=CUERPO, headers=cabecera())

    assert respuesta.status_code == 422


def test_el_scope_puede_llegar_como_lista() -> None:
    respuesta = _cliente().post("/exportes", json=CUERPO, headers=cabecera(token(scope=["openid", SCOPE])))

    assert respuesta.status_code == 422


def test_sin_token_responde_401() -> None:
    respuesta = _cliente().post("/exportes", json=CUERPO)

    assert respuesta.status_code == 401
    assert respuesta.json()["codigo"] == "token_invalido"
    assert respuesta.headers["www-authenticate"] == "Bearer"


@pytest.mark.parametrize(
    "malo",
    [
        pytest.param(lambda: token(llave=OTRA_LLAVE), id="firma de otra llave"),
        pytest.param(lambda: token(aud="atlas-api"), id="otra audiencia"),
        pytest.param(lambda: token(iss="https://otro.ejemplo.com/"), id="otro emisor"),
        pytest.param(lambda: token(exp=int(time.time()) - 600), id="vencido"),
        pytest.param(lambda: token(nbf=int(time.time()) + 600), id="aún no vale"),
        pytest.param(lambda: token(kid="otra"), id="kid desconocido"),
        pytest.param(lambda: token(exp=None), id="sin exp"),
        pytest.param(
            lambda: jwt.encode({"iss": "x", "aud": AUDIENCIA, "scope": SCOPE}, None, algorithm="none"),
            id="alg none",
        ),
        pytest.param(
            lambda: jwt.encode({"aud": AUDIENCIA, "scope": SCOPE}, "secreto-compartido-de-prueba-32bytes", "HS256"),
            id="HS256",
        ),
        pytest.param(lambda: "no-es-un-jwt", id="basura"),
    ],
)
def test_un_token_invalido_responde_401(malo) -> None:  # noqa: ANN001
    respuesta = _cliente().post("/exportes", json=CUERPO, headers=cabecera(malo()))

    assert respuesta.status_code == 401
    assert respuesta.json()["codigo"] == "token_invalido"


def test_sin_el_scope_responde_403() -> None:
    respuesta = _cliente().post("/exportes", json=CUERPO, headers=cabecera(token(scope="atlas.mcp")))

    assert respuesta.status_code == 403
    assert respuesta.json()["codigo"] == "scope_insuficiente"


def test_sin_configuracion_rechaza_todo() -> None:
    sin_config = ValidadorTokens.desde_jwks(None, None, AUDIENCIA, SCOPE)

    respuesta = _cliente(sin_config).post("/exportes", json=CUERPO, headers=cabecera())

    assert respuesta.status_code == 401


class JwksFalso:
    """El JWKS de atlas-api en memoria para el PyJWKClient real de `desde_jwks` (sin red) y un reloj que se adelanta."""

    def __init__(self, monkeypatch: pytest.MonkeyPatch) -> None:
        self.jwks: dict = JWKS
        self.pedidos = 0
        self.ahora = 1000.0
        falso = self

        def fetch_data(cliente: PyJWKClient) -> dict:
            falso.pedidos += 1
            if cliente.jwk_set_cache is not None:
                cliente.jwk_set_cache.put(falso.jwks)
            cliente._last_successful_fetch = falso.ahora  # noqa: SLF001 — lo mismo que hace PyJWT
            return falso.jwks

        monkeypatch.setattr(PyJWKClient, "fetch_data", fetch_data)
        monkeypatch.setattr(jwk_set_cache.time, "monotonic", lambda: falso.ahora)

    def validador(self) -> ValidadorTokens:
        return ValidadorTokens.desde_jwks("https://atlas.ejemplo.com/.well-known/jwks", EMISOR, AUDIENCIA, SCOPE)


def test_un_kid_inventado_no_vuelve_a_pedir_el_jwks_en_cada_peticion(monkeypatch: pytest.MonkeyPatch) -> None:
    jwks = JwksFalso(monkeypatch)
    v = jwks.validador()

    assert v.validar(token())["scope"] == SCOPE
    for _ in range(20):
        with pytest.raises(TokenInvalidoError):
            v.validar(token(kid="inventado"))
    assert jwks.pedidos == 1

    jwks.ahora += ESPERA_REFRESCO_SEGUNDOS + 1
    with pytest.raises(TokenInvalidoError):
        v.validar(token(kid="inventado"))
    assert jwks.pedidos == 2


def test_una_llave_retirada_del_jwks_deja_de_valer_al_vencer_la_cache(monkeypatch: pytest.MonkeyPatch) -> None:
    jwks = JwksFalso(monkeypatch)
    v = jwks.validador()
    assert v.validar(token())["scope"] == SCOPE

    # atlas-api rota la llave: el JWKS ya no trae la de antes.
    nueva = {**jwt.algorithms.RSAAlgorithm.to_jwk(OTRA_LLAVE.public_key(), as_dict=True), "kid": "nueva", "use": "sig"}
    jwks.jwks = {"keys": [nueva]}
    jwks.ahora += VIDA_JWKS_SEGUNDOS + 1

    with pytest.raises(TokenInvalidoError):
        v.validar(token())
    assert v.validar(token(llave=OTRA_LLAVE, kid="nueva"))["scope"] == SCOPE


def test_por_defecto_sin_variables_el_endpoint_queda_cerrado() -> None:
    cliente = TestClient(crear_app(CONFIG, [], consumidor=False))

    assert cliente.post("/exportes", json=CUERPO, headers=cabecera()).status_code == 401
