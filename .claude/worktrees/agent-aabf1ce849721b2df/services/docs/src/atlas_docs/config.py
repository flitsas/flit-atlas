"""Configuración de atlas-docs desde variables de entorno.

Falla al arrancar nombrando todas las variables obligatorias que faltan (HU 1.1).
"""

import os
from typing import Literal, Self

from pydantic import Field, SecretStr, ValidationError, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

PREFIJO = "ATLAS_"
AMBIENTES_DESPLEGADOS = {"dev", "qa", "pdn"}
# Fuera de local, el consumidor de la cola y la autenticación del endpoint interno son obligatorios (HU 4.5,
# ATLAS-ADR-0042 y ATLAS-ADR-0044).
OBLIGATORIAS_DESPLEGADO = ("DOCS_RABBITMQ_URL", "DOCS_AUTH_JWKS_URL", "DOCS_AUTH_EMISOR")


class ConfiguracionFaltanteError(RuntimeError):
    def __init__(self, variables: list[str]) -> None:
        self.variables = variables
        super().__init__("Faltan variables de configuración obligatorias o inválidas: " + ", ".join(variables))


class Configuracion(BaseSettings):
    model_config = SettingsConfigDict(env_prefix=PREFIJO, case_sensitive=True, extra="ignore")

    AMBIENTE: Literal["local", "dev", "qa", "pdn"]
    DOCS_S3_ENDPOINT: str = Field(min_length=1)
    DOCS_S3_ACCESS_KEY: SecretStr = Field(min_length=1)
    DOCS_S3_SECRET_KEY: SecretStr = Field(min_length=1)
    DOCS_S3_BUCKET: str = Field(min_length=1)
    DOCS_S3_REGION: str = "us-east-1"
    # Solo en local: en dev, qa y pdn el bucket lo crea infraestructura (F0.4, ítems 4.1 y 4.2).
    DOCS_S3_CREAR_BUCKET: bool = False

    # Cola de exportaciones (atlas.contratos.exportacion_solicitada). Sin URL (solo en local) no hay consumidor.
    DOCS_RABBITMQ_URL: SecretStr | None = None

    # Token de servicio de OpenIddict para POST /exportes (ATLAS-ADR-0044). Sin JWKS ni emisor se rechaza todo.
    DOCS_AUTH_JWKS_URL: str | None = None
    DOCS_AUTH_EMISOR: str | None = None
    DOCS_AUTH_AUDIENCIA: str = "atlas-docs"
    DOCS_AUTH_SCOPE: str = "atlas.contratos.exportar"

    @model_validator(mode="after")
    def _reglas_por_ambiente(self) -> Self:
        problemas: list[str] = []
        if self.DOCS_S3_CREAR_BUCKET and self.AMBIENTE != "local":
            problemas.append("DOCS_S3_CREAR_BUCKET")
        if self.AMBIENTE in AMBIENTES_DESPLEGADOS:
            problemas.extend(v for v in OBLIGATORIAS_DESPLEGADO if not _con_valor(getattr(self, v)))
        if problemas:
            # El texto lleva los nombres para que `cargar` los informe todos (los errores de modelo no traen campo).
            raise ValueError("|".join(PREFIJO + p for p in problemas))
        return self

    @property
    def auth_configurada(self) -> bool:
        return bool(self.DOCS_AUTH_JWKS_URL and self.DOCS_AUTH_EMISOR)


def _con_valor(valor: object) -> bool:
    if isinstance(valor, SecretStr):
        valor = valor.get_secret_value()
    return bool(valor)


def _variables(error: ValidationError) -> set[str]:
    variables: set[str] = set()
    for e in error.errors():
        if e["loc"]:
            variables.add(PREFIJO + str(e["loc"][0]))
        else:
            variables.update(str(e["ctx"]["error"]).split("|"))
    return variables


def cargar() -> Configuracion:
    try:
        return Configuracion()  # type: ignore[call-arg]  # los valores llegan del entorno
    except ValidationError as error:
        variables = _variables(error)
        # Con errores de campo el validador de modelo no corre: las obligatorias del ambiente se revisan aquí también.
        if os.environ.get(PREFIJO + "AMBIENTE") in AMBIENTES_DESPLEGADOS:
            variables.update(PREFIJO + v for v in OBLIGATORIAS_DESPLEGADO if not os.environ.get(PREFIJO + v))
        raise ConfiguracionFaltanteError(sorted(variables)) from None
