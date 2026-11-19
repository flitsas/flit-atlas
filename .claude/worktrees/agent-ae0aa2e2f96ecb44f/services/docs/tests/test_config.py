import pytest

from atlas_docs.config import ConfiguracionFaltanteError, cargar

VARIABLES = [
    "ATLAS_AMBIENTE",
    "ATLAS_DOCS_S3_ACCESS_KEY",
    "ATLAS_DOCS_S3_BUCKET",
    "ATLAS_DOCS_S3_ENDPOINT",
    "ATLAS_DOCS_S3_SECRET_KEY",
]


def _completo(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("ATLAS_AMBIENTE", "dev")
    monkeypatch.setenv("ATLAS_DOCS_S3_ENDPOINT", "http://s3.ejemplo.com")
    monkeypatch.setenv("ATLAS_DOCS_S3_ACCESS_KEY", "llave-de-prueba")
    monkeypatch.setenv("ATLAS_DOCS_S3_SECRET_KEY", "secreto-de-prueba")
    monkeypatch.setenv("ATLAS_DOCS_S3_BUCKET", "atlas")
    monkeypatch.setenv("ATLAS_DOCS_RABBITMQ_URL", "amqp://atlas-docs:clave-amqp@rabbitmq.ejemplo.com:5672/atlas")
    monkeypatch.setenv("ATLAS_DOCS_AUTH_JWKS_URL", "https://atlas.ejemplo.com/.well-known/jwks")
    monkeypatch.setenv("ATLAS_DOCS_AUTH_EMISOR", "https://atlas.ejemplo.com/")


def test_sin_variables_nombra_todas_las_que_faltan(monkeypatch: pytest.MonkeyPatch) -> None:
    for variable in VARIABLES:
        monkeypatch.delenv(variable, raising=False)

    with pytest.raises(ConfiguracionFaltanteError) as error:
        cargar()

    assert error.value.variables == VARIABLES


def test_crear_bucket_fuera_de_local_se_rechaza(monkeypatch: pytest.MonkeyPatch) -> None:
    _completo(monkeypatch)
    monkeypatch.setenv("ATLAS_DOCS_S3_CREAR_BUCKET", "true")

    with pytest.raises(ConfiguracionFaltanteError) as error:
        cargar()

    assert error.value.variables == ["ATLAS_DOCS_S3_CREAR_BUCKET"]


def test_los_secretos_no_salen_en_la_representacion(monkeypatch: pytest.MonkeyPatch) -> None:
    _completo(monkeypatch)

    assert "secreto-de-prueba" not in repr(cargar())
    assert "clave-amqp" not in repr(cargar())


DESPLEGADO = ["ATLAS_DOCS_AUTH_EMISOR", "ATLAS_DOCS_AUTH_JWKS_URL", "ATLAS_DOCS_RABBITMQ_URL"]


def test_fuera_de_local_la_cola_y_la_autenticacion_son_obligatorias(monkeypatch: pytest.MonkeyPatch) -> None:
    _completo(monkeypatch)
    for variable in DESPLEGADO:
        monkeypatch.delenv(variable, raising=False)

    with pytest.raises(ConfiguracionFaltanteError) as error:
        cargar()

    assert error.value.variables == DESPLEGADO


def test_se_nombran_juntas_las_de_s3_y_las_del_ambiente(monkeypatch: pytest.MonkeyPatch) -> None:
    _completo(monkeypatch)
    monkeypatch.delenv("ATLAS_DOCS_S3_BUCKET")
    for variable in DESPLEGADO:
        monkeypatch.delenv(variable, raising=False)

    with pytest.raises(ConfiguracionFaltanteError) as error:
        cargar()

    assert error.value.variables == sorted([*DESPLEGADO, "ATLAS_DOCS_S3_BUCKET"])


def test_en_local_la_cola_y_la_autenticacion_son_opcionales(monkeypatch: pytest.MonkeyPatch) -> None:
    _completo(monkeypatch)
    monkeypatch.setenv("ATLAS_AMBIENTE", "local")
    for variable in DESPLEGADO:
        monkeypatch.delenv(variable, raising=False)

    config = cargar()

    assert config.DOCS_RABBITMQ_URL is None
    assert not config.auth_configurada
