"""Configuración de atlas-ia desde variables de entorno.

Falla al arrancar nombrando todas las variables obligatorias que faltan (HU 1.1).
"""

from typing import Literal

from pydantic import Field, ValidationError
from pydantic_settings import BaseSettings, SettingsConfigDict


class ConfiguracionFaltanteError(RuntimeError):
    def __init__(self, variables: list[str]) -> None:
        self.variables = variables
        super().__init__("Faltan variables de configuración obligatorias o inválidas: " + ", ".join(variables))


class Configuracion(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="ATLAS_", case_sensitive=True, extra="ignore")

    AMBIENTE: Literal["local", "dev", "qa", "pdn"]
    IA_DATABASE_URL: str = Field(min_length=1)


def cargar() -> Configuracion:
    try:
        return Configuracion()  # type: ignore[call-arg]  # los valores llegan del entorno
    except ValidationError as error:
        variables = sorted({"ATLAS_" + str(e["loc"][0]) for e in error.errors()})
        raise ConfiguracionFaltanteError(variables) from None
