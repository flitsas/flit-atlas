"""Tipos de dato del Excel («String(12)», «number(17,2)», «Min String(10) Max String(50)») a JSON Schema."""

import re
from decimal import Decimal

_CADENA = re.compile(r"^string(?:\((\d+)\))?$")
_MIN_MAX = re.compile(r"^min\s*string\((\d+)\)\s*max\s*string\((\d+)\)$")
_NUMERO = re.compile(r"^number(?:\((\d+)(?:,(\d+))?\))?$")


def esquema_desde_tipo(texto: str | None) -> dict | None:
    """El fragmento de esquema que describe el tipo del Excel, o None si el texto no es un tipo conocido."""
    if texto is None:
        return None
    limpio = re.sub(r"\s+", " ", str(texto).strip().strip('"').strip()).lower()
    if not limpio or limpio == "n/a":
        return None
    if m := _MIN_MAX.match(limpio):
        return {"type": "string", "minLength": int(m.group(1)), "maxLength": int(m.group(2))}
    if m := _CADENA.match(limpio):
        return {"type": "string", "maxLength": int(m.group(1))} if m.group(1) else {"type": "string"}
    if m := _NUMERO.match(limpio):
        digitos, decimales = m.group(1), m.group(2)
        if digitos is None:
            return {"type": "number"}
        if decimales is None:
            # number(n): entero de n dígitos.
            return {"type": "integer", "minimum": 0, "maximum": 10 ** int(digitos) - 1}
        # number(p,s): p dígitos con s decimales. Sin multipleOf: con decimales binarios da falsos negativos al
        # validar ejemplos; los decimales quedan en una extensión.
        paso = Decimal(1).scaleb(-int(decimales))
        maximo = Decimal(10) ** (int(digitos) - int(decimales)) - paso
        return {"type": "number", "maximum": float(maximo), "x-flit-decimales": int(decimales)}
    if limpio == "integer":
        return {"type": "integer"}
    if limpio == "boolean":
        return {"type": "boolean"}
    if limpio == "base64":
        return {"type": "string", "contentEncoding": "base64"}
    if limpio == "list":
        return {"type": "array"}
    return None


def familia(tipo: object) -> str | None:
    """«texto», «numero», «logico», «lista» u «objeto»: para comparar tipos sin pelear por integer frente a number."""
    if isinstance(tipo, list):
        tipo = next((t for t in tipo if t != "null"), None)
    return {
        "string": "texto",
        "integer": "numero",
        "number": "numero",
        "boolean": "logico",
        "array": "lista",
        "object": "objeto",
    }.get(tipo if isinstance(tipo, str) else "")
