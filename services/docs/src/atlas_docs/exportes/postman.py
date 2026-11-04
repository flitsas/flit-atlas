"""OpenAPI → colección Postman v2.1 (el mínimo que necesita un integrador).

No hay un conversor maduro en Python: el de referencia es `openapi-to-postmanv2` (Node), que Atlas no corre en
producción (A-02). Este arma una colección por spec OpenAPI con:

- variables de colección `{{baseUrl}}` (el servidor de QA si se reconoce; si no, el primero) y `{{token}}` (vacía);
- autenticación Bearer `{{token}}` en la colección cuando el spec la declara, y `noauth` en las operaciones con
  `security: []`;
- una carpeta por etiqueta y una petición por operación, con las variables de ruta (`:id`), los parámetros de
  consulta (activos solo los obligatorios), las cabeceras y el ejemplo del cuerpo.

Sin scripts (`event`) ni URL reales de producción en las variables. Los webhooks no van: los envía FLIT.
"""

import json
import re
import uuid
from typing import Any

from atlas_docs.exportes.contenido import Constructor, Operacion
from atlas_docs.exportes.documento import Documento
from atlas_docs.exportes.specs import SpecLeido

ESQUEMA_V21 = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
_ESPACIO_ATLAS = uuid.UUID("5b1f2c4e-7a1d-4c8e-9f3a-0d2b6e8a4c10")  # espacio de nombres de los id deterministas


def _id(*partes: str) -> str:
    return str(uuid.uuid5(_ESPACIO_ATLAS, "|".join(partes)))


def _base_url(constructor: Constructor) -> str:
    ambientes = constructor.ambientes()
    qa = next((a for a in ambientes if "QA" in a.nombre.upper() or ".qa." in a.url), None)
    elegido = qa or (ambientes[0] if ambientes else None)
    return elegido.url.rstrip("/") if elegido else "https://api.ejemplo.com"


def _usa_bearer(doc: dict[str, Any]) -> bool:
    esquemas = (doc.get("components") or {}).get("securitySchemes") or {}
    return any(
        isinstance(e, dict) and (e.get("type") in ("oauth2", "openIdConnect") or e.get("scheme") == "bearer")
        for e in esquemas.values()
    )


def _url(op: Operacion) -> dict[str, Any]:
    ruta_postman = re.sub(r"\{([^}/]+)\}", r":\1", op.ruta)
    segmentos = [s for s in ruta_postman.split("/") if s]
    consulta = [
        {
            "key": p.nombre,
            "value": "",
            "description": p.descripcion or p.tipo,
            "disabled": not p.obligatorio,
        }
        for p in op.parametros
        if p.ubicacion == "consulta"
    ]
    variables = [
        {"key": p.nombre, "value": "", "description": p.descripcion or p.tipo}
        for p in op.parametros
        if p.ubicacion == "ruta"
    ]
    raw = "{{baseUrl}}" + ruta_postman
    activos = [q for q in consulta if not q["disabled"]]
    if activos:
        raw += "?" + "&".join(f"{q['key']}=" for q in activos)
    url: dict[str, Any] = {"raw": raw, "host": ["{{baseUrl}}"], "path": segmentos}
    if consulta:
        url["query"] = consulta
    if variables:
        url["variable"] = variables
    return url


def _peticion(op: Operacion, bearer_en_coleccion: bool) -> dict[str, Any]:
    cabeceras = [{"key": "Accept", "value": "application/json"}]
    if op.tipo_contenido:
        cabeceras.append({"key": "Content-Type", "value": op.tipo_contenido})
    for linea in op.autenticacion:
        clave, _, valor = linea.partition(": ")
        if clave not in ("Authorization", "Cookie") and not clave.startswith("?") and valor == "{{token}}":
            cabeceras.append({"key": clave, "value": "{{token}}"})
    cabeceras.extend(
        {"key": p.nombre, "value": "", "description": p.descripcion or p.tipo}
        for p in op.parametros
        if p.ubicacion == "cabecera"
    )
    peticion: dict[str, Any] = {
        "method": op.metodo,
        "header": cabeceras,
        "url": _url(op),
        "description": (op.descripcion or op.resumen or op.operation_id),
    }
    if not op.autenticacion:
        peticion["auth"] = {"type": "noauth"}
    elif not bearer_en_coleccion and any(a.startswith("Authorization: Bearer") for a in op.autenticacion):
        peticion["auth"] = {"type": "bearer", "bearer": [{"key": "token", "value": "{{token}}", "type": "string"}]}
    if op.tipo_contenido:
        if op.tipo_contenido.endswith("json"):
            cuerpo = op.ejemplo_entrada if op.ejemplo_entrada is not None else "{}"
            peticion["body"] = {"mode": "raw", "raw": cuerpo, "options": {"raw": {"language": "json"}}}
        elif op.tipo_contenido == "multipart/form-data":
            peticion["body"] = {
                "mode": "formdata",
                "formdata": [{"key": c.nombre, "value": "", "type": "text"} for c in op.cuerpo if c.nivel == 0],
            }
        else:
            peticion["body"] = {"mode": "raw", "raw": op.ejemplo_entrada or ""}
    return peticion


def coleccion(spec: SpecLeido, documento: Documento) -> dict[str, Any]:
    constructor = Constructor(spec)
    operaciones = [op for op in constructor.operaciones_openapi() if not op.es_webhook]
    bearer = _usa_bearer(spec.documento)
    carpetas: dict[str, list[dict[str, Any]]] = {}
    for op in operaciones:
        carpetas.setdefault(op.etiqueta, []).append(
            {
                "id": _id(documento.hash, spec.contrato, op.ancla),
                "name": op.titulo,
                "request": _peticion(op, bearer),
                "response": [],
            }
        )
    descripcion = (
        f"{spec.titulo} {spec.version} · paquete «{documento.paquete}», revisión {documento.revision}.\n\n"
        f"{documento.leyenda}\n\nHash SHA-256 del contenido exportado: {documento.hash}\n\n"
        "Define `token` con el que te entregue el servicio de autenticación; `baseUrl` apunta a QA."
    )
    resultado: dict[str, Any] = {
        "info": {
            "_postman_id": _id(documento.hash, spec.contrato),
            "name": f"{documento.paquete} · {spec.titulo} {spec.version}",
            "description": descripcion,
            "schema": ESQUEMA_V21,
        },
        "item": [
            {"id": _id(documento.hash, spec.contrato, "carpeta", nombre), "name": nombre, "item": items}
            for nombre, items in carpetas.items()
        ],
        "variable": [
            {"key": "baseUrl", "value": _base_url(constructor), "type": "string"},
            {"key": "token", "value": "", "type": "string"},
        ],
    }
    if bearer:
        resultado["auth"] = {"type": "bearer", "bearer": [{"key": "token", "value": "{{token}}", "type": "string"}]}
    return resultado


def a_json(coleccion_postman: dict[str, Any]) -> str:
    return json.dumps(coleccion_postman, ensure_ascii=False, indent=2) + "\n"
