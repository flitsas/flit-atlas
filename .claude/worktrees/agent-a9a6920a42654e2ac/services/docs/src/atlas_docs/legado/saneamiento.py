"""Saneamiento de lo que llega del Excel y de los swagger legados (prompt maestro §8.4, punto 7).

Dos piezas que comparten los mismos patrones:

- `sanear_texto` reemplaza lo sensible por valores ficticios (`$TOKEN`, `usuario@ejemplo.com`, `documento.pdf`,
  `api.ejemplo.com`, una IP de documentación) y cuenta cuántos reemplazos hizo de cada tipo, sin guardar el valor
  original.
- `buscar_sensibles` solo detecta. La usan la conversión (falla si algo sobrevive) y la prueba que recorre los specs
  versionados en `contracts/legado/`.
"""

import re
from collections import Counter
from dataclasses import dataclass

# Únicos correos que pueden quedar en un spec: los del dominio de ejemplo.
CORREO_FICTICIO = "usuario@ejemplo.com"
HOST_FICTICIO = "api.ejemplo.com"
IP_DOCUMENTACION = "192.0.2.10"  # RFC 5737 (TEST-NET-1)

PATRONES: dict[str, re.Pattern[str]] = {
    # JWT y similares: cabecera base64url de un objeto JSON («eyJ»), con o sin firma.
    "jwt": re.compile(r"eyJ[A-Za-z0-9_-]{8,}(?:\.[A-Za-z0-9_-]*){0,2}"),
    # Un valor real después de «Bearer» (las variables `$TOKEN` y `access_token` del Excel no cuentan).
    # En la misma línea y con al menos un dígito o punto: «scheme: bearer» + «bearerFormat» no es un token.
    "bearer": re.compile(
        r"(?i)\bbearer[ \t]+(?!\$TOKEN\b|access_token\b)(?=[A-Za-z0-9._~+/-]*[\d.])[A-Za-z0-9._~+/-]{12,}=*"
    ),
    "correo": re.compile(r"[A-Za-z0-9._%+-]+@(?!ejemplo\.com\b)[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+"),
    "ruta_windows": re.compile(r"(?<![A-Za-z0-9])[A-Za-z]:\\[^\s'\"<>|]+"),
    "ruta_unix": re.compile(r"(?<![\w.:/])/(?:home|Users|root)/[^\s'\"<>]+"),
    "ip_privada": re.compile(
        r"(?<![\d.])(?:10\.\d{1,3}|127\.\d{1,3}|192\.168|172\.(?:1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}(?![\d.])"
    ),
    "host_interno": re.compile(
        r"(?i)\b(?:localhost|[a-z0-9-]+\.execute-api\.[a-z0-9-]+\.amazonaws\.com|dev\.azure\.com"
        r"|[a-z0-9.-]+\.svc\.cluster\.local|[a-z0-9-]+\.(?:internal|lan))\b"
    ),
}

_URL = re.compile(r"(?i)\b(?:https?://|www\.)[^\s'\"<>]+")
_EXTENSION = re.compile(r"\.([A-Za-z0-9]{2,5})$")


@dataclass(frozen=True)
class Hallazgo:
    tipo: str
    muestra: str  # enmascarada: nunca el valor completo


def _enmascarar(valor: str) -> str:
    return valor[:4] + "…" if len(valor) > 4 else "…"


def buscar_sensibles(texto: str) -> list[Hallazgo]:
    """Todo lo que en `texto` parece credencial, correo real, ruta local, IP privada o host interno."""
    return [
        Hallazgo(tipo, _enmascarar(m.group(0))) for tipo, patron in PATRONES.items() for m in patron.finditer(texto)
    ]


def _ruta_ficticia(ruta: str) -> str:
    extension = _EXTENSION.search(ruta.rstrip("'\""))
    return f"documento.{extension.group(1).lower()}" if extension else "documento"


