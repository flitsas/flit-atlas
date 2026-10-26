"""Lectura y normalización de los specs del paquete, y el hash del contenido exportado.

El spec se lee como YAML (que también acepta JSON) sin convertir fechas: `fecha: 2025-05-16` sigue siendo texto, así
el JSON exportado dice lo mismo que el YAML. La normalización conserva el orden de las claves del autor.
"""

import hashlib
import json
from dataclasses import dataclass
from typing import Any

import yaml

from atlas_docs.exportes.modelo import PaqueteExportar, SpecExportar


class SpecInvalidoError(ValueError):
    pass


# Los mismos topes que la poda de atlas-api (TextoSpec): un spec patológico (alias de YAML que se multiplican, anidación
# sin fin) es un spec inválido, no un fallo de infraestructura que se reintenta (F4-SEC-30).
PROFUNDIDAD_MAXIMA = 128
NODOS_MAXIMOS = 2_000_000


def _revisar_forma(documento: Any, contrato: str) -> None:
    """Cuenta los nodos como quedan al expandir los alias y mide la profundidad, sin recursión."""
    pendientes: list[tuple[Any, int]] = [(documento, 1)]
    nodos = 0
    while pendientes:
        nodo, profundidad = pendientes.pop()
        nodos += 1
        if nodos > NODOS_MAXIMOS or profundidad > PROFUNDIDAD_MAXIMA:
            raise SpecInvalidoError(f"El spec «{contrato}» es demasiado grande o anidado (alias o profundidad).")
        if isinstance(nodo, dict):
            pendientes.extend((v, profundidad + 1) for v in nodo.values())
        elif isinstance(nodo, list):
            pendientes.extend((v, profundidad + 1) for v in nodo)


class _CargadorSinFechas(yaml.SafeLoader):
    """SafeLoader sin el resolutor implícito de timestamps (las fechas quedan como texto)."""


_CargadorSinFechas.yaml_implicit_resolvers = {
    inicial: [(etiqueta, regex) for etiqueta, regex in resolutores if etiqueta != "tag:yaml.org,2002:timestamp"]
    for inicial, resolutores in yaml.SafeLoader.yaml_implicit_resolvers.items()
}


class _EscritorLegible(yaml.SafeDumper):
    """Bloques literales para textos de varias líneas (las descripciones se leen como en el original)."""


def _representar_texto(escritor: yaml.SafeDumper, valor: str) -> yaml.ScalarNode:
    if "\n" in valor:
        return escritor.represent_scalar("tag:yaml.org,2002:str", valor, style="|")
    return escritor.represent_scalar("tag:yaml.org,2002:str", valor)


_EscritorLegible.add_representer(str, _representar_texto)


@dataclass(frozen=True)
class SpecLeido:
    contrato: str
    version: str
    tipo: str  # «openapi» o «asyncapi»
    documento: dict[str, Any]

    @property
    def titulo(self) -> str:
        info = self.documento.get("info")
        titulo = info.get("title") if isinstance(info, dict) else None
        return str(titulo) if titulo else self.contrato

    @property
    def nombre_archivo(self) -> str:
        return f"{self.contrato}-{self.version}"


def leer(spec: SpecExportar) -> SpecLeido:
    try:
        documento = yaml.load(spec.contenido, Loader=_CargadorSinFechas)  # noqa: S506 — SafeLoader sin timestamps
    except yaml.YAMLError as error:
        raise SpecInvalidoError(f"El spec «{spec.contrato}» no es YAML ni JSON válido.") from error
    except (RecursionError, MemoryError) as error:
        raise SpecInvalidoError(f"El spec «{spec.contrato}» es demasiado grande o anidado.") from error
    _revisar_forma(documento, spec.contrato)
    if not isinstance(documento, dict):
        raise SpecInvalidoError(f"El spec «{spec.contrato}» no es un objeto.")
    deducido = "openapi" if "openapi" in documento else "asyncapi" if "asyncapi" in documento else None
    if deducido is None:
        raise SpecInvalidoError(f"El spec «{spec.contrato}» no declara `openapi` ni `asyncapi`.")
    if spec.tipo is not None and spec.tipo != deducido:
        raise SpecInvalidoError(f"El spec «{spec.contrato}» se declaró {spec.tipo} pero es {deducido}.")
    # Ida y vuelta por JSON: deja solo tipos JSON (y falla con lo que no lo sea, como claves no textuales raras).
    try:
        documento = json.loads(json.dumps(documento, ensure_ascii=False))
    except (TypeError, ValueError) as error:
        raise SpecInvalidoError(f"El spec «{spec.contrato}» trae valores que no son JSON.") from error
    except (RecursionError, MemoryError) as error:
        raise SpecInvalidoError(f"El spec «{spec.contrato}» es demasiado grande o anidado.") from error
    return SpecLeido(spec.contrato, spec.version, deducido, documento)


def a_yaml(documento: dict[str, Any]) -> str:
    return yaml.dump(
        documento, Dumper=_EscritorLegible, sort_keys=False, allow_unicode=True, width=120, default_flow_style=False
    )


def a_json(documento: dict[str, Any]) -> str:
    return json.dumps(documento, ensure_ascii=False, indent=2) + "\n"


def hash_contenido(paquete: PaqueteExportar, specs: list[SpecLeido]) -> str:
    """SHA-256 del contenido exportado: metadatos, specs normalizados y guías, en JSON canónico.

    Es lo que va en la portada de cada formato y en el manifiesto del ZIP. No depende del formato ni del orden de las
    claves dentro de los specs, solo de lo que dicen.
    """
    canonico = {
        "metadatos": paquete.metadatos.model_dump(mode="json"),
        "specs": [
            {"contrato": s.contrato, "version": s.version, "tipo": s.tipo, "documento": s.documento} for s in specs
        ],
        "guias": [g.model_dump(mode="json") for g in paquete.guias],
    }
    texto = json.dumps(canonico, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(texto.encode("utf-8")).hexdigest()
