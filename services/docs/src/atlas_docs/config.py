"""Configuración de atlas-docs desde variables de entorno.

Falla al arrancar nombrando todas las variables obligatorias que faltan (HU 1.1).
"""

from typing import Literal, Self

from pydantic import Field, SecretStr, ValidationError, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class ConfiguracionFaltanteError(RuntimeError):
    def __init__(self, variables: list[str]) -> None:
        self.variables = variables
        super().__init__("Faltan variables de configuración obligatorias o inválidas: " + ", ".join(variables))


class Configuracion(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="ATLAS_", case_sensitive=True, extra="ignore")

    AMBIENTE: Literal["local", "dev", "qa", "pdn"]
    DOCS_S3_ENDPOINT: str = Field(min_length=1)
    DOCS_S3_ACCESS_KEY: SecretStr = Field(min_length=1)
    DOCS_S3_SECRET_KEY: SecretStr = Field(min_length=1)
    DOCS_S3_BUCKET: str = Field(min_length=1)
    DOCS_S3_REGION: str = "us-east-1"
    # Solo en local: en dev, qa y pdn el bucket lo crea infraestructura (F0.4, ítems 4.1 y 4.2).
    DOCS_S3_CREAR_BUCKET: bool = False

    @model_validator(mode="after")
    def _crear_bucket_solo_en_local(self) -> Self:
        if self.DOCS_S3_CREAR_BUCKET and self.AMBIENTE != "local":
            raise ValueError("ATLAS_DOCS_S3_CREAR_BUCKET solo se permite con ATLAS_AMBIENTE=local")
        return self


def _variable(loc: tuple[int | str, ...]) -> str:
    # Los errores del validador de modelo no traen campo: el único es el de CREAR_BUCKET.
    return "ATLAS_" + str(loc[0]) if loc else "ATLAS_DOCS_S3_CREAR_BUCKET"


def cargar() -> Configuracion:
    try:
        return Configuracion()  # type: ignore[call-arg]  # los valores llegan del entorno
    except ValidationError as error:
        variables = sorted({_variable(e["loc"]) for e in error.errors()})
        raise ConfiguracionFaltanteError(variables) from None