def sanear_texto(texto: str, cuenta: Counter[str] | None = None) -> str:
    """Reemplaza lo sensible de `texto` y suma en `cuenta` los reemplazos por tipo."""
    cuenta = cuenta if cuenta is not None else Counter()

    def reemplazar(tipo: str, patron: re.Pattern[str], nuevo, origen: str) -> str:
        def sustituto(m: re.Match[str]) -> str:
            cuenta[tipo] += 1
            return nuevo(m.group(0)) if callable(nuevo) else nuevo

        return patron.sub(sustituto, origen)

    # Las URL con host interno se reemplazan enteras: la ruta puede delatar organización, proyecto o ambiente.
    def url(m: re.Match[str]) -> str:
        valor = m.group(0)
        if PATRONES["host_interno"].search(valor) or PATRONES["ip_privada"].search(valor):
            cuenta["host_interno"] += 1
            return f"https://{HOST_FICTICIO}"
        return valor

    texto = _URL.sub(url, texto)
    texto = reemplazar("bearer", PATRONES["bearer"], "Bearer $TOKEN", texto)
    texto = reemplazar("jwt", PATRONES["jwt"], "$TOKEN", texto)
    texto = reemplazar("correo", PATRONES["correo"], CORREO_FICTICIO, texto)
    texto = reemplazar("ruta_local", PATRONES["ruta_windows"], _ruta_ficticia, texto)
    texto = reemplazar("ruta_local", PATRONES["ruta_unix"], _ruta_ficticia, texto)
    texto = reemplazar("ip_privada", PATRONES["ip_privada"], IP_DOCUMENTACION, texto)
    return reemplazar("host_interno", PATRONES["host_interno"], HOST_FICTICIO, texto)


def _norma(clave: str) -> str:
    return re.sub(r"[^a-z0-9]", "", clave.lower())


# Valores ficticios por tipo de dato personal, según el nombre de la clave del ejemplo (normalizado: minúsculas, sin
# separadores). El orden importa: gana la primera regla que coincide.
_REGLAS_CLAVE: list[tuple[str, re.Pattern[str], str | None]] = [
    ("credencial", re.compile(r"(password|contrasena|clave|secret)$"), "$CLAVE"),
    ("credencial", re.compile(r"(token|accesstoken)$"), "$TOKEN"),
    ("correo", re.compile(r"(email|mail|mailregistered)$"), CORREO_FICTICIO),
    ("persona", re.compile(r"(firstlastname)$"), "Apellido Demo"),
    ("persona", re.compile(r"(secondlastname)$"), "Segundo Demo"),
    (
        "organizacion",
        re.compile(r"(companyregistered|companyname|relatedcompanycompanyname)$"),
        "Organismo Demo",
    ),
    ("persona", re.compile(r"^(?!file)(.*name|manageruser|usernameregistered|limitationscreditor|username)$"), None),
    (
        "documento",
        re.compile(r"(documentnumber|companymanagerdocument|companydocument|ownerdocumentnumber)$"),
        "900123456",
    ),
    ("telefono", re.compile(r"phone$"), "3001234567"),
    ("placa", re.compile(r"^plate$"), "ABC123"),
    # El id propio del gestor puede delatar al cliente (prefijos de su herramienta).
    ("documento", re.compile(r"^manageridtransaction$"), "GESTOR-0001"),
    ("vin", re.compile(r"^vin$"), "VIN00000000000001"),
]


def _persona(clave: str) -> str:
    # El usuario de login es un correo en el Excel; el resto son nombres de personas o empresas.
    return CORREO_FICTICIO if _norma(clave) == "username" else "Nombre Demo"


def sanear_ejemplo(valor: object, cuenta: Counter[str] | None = None, clave: str = "") -> object:
    """Sanea un ejemplo JSON: valores de texto por patrón y datos personales por el nombre de la clave."""
    cuenta = cuenta if cuenta is not None else Counter()
    if isinstance(valor, dict):
        return {k: sanear_ejemplo(v, cuenta, k) for k, v in valor.items()}
    if isinstance(valor, list):
        return [sanear_ejemplo(v, cuenta, clave) for v in valor]
    if clave and isinstance(valor, str) and valor.strip():
        norma = _norma(clave)
        for tipo, regla, nuevo in _REGLAS_CLAVE:
            if regla.search(norma):
                reemplazo = _persona(clave) if nuevo is None else str(nuevo)
                if valor != reemplazo:
                    cuenta[tipo] += 1
                return reemplazo
    return sanear_texto(valor, cuenta) if isinstance(valor, str) else valor


def sanear_arbol(nodo: object, cuenta: Counter[str] | None = None) -> object:
    """Pasa `sanear_texto` por cada texto (claves incluidas) de un árbol JSON: la última barrera antes de escribir."""
    cuenta = cuenta if cuenta is not None else Counter()
    if isinstance(nodo, dict):
        return {sanear_texto(k, cuenta): sanear_arbol(v, cuenta) for k, v in nodo.items()}
    if isinstance(nodo, list):
        return [sanear_arbol(v, cuenta) for v in nodo]
    return sanear_texto(nodo, cuenta) if isinstance(nodo, str) else nodo
