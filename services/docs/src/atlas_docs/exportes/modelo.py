"""Entrada de una exportación: el «paquete a exportar» que arma atlas-api (ATLAS-ADR-0042).

atlas-api ya aplicó los permisos y la poda (HU 4.2): aquí no se decide qué ve cada organización, solo cómo se
presenta. La organización y la fecha llegan resueltas (fecha de Bogotá) para que la salida no dependa del reloj.
"""

from datetime import date
from enum import StrEnum
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints

Texto = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=300)]

# Topes para que una petición no tumbe el pod (el contrato de Atlas pesa ~0,5 MB en YAML).
MAX_SPECS = 30
MAX_GUIAS = 50
MAX_BYTES_SPEC = 5 * 1024 * 1024
MAX_BYTES_GUIA = 512 * 1024
# Tope del cuerpo de `POST /exportes` y de la entrada leída del S3, en bytes y antes de leerla (F4-SEC-12). Los topes de
# arriba cuentan caracteres y solo se revisan después de leer y parsear todo: este es el que protege la memoria.
MAX_BYTES_ENTRADA = 64 * 1024 * 1024


class Formato(StrEnum):
    OPENAPI = "openapi"  # YAML y JSON del spec podado
    POSTMAN = "postman"
    HTML = "html"
    PDF = "pdf"
    WORD = "word"
    EXCEL = "excel"
    ZIP = "zip"


class EntradaChangelog(BaseModel):
    model_config = ConfigDict(extra="forbid")

    revision: Texto
    fecha: date
    cambio: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=2000)]


class Metadatos(BaseModel):
    model_config = ConfigDict(extra="forbid")

    paquete: Texto = Field(description="Nombre del paquete («Integración ICT»).")
    revision: int = Field(ge=1)
    organizacion: Texto = Field(description="Organización a la que se entrega; va en la marca de agua.")
    fecha: date = Field(description="Fecha de la exportación en hora de Bogotá; va en la marca de agua.")
    changelog: list[EntradaChangelog] = Field(default_factory=list, max_length=500)


class SpecExportar(BaseModel):
    model_config = ConfigDict(extra="forbid")

    contrato: Annotated[str, StringConstraints(pattern=r"^[a-z0-9][a-z0-9-]{0,79}$")]
    version: Annotated[str, StringConstraints(pattern=r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")]
    tipo: Literal["openapi", "asyncapi"] | None = Field(default=None, description="Si falta, se deduce del spec.")
    contenido: Annotated[str, StringConstraints(min_length=1, max_length=MAX_BYTES_SPEC)] = Field(
        description="El spec podado, en YAML o JSON."
    )


class Guia(BaseModel):
    model_config = ConfigDict(extra="forbid")

    titulo: Texto
    markdown: Annotated[str, StringConstraints(max_length=MAX_BYTES_GUIA)]


class PaqueteExportar(BaseModel):
    model_config = ConfigDict(extra="forbid")

    metadatos: Metadatos
    specs: list[SpecExportar] = Field(min_length=1, max_length=MAX_SPECS)
    guias: list[Guia] = Field(default_factory=list, max_length=MAX_GUIAS)
