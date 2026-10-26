"""Lenguaje funcional en los textos de cara al cliente del contrato de ICT (F4-SEC-09, ATLAS-ADR-0037).

El spec enriquecido se comparte con clientes después de la poda, y la poda no reescribe textos (ATLAS-ADR-0038). Por
eso `description`, `summary` y `title` salen del generador en lenguaje funcional: sin rutas de repo, archivos,
clases, proyectos, números de feature o ticket, comandos, nombres de servicios internos, hosts internos ni notas del
equipo. La procedencia que sirve al equipo va en extensiones `x-flit-*` (la poda las quita) o en el informe.

`piezas_internas` solo detecta. La usan la conversión (falla si algo sobrevive) y la prueba que recorre los specs
versionados en `contracts/ict/`. «ICT» y «FLIT» son nombres del producto y sí valen.
"""

import re
from collections.abc import Iterator
from dataclasses import dataclass

# Claves de texto que lee el cliente (OpenAPI: info, operaciones, parámetros, esquemas, respuestas, ejemplos…).
CLAVES_TEXTO = ("description", "summary", "title")

PATRONES: dict[str, re.Pattern[str]] = {
    # Rutas de un repo: `services/core-ict`, `contracts/openapi/…`, `src/Flit.Ict.Api/…`.
    "ruta_repo": re.compile(r"(?i)(?<![\w.:/])(?:services|contracts|src|apps|deploy|docs|tests)/"),
    # Archivos de código o de configuración: `WebhookNotificationJob.cs`, `appsettings.json`, `core-ict.v1.yaml`.
    "archivo": re.compile(r"\b[\w-]+\.(?:cs|csproj|sln|py|ts|tsx|go|java|json|ya?ml|proto)\b"),
    # Proyectos y espacios de nombres de .NET: `Flit.Ict.Infrastructure`, `Flit.Gateway`.
    "proyecto": re.compile(r"\bFlit\.[A-Z]\w*"),
    # Números de feature, bug o ticket: `Feature #10888`, `Bug #13304`, `#1234`.
    "ticket": re.compile(r"(?i)#\d{3,}\b|\b(?:feature|bug|pbi|task|issue|ticket)\s*#?\s*\d+"),
    # Comandos del equipo.
    "comando": re.compile(
        r"\b(?:python3?|uv|dotnet|npm|npx|pnpm|kubectl|helm|git|docker)\s+(?:-m|run|test|build|install|apply|exec)\b"
    ),
    # Nombres de servicios, repos y módulos internos (`core-ict`, `core-api`, `atlas_docs`, los legados de FLIT 1).
    "servicio_interno": re.compile(
        r"(?i)\bcore-[a-z]+\b|\bpython-ml\b|\bflit-(?:gitops|atlas|scheduler)\b|\batlas_docs\b|\bBackApi\w*"
    ),
    # Piezas de infraestructura que el cliente no ve.
    "infraestructura": re.compile(r"\b(?:YARP|gRPC|Hangfire|RabbitMQ|ASP\.NET|Kestrel|Argo CD|k3s|Kubernetes)\b"),
    # Clases y métodos del código: `RegisterRowInput`, `WebhookNotificationJob`, `TryDeliverAsync`.
    "clase": re.compile(
        r"\b[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+(?:Job|Service|Controller|Handler|Endpoints|Input|Dto|Repository|Worker|"
        r"Query|Command|Policy|Async)\b"
    ),
    # Hosts e IP internos.
    "host_interno": re.compile(r"(?i)\blocalhost\b|\.svc\b|\.cluster\.local\b|\.internal\b"),
    "ip": re.compile(r"(?<![\d.])\d{1,3}(?:\.\d{1,3}){3}(?!\.?\d)"),
    # Decisiones y notas del equipo: ADR, pendientes, el Excel de trabajo y sus hojas.
    "nota_interna": re.compile(r"\b(?:ATLAS-)?ADR-\d+|\[por verificar\]|\bTODO\b|\bFIXME\b|(?i:\bexcel\b)"),
}

# Claves cuyo valor es dato del autor (ejemplos, valores): sus textos no son descripciones.
_DATOS = ("example", "default", "enum", "const", "value")


@dataclass(frozen=True)
class PiezaInterna:
    puntero: str
    tipo: str
    muestra: str


def _escapar(segmento: str) -> str:
    return segmento.replace("~", "~0").replace("/", "~1")


def piezas_en_texto(texto: str) -> list[tuple[str, str]]:
    """(tipo, coincidencia) de cada pieza interna de `texto`."""
    return [(tipo, m.group(0)) for tipo, patron in PATRONES.items() for m in patron.finditer(texto)]


def textos(nodo: object, puntero: str = "") -> Iterator[tuple[str, str]]:
    """(puntero, texto) de cada `description`, `summary` y `title` que lee el cliente, y de `x-enum-descriptions`.

    No entra a las demás extensiones `x-*` (procedencia del equipo, la poda las quita) ni a los datos de los ejemplos;
    sí a los `summary` y `description` de los objetos de ejemplo (`examples` como mapa). `x-enum-descriptions` sale
    al cliente (la poda la deja pasar) y por eso se revisa.
    """
    if isinstance(nodo, dict):
        for clave, valor in nodo.items():
            hijo = f"{puntero}/{_escapar(str(clave))}"
            if clave == "x-enum-descriptions":
                valores = valor.values() if isinstance(valor, dict) else valor if isinstance(valor, list) else []
                yield from ((f"{hijo}/{i}", v) for i, v in enumerate(valores) if isinstance(v, str))
                continue
            if str(clave).startswith("x-") or clave in _DATOS:
                continue
            if clave == "examples" and isinstance(valor, list):
                continue  # `examples` de JSON Schema: lista de datos
            if clave in CLAVES_TEXTO and isinstance(valor, str):
                yield hijo, valor
                continue
            yield from textos(valor, hijo)
    elif isinstance(nodo, list):
        for indice, valor in enumerate(nodo):
            yield from textos(valor, f"{puntero}/{indice}")


def piezas_internas(spec: object) -> list[PiezaInterna]:
    """Cada pieza interna que aparece en un texto de cara al cliente del spec."""
    return [
        PiezaInterna(puntero, tipo, muestra)
        for puntero, texto in textos(spec)
        for tipo, muestra in piezas_en_texto(texto)
    ]
