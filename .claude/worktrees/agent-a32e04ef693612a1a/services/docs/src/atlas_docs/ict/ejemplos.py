"""Lectura tolerante de los ejemplos del Excel.

Los ejemplos se escribieron a mano: comillas tipográficas, comas al final, comas que faltan, varios objetos seguidos
en la misma celda, URL sueltas o un `curl`. Esta lectura los repara cuando el arreglo es mecánico y dice qué reparó;
lo que no puede leer queda como texto y lo reporta la conversión.
"""

import json
import re
import shlex
from dataclasses import dataclass, field
from urllib.parse import urlsplit


@dataclass
class Ejemplo:
    tipo: str  # "json" | "urls" | "curl" | "texto"
    valores: list = field(default_factory=list)  # json: documentos; urls: textos; curl: un dict
    reparaciones: list[str] = field(default_factory=list)
    error: str | None = None


_COMILLAS = str.maketrans({"“": '"', "”": '"', "‘": "'", "’": "'"})
_COMA_FINAL = re.compile(r",(\s*[}\]])")
_COMA_FALTANTE = re.compile(r'(["\d}\]]|true|false|null)([ \t]*\r?\n\s*")')


def _limpiar_claves(valor: object, reparaciones: list[str]) -> object:
    if isinstance(valor, dict):
        limpio = {}
        for clave, interno in valor.items():
            if clave != clave.strip():
                reparaciones.append(f"clave con espacios sobrantes: «{clave.strip()}»")
            limpio[clave.strip()] = _limpiar_claves(interno, reparaciones)
        return limpio
    if isinstance(valor, list):
        return [_limpiar_claves(v, reparaciones) for v in valor]
    return valor


def _documentos_json(texto: str) -> list:
    decodificador = json.JSONDecoder()
    documentos, i = [], 0
    while i < len(texto):
        while i < len(texto) and texto[i].isspace():
            i += 1
        if i >= len(texto):
            break
        valor, i = decodificador.raw_decode(texto, i)
        documentos.append(valor)
    return documentos


def _json(texto: str) -> Ejemplo:
    reparaciones: list[str] = []
    candidato = texto.translate(_COMILLAS)
    if candidato != texto:
        reparaciones.append("comillas tipográficas cambiadas por comillas rectas")
    for nombre, patron, reemplazo in (
        ("comas sobrantes antes de cerrar un objeto o una lista", _COMA_FINAL, r"\1"),
        ("comas que faltaban entre propiedades", _COMA_FALTANTE, r"\1,\2"),
    ):
        try:
            documentos = _documentos_json(candidato)
            break
        except json.JSONDecodeError:
            nuevo = patron.sub(reemplazo, candidato)
            if nuevo != candidato:
                reparaciones.append(nombre)
                candidato = nuevo
    else:
        try:
            documentos = _documentos_json(candidato)
        except json.JSONDecodeError as error:
            return Ejemplo("texto", [texto], reparaciones, f"no es JSON válido ni con reparaciones ({error.msg})")
    if len(documentos) > 1:
        reparaciones.append(f"{len(documentos)} documentos JSON seguidos en la misma celda")
    return Ejemplo("json", _limpiar_claves(documentos, reparaciones), reparaciones)


def _curl(texto: str) -> Ejemplo:
    try:
        partes = shlex.split(texto.replace("\\\n", " "), posix=True)
    except ValueError as error:
        return Ejemplo("texto", [texto], [], f"curl ilegible ({error})")
    resultado: dict = {"url": None, "metodo": None, "cabeceras": {}, "formulario": {}}
    i = 1
    while i < len(partes):
        parte, siguiente = partes[i], partes[i + 1] if i + 1 < len(partes) else ""
        if parte in ("--request", "-X"):
            resultado["metodo"] = siguiente.lower()
        elif parte == "--url":
            resultado["url"] = siguiente
        elif parte in ("--header", "-H"):
            nombre, _, valor = siguiente.partition(":")
            resultado["cabeceras"][nombre.strip().lower()] = valor.strip()
        elif parte in ("--form", "-F"):
            nombre, _, valor = siguiente.partition("=")
            resultado["formulario"][nombre.strip()] = valor
        else:
            i += 1
            continue
        i += 2
    return Ejemplo("curl", [resultado])


def interpretar(texto: str | None) -> Ejemplo | None:
    if texto is None or not str(texto).strip():
        return None
    texto = str(texto).strip()
    if texto.lower().startswith("curl "):
        return _curl(texto)
    lineas = [linea.strip() for linea in texto.splitlines() if linea.strip()]
    if lineas and all(re.search(r"https?://", linea) for linea in lineas):
        urls = [re.search(r"https?://\S+", linea).group(0) for linea in lineas]  # type: ignore[union-attr]
        return Ejemplo("urls", urls)
    if texto[0] in "[{":
        return _json(texto)
    return Ejemplo("texto", [texto], [], "no es JSON, URL ni curl")


def host(url: str) -> str:
    return (urlsplit(url).hostname or "").lower()
