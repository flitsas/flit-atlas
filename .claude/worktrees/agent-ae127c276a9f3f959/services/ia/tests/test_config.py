import pytest

from atlas_ia.config import ConfiguracionFaltanteError, cargar


def test_sin_variables_nombra_todas_las_que_faltan(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("ATLAS_AMBIENTE", raising=False)
    monkeypatch.delenv("ATLAS_IA_DATABASE_URL", raising=False)

    with pytest.raises(ConfiguracionFaltanteError) as error:
        cargar()

    assert error.value.variables == ["ATLAS_AMBIENTE", "ATLAS_IA_DATABASE_URL"]
    assert "ATLAS_AMBIENTE, ATLAS_IA_DATABASE_URL" in str(error.value)


def test_ambiente_invalido_se_reporta(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("ATLAS_AMBIENTE", "produccion")
    monkeypatch.setenv("ATLAS_IA_DATABASE_URL", "postgresql://x")

    with pytest.raises(ConfiguracionFaltanteError) as error:
        cargar()

    assert error.value.variables == ["ATLAS_AMBIENTE"]
