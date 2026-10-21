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
