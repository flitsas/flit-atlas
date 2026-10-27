"""Modelo neutro del documento que comparten el HTML, el PDF, el Word y el Excel.

Se arma una vez desde los specs leídos: contratos con sus ambientes, operaciones (parámetros, cuerpo, ejemplos y
respuestas), la matriz de obligatoriedad (`x-flit-obligatoriedad`) y las tablas de referencia (esquemas con `enum` y
`x-enum-descriptions`, o con `x-flit-tabla`), con las mismas reglas que la vista del contrato de la PWA
(`apps/web/src/modulos/contratos/spec/referencia.ts`, HU 3.9).
"""

import json
import re
import unicodedata
from dataclasses import dataclass, field
from typing import Any

from atlas_docs.exportes.specs import SpecLeido

METODOS = ("get", "put", "post", "delete", "options", "head", "patch", "trace")
PROFUNDIDAD_MAXIMA = 4
MAX_CAMPOS = 250

NIVELES = {
    "obligatorio": "obligatorio",
    "obligatoria": "obligatorio",
    "si": "obligatorio",
    "opcional": "opcional",
    "no_aplica": "no_aplica",
    "no aplica": "no_aplica",
    "na": "no_aplica",
}
# Como en el Excel de contratos: S (sí), N (no) y N/A (no aplica a ese tipo de trámite).
SIGLAS_OBLIGATORIEDAD = {"obligatorio": "S", "opcional": "N", "no_aplica": "N/A"}
ETIQUETAS_OBLIGATORIEDAD = {"obligatorio": "Obligatorio", "opcional": "Opcional", "no_aplica": "No aplica"}

UBICACIONES = {"path": "ruta", "query": "consulta", "header": "cabecera", "cookie": "cookie"}


@dataclass
class Campo:
    nombre: str
    ubicacion: str  # ruta, consulta, cabecera, cookie, cuerpo, salida
    tipo: str
    obligatorio: bool
    descripcion: str = ""
    restricciones: str = ""
    nivel: int = 0


@dataclass
class Respuesta:
    codigo: str
    descripcion: str
    campos: list[Campo] = field(default_factory=list)
    ejemplo: str | None = None


@dataclass
class TipoTramite:
    clave: str
    nombre: str
    valor: str | None = None


@dataclass
class FilaMatriz:
    campo: str
    detalle: str
    valores: dict[str, str]  # clave del tipo → obligatorio | opcional | no_aplica
    condicion: str = ""


@dataclass
class Matriz:
    esquema: str
    propiedad: str
    clase: str  # campos o documentos
    tipos: list[TipoTramite]
    filas: list[FilaMatriz]

    @property
    def titulo(self) -> str:
        if self.clase == "documentos":
            return "Guía de documentos adjuntos por tipo de trámite"
        return "Obligatoriedad por tipo de trámite"

    def fila(self, campo: str) -> FilaMatriz | None:
        return next((f for f in self.filas if f.campo == campo), None)


@dataclass
class Tabla:
    id: str
    titulo: str
    descripcion: str
    columnas: list[str]
    filas: list[list[str]]


@dataclass
class Ambiente:
    nombre: str
    url: str


@dataclass
class Operacion:
    ancla: str
    operation_id: str
    metodo: str
    ruta: str
    resumen: str
    descripcion: str
    etiqueta: str
    autenticacion: list[str]
    parametros: list[Campo]
    cuerpo: list[Campo]
    tipo_contenido: str | None
    ejemplo_entrada: str | None
    respuestas: list[Respuesta]
    es_webhook: bool = False
    deprecada: bool = False
    matriz: Matriz | None = None
    documentos: Matriz | None = None
    tablas: list[Tabla] = field(default_factory=list)

    @property
    def titulo(self) -> str:
        return self.resumen or self.operation_id

    @property
    def cabeceras(self) -> list[str]:
        salida = ["Accept: application/json"]
        if self.tipo_contenido:
            salida.append(f"Content-Type: {self.tipo_contenido}")
        salida.extend(self.autenticacion)
        salida.extend(f"{p.nombre}: <{p.tipo}>" for p in self.parametros if p.ubicacion == "cabecera")
        return salida


@dataclass
class Contrato:
    ancla: str
    contrato: str
    version: str
    tipo: str
    titulo: str
    descripcion: str
    ambientes: list[Ambiente]
    operaciones: list[Operacion]
    matrices: list[Matriz]
    tablas: list[Tabla]
    historial: list[dict[str, str]]


def json_legible(valor: Any) -> str:
    return json.dumps(valor, ensure_ascii=False, indent=2)


def ancla(*partes: str) -> str:
    """Identificador ASCII para anclas y nombres de archivo («Integración ICT» → «integracion-ict»)."""
    sin_tildes = unicodedata.normalize("NFKD", "-".join(partes))
    texto = "".join(c for c in sin_tildes if not unicodedata.combining(c)).lower()
    return re.sub(r"[^a-z0-9]+", "-", texto).strip("-") or "seccion"


def texto(valor: Any) -> str:
    if valor is None:
        return ""
    return str(valor).strip()


class _Resolutor:
    """Resuelve `$ref` locales (`#/...`) del spec y anota los esquemas de `components` que se visitan."""

    def __init__(self, documento: dict[str, Any]) -> None:
        self.documento = documento

    def resolver(self, ref: str) -> Any:
        if not ref.startswith("#/"):
            return None  # el spec podado es autocontenido; una referencia externa queda como nombre
        nodo: Any = self.documento
        for parte in ref[2:].split("/"):
            parte = parte.replace("~1", "/").replace("~0", "~")
            if isinstance(nodo, dict) and parte in nodo:
                nodo = nodo[parte]
            elif isinstance(nodo, list) and parte.isdigit() and int(parte) < len(nodo):
                nodo = nodo[int(parte)]
            else:
                return None
        return nodo

    def seguir(self, nodo: Any, visitados: set[str] | None = None) -> tuple[dict[str, Any], set[str]]:
        """Sigue la cadena de `$ref` hasta un objeto. Devuelve el nodo y los nombres de esquema visitados."""
        nombres: set[str] = set()
        vistos = set(visitados or ())
        while isinstance(nodo, dict) and isinstance(nodo.get("$ref"), str):
            ref = nodo["$ref"]
            if ref in vistos:
                return {}, nombres
            vistos.add(ref)
            if ref.startswith("#/components/schemas/"):
                nombres.add(ref.rsplit("/", 1)[-1])
            nodo = self.resolver(ref)
        return (nodo if isinstance(nodo, dict) else {}), nombres

    def aplanar(self, crudo: Any, refs: set[str] | None = None) -> tuple[dict[str, Any], set[str]]:
        """Une `allOf` y las variantes de `oneOf`/`anyOf` en un solo objeto (propiedades y requeridos).

        En `oneOf`/`anyOf` una propiedad es obligatoria solo si lo es en todas las variantes.
        """
        refs = set(refs or ())
        nodo, nombres = self.seguir(crudo, refs)
        refs |= {f"#/components/schemas/{n}" for n in nombres}
        partes_all = nodo.get("allOf") if isinstance(nodo.get("allOf"), list) else []
        variantes = next((nodo[k] for k in ("oneOf", "anyOf") if isinstance(nodo.get(k), list)), [])
        if not partes_all and not variantes:
            return nodo, nombres
        unido: dict[str, Any] = {k: v for k, v in nodo.items() if k not in ("allOf", "oneOf", "anyOf")}
        propiedades: dict[str, Any] = dict(unido.get("properties") or {})
        requeridos: set[str] = set(unido.get("required") or [])
        for parte in partes_all:
            sub, sub_nombres = self.aplanar(parte, refs)
            nombres |= sub_nombres
            for clave, valor in (sub.get("properties") or {}).items():
                propiedades.setdefault(clave, valor)
            requeridos |= set(sub.get("required") or [])
            for clave in ("type", "description", "items", "enum", "x-enum-descriptions", "examples", "example"):
                if clave in sub and clave not in unido:
                    unido[clave] = sub[clave]
        if variantes:
            comunes: set[str] | None = None
            for variante in variantes:
                sub, sub_nombres = self.aplanar(variante, refs)
                nombres |= sub_nombres
                for clave, valor in (sub.get("properties") or {}).items():
                    propiedades.setdefault(clave, valor)
                propios = set(sub.get("required") or [])
                comunes = propios if comunes is None else comunes & propios
                for clave in ("type", "items", "enum"):
                    if clave in sub and clave not in unido:
                        unido[clave] = sub[clave]
            requeridos |= comunes or set()
        if propiedades:
            unido["properties"] = propiedades
            unido.setdefault("type", "object")
        unido["required"] = sorted(requeridos)
        return unido, nombres


def _tipo(nodo: dict[str, Any], nombres: set[str]) -> str:
    tipo = nodo.get("type")
    if isinstance(tipo, list):
        no_nulos = [t for t in tipo if t != "null"]
        tipo = no_nulos[0] if no_nulos else "null"
    legibles = {
        "string": "texto",
        "integer": "entero",
        "number": "número",
        "boolean": "booleano",
        "object": "objeto",
        "array": "lista",
        "null": "nulo",
    }
    base = legibles.get(str(tipo), "objeto" if "properties" in nodo else "valor")
    if isinstance(nodo.get("format"), str):
        base += f" ({nodo['format']})"
    elif nodo.get("contentEncoding") == "base64":
        base += " (base64)"
    tabla = next(iter(sorted(nombres)), None) if "enum" in nodo else None
    if tabla:
        base += f" · tabla {tabla}"
    return base


def _restricciones(nodo: dict[str, Any]) -> str:
    r: list[str] = []
    minimo, maximo = nodo.get("minLength"), nodo.get("maxLength")
    if isinstance(minimo, int) and isinstance(maximo, int):
        r.append(f"{minimo} a {maximo} caracteres")
    elif isinstance(maximo, int):
        r.append(f"máx. {maximo} caracteres")
    elif isinstance(minimo, int):
        r.append(f"mín. {minimo} caracteres")
    vmin, vmax = nodo.get("minimum"), nodo.get("maximum")
    if isinstance(vmin, int | float) and isinstance(vmax, int | float):
        r.append(f"de {_numero(vmin)} a {_numero(vmax)}")
    elif isinstance(vmax, int | float):
        r.append(f"hasta {_numero(vmax)}")
    elif isinstance(vmin, int | float):
        r.append(f"desde {_numero(vmin)}")
    if isinstance(nodo.get("x-flit-decimales"), int):
        r.append(f"{nodo['x-flit-decimales']} decimales")
    if isinstance(nodo.get("pattern"), str):
        r.append(f"patrón {nodo['pattern']}")
    if "const" in nodo:
        r.append(f"valor fijo {nodo['const']}")
    valores = nodo.get("enum")
    if isinstance(valores, list) and valores:
        muestra = ", ".join(str(v) for v in valores[:12])
        r.append(f"valores: {muestra}{'…' if len(valores) > 12 else ''}")
    if nodo.get("deprecated") is True:
        r.append("deprecado")
    return "; ".join(r)


def _numero(valor: float) -> str:
    """Número con separador de miles «.» y coma decimal (es-CO)."""
    if isinstance(valor, float) and not valor.is_integer():
        entero, decimales = f"{valor:,.2f}".split(".")
        return entero.replace(",", ".") + "," + decimales
    return f"{int(valor):,}".replace(",", ".")


class Constructor:
    def __init__(self, spec: SpecLeido) -> None:
        self.spec = spec
        self.doc = spec.documento
        self.r = _Resolutor(self.doc)
        self.matrices = _matrices(self.doc)
        self.tablas = _tablas(self.doc)
        self._anclas: set[str] = set()

    def _ancla_unica(self, base: str) -> str:
        candidata, n = base, 2
        while candidata in self._anclas:
            candidata, n = f"{base}-{n}", n + 1
        self._anclas.add(candidata)
        return candidata

    # Campos -----------------------------------------------------------------------------------------------------
    def campos(self, crudo: Any, ubicacion: str, prefijo: str = "", nivel: int = 0) -> tuple[list[Campo], set[str]]:
        salida: list[Campo] = []
        usados: set[str] = set()
        nodo, nombres = self.r.aplanar(crudo)
        usados |= nombres
        if nodo.get("type") == "array" or ("items" in nodo and "properties" not in nodo):
            items, nombres_items = self.r.aplanar(nodo.get("items"))
            usados |= nombres_items
            nodo = items
        requeridos = set(nodo.get("required") or [])
        for nombre, crudo_prop in (nodo.get("properties") or {}).items():
            if len(salida) >= MAX_CAMPOS:
                break
            prop, nombres_prop = self.r.aplanar(crudo_prop)
            usados |= nombres_prop
            ruta = f"{prefijo}{nombre}"
            es_lista = prop.get("type") == "array"
            interno = prop
            if es_lista:
                interno, nombres_items = self.r.aplanar(prop.get("items"))
                usados |= nombres_items
                nombres_prop = nombres_prop | nombres_items
            tipo = _tipo(interno, nombres_prop)
            salida.append(
                Campo(
                    nombre=ruta,
                    ubicacion=ubicacion,
                    tipo=f"lista de {tipo}" if es_lista else tipo,
                    obligatorio=nombre in requeridos,
                    descripcion=texto(prop.get("description") or interno.get("description")),
                    restricciones=_restricciones(interno if es_lista else prop),
                    nivel=nivel,
                )
            )
            if "properties" in interno and nivel + 1 < PROFUNDIDAD_MAXIMA:
                hijos, usados_hijos = self.campos(interno, ubicacion, f"{ruta}/", nivel + 1)
                salida.extend(hijos)
                usados |= usados_hijos
        return salida[:MAX_CAMPOS], usados

    def ejemplo(self, medio: dict[str, Any]) -> str | None:
        if "example" in medio:
            return json_legible(medio["example"])
        ejemplos = medio.get("examples")
        if isinstance(ejemplos, dict) and ejemplos:
            primero, _ = self.r.seguir(next(iter(ejemplos.values())))
            if "value" in primero:
                return json_legible(primero["value"])
        esquema, _ = self.r.seguir(medio.get("schema"))
        for candidato, envolver in ((esquema, False), (self.r.seguir(esquema.get("items"))[0], True)):
            if isinstance(candidato.get("examples"), list) and candidato["examples"]:
                valor = candidato["examples"][0]
                return json_legible([valor] if envolver else valor)
            if "example" in candidato:
                return json_legible([candidato["example"]] if envolver else candidato["example"])
        return None

    # Operaciones OpenAPI ----------------------------------------------------------------------------------------
    def autenticacion(self, operacion: dict[str, Any]) -> list[str]:
        requisitos = operacion.get("security", self.doc.get("security", []))
        if not requisitos:
            return []
        esquemas = (self.doc.get("components") or {}).get("securitySchemes") or {}
        cabeceras: list[str] = []
        primero = requisitos[0] if isinstance(requisitos, list) and requisitos else {}
        for nombre in primero if isinstance(primero, dict) else {}:
            esquema, _ = self.r.seguir(esquemas.get(nombre))
            tipo = esquema.get("type")
            if tipo == "apiKey":
                donde = esquema.get("in")
                clave = texto(esquema.get("name")) or nombre
                if donde == "header":
                    cabeceras.append(f"{clave}: {{{{token}}}}")
                elif donde == "cookie":
                    cabeceras.append(f"Cookie: {clave}=<sesión>")
                else:
                    cabeceras.append(f"?{clave}={{{{token}}}}")
            elif tipo == "http" and texto(esquema.get("scheme")).lower() == "basic":
                cabeceras.append("Authorization: Basic <usuario:clave en base64>")
            else:
                cabeceras.append("Authorization: Bearer {{token}}")
        return cabeceras

    def operacion(
        self, ruta: str, metodo: str, op: dict[str, Any], comunes: list[Any], webhook: bool = False
    ) -> Operacion:
        usados: set[str] = set()
        parametros: list[Campo] = []
        vistos: set[tuple[str, str]] = set()
        for crudo in [*(op.get("parameters") or []), *comunes]:
            p, _ = self.r.seguir(crudo)
            clave = (texto(p.get("name")), texto(p.get("in")))
            if not clave[0] or clave in vistos:
                continue
            vistos.add(clave)
            esquema, nombres = self.r.aplanar(p.get("schema"))
            usados |= nombres
            parametros.append(
                Campo(
                    nombre=clave[0],
                    ubicacion=UBICACIONES.get(clave[1], clave[1]),
                    tipo=_tipo(esquema, nombres),
                    obligatorio=bool(p.get("required")) or clave[1] == "path",
                    descripcion=texto(p.get("description") or esquema.get("description")),
                    restricciones=_restricciones(esquema),
                )
            )
        cuerpo: list[Campo] = []
        tipo_contenido = ejemplo_entrada = None
        cuerpo_crudo, _ = self.r.seguir(op.get("requestBody"))
        contenido = cuerpo_crudo.get("content") or {}
        if contenido:
            tipo_contenido = "application/json" if "application/json" in contenido else next(iter(contenido))
            medio = contenido[tipo_contenido] or {}
            cuerpo, usados_cuerpo = self.campos(medio.get("schema"), "cuerpo")
            usados |= usados_cuerpo
            ejemplo_entrada = self.ejemplo(medio)
        respuestas: list[Respuesta] = []
        for codigo, crudo in (op.get("responses") or {}).items():
            resp, _ = self.r.seguir(crudo)
            campos: list[Campo] = []
            ejemplo = None
            medios = resp.get("content") or {}
            if medios:
                medio = medios.get("application/json") or next(iter(medios.values())) or {}
                campos, usados_resp = self.campos(medio.get("schema"), "salida")
                usados |= usados_resp
                ejemplo = self.ejemplo(medio)
            respuestas.append(Respuesta(str(codigo), texto(resp.get("description")), campos, ejemplo))
        operation_id = texto(op.get("operationId")) or f"{metodo} {ruta}"
        matriz = next((m for m in self.matrices if m.esquema in usados and m.clase == "campos"), None)
        documentos = next((m for m in self.matrices if m.esquema in usados and m.clase == "documentos"), None)
        etiquetas = op.get("tags") or []
        return Operacion(
            ancla=self._ancla_unica(ancla(self.spec.contrato, operation_id)),
            operation_id=operation_id,
            metodo=metodo.upper(),
            ruta=ruta,
            resumen=texto(op.get("summary")),
            descripcion=texto(op.get("description")),
            etiqueta=texto(etiquetas[0]) if etiquetas else "General",
            autenticacion=self.autenticacion(op),
            parametros=parametros,
            cuerpo=cuerpo,
            tipo_contenido=tipo_contenido,
            ejemplo_entrada=ejemplo_entrada,
            respuestas=respuestas,
            es_webhook=webhook,
            deprecada=op.get("deprecated") is True,
            matriz=matriz,
            documentos=documentos,
            tablas=[t for t in self.tablas if t.id in usados],
        )

    def operaciones_openapi(self) -> list[Operacion]:
        salida: list[Operacion] = []
        for grupo, webhook in (("paths", False), ("webhooks", True)):
            for ruta, item_crudo in (self.doc.get(grupo) or {}).items():
                item, _ = self.r.seguir(item_crudo)
                comunes = item.get("parameters") or []
                for metodo in METODOS:
                    if isinstance(item.get(metodo), dict):
                        etiqueta_ruta = f"(webhook {ruta})" if webhook else ruta
                        salida.append(self.operacion(etiqueta_ruta, metodo, item[metodo], comunes, webhook))
        return salida

    # AsyncAPI ---------------------------------------------------------------------------------------------------
    def operaciones_asyncapi(self) -> list[Operacion]:
        salida: list[Operacion] = []
        canales = self.doc.get("channels") or {}
        if str(self.doc.get("asyncapi", "")).startswith("3"):
            for op_id, crudo in (self.doc.get("operations") or {}).items():
                op, _ = self.r.seguir(crudo)
                canal, _ = self.r.seguir(op.get("channel"))
                mensajes = [self.r.seguir(m)[0] for m in op.get("messages") or []] or [
                    self.r.seguir(m)[0] for m in (canal.get("messages") or {}).values()
                ]
                direccion = texto(canal.get("address"))
                salida.extend(self._mensajes(op_id, texto(op.get("action")), direccion, op, mensajes))
        else:
            for direccion, crudo in canales.items():
                canal, _ = self.r.seguir(crudo)
                for accion in ("publish", "subscribe"):
                    op = canal.get(accion)
                    if not isinstance(op, dict):
                        continue
                    mensaje, _ = self.r.seguir(op.get("message"))
                    mensajes = [self.r.seguir(m)[0] for m in mensaje.get("oneOf") or [mensaje]]
                    op_id = texto(op.get("operationId")) or f"{accion} {direccion}"
                    salida.extend(self._mensajes(op_id, accion, direccion, op, mensajes))
        return salida

    def _mensajes(
        self, op_id: str, accion: str, direccion: str, op: dict[str, Any], mensajes: list[dict[str, Any]]
    ) -> list[Operacion]:
        salida: list[Operacion] = []
        for mensaje in mensajes or [{}]:
            nombre = texto(mensaje.get("name")) or op_id
            campos, usados = self.campos(mensaje.get("payload"), "cuerpo") if mensaje.get("payload") else ([], set())
            ejemplos = mensaje.get("examples") or []
            ejemplo = (
                json_legible(ejemplos[0]["payload"])
                if ejemplos and isinstance(ejemplos[0], dict) and "payload" in ejemplos[0]
                else None
            )
            etiquetas = op.get("tags") or []
            salida.append(
                Operacion(
                    ancla=self._ancla_unica(ancla(self.spec.contrato, nombre)),
                    operation_id=nombre,
                    metodo=accion.upper() or "MENSAJE",
                    ruta=direccion,
                    resumen=texto(mensaje.get("title") or mensaje.get("summary") or op.get("summary")),
                    descripcion=texto(mensaje.get("description") or op.get("description")),
                    etiqueta=texto(etiquetas[0].get("name")) if etiquetas and isinstance(etiquetas[0], dict) else "",
                    autenticacion=[],
                    parametros=[],
                    cuerpo=campos,
                    tipo_contenido=texto(mensaje.get("contentType") or self.doc.get("defaultContentType")) or None,
                    ejemplo_entrada=ejemplo,
                    respuestas=[],
                    tablas=[t for t in self.tablas if t.id in usados],
                )
            )
        return salida

    # Contrato ---------------------------------------------------------------------------------------------------
    def ambientes(self) -> list[Ambiente]:
        salida: list[Ambiente] = []
        servidores = self.doc.get("servers") or []
        if isinstance(servidores, dict):  # AsyncAPI: mapa de servidores
            for nombre, s in servidores.items():
                s, _ = self.r.seguir(s)
                host = texto(s.get("host") or s.get("url"))
                salida.append(Ambiente(texto(s.get("title")) or nombre, f"{texto(s.get('protocol'))}://{host}"))
            return salida
        for s in servidores:
            if not isinstance(s, dict):
                continue
            url = texto(s.get("url"))
            for variable, definicion in (s.get("variables") or {}).items():
                if isinstance(definicion, dict) and "default" in definicion:
                    url = url.replace("{" + variable + "}", texto(definicion["default"]))
            nombre = texto(s.get("x-flit-ambiente")).upper() or texto(s.get("description")).split(".")[0]
            salida.append(Ambiente(nombre or "Servidor", url))
        return salida

    def construir(self) -> Contrato:
        operaciones = self.operaciones_openapi() if self.spec.tipo == "openapi" else self.operaciones_asyncapi()
        info = self.doc.get("info") or {}
        historial = [
            {
                "version": texto(e.get("version")),
                "fecha": _fecha_legible(texto(e.get("fecha"))),
                "cambio": texto(e.get("cambio")),
            }
            for e in info.get("x-flit-changelog") or []
            if isinstance(e, dict)
        ]
        return Contrato(
            ancla=ancla("contrato", self.spec.contrato),
            contrato=self.spec.contrato,
            version=self.spec.version,
            tipo="OpenAPI" if self.spec.tipo == "openapi" else "AsyncAPI",
            titulo=self.spec.titulo,
            descripcion=texto(info.get("description")),
            ambientes=self.ambientes(),
            operaciones=operaciones,
            matrices=self.matrices,
            tablas=self.tablas,
            historial=historial,
        )


def _fecha_legible(valor: str) -> str:
    """«2025-05-16» → «16/05/2025»; lo demás queda igual."""
    if m := re.fullmatch(r"(\d{4})-(\d{2})-(\d{2})", valor):
        return f"{m.group(3)}/{m.group(2)}/{m.group(1)}"
    return valor


def _esquemas(doc: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
    componentes = (doc.get("components") or {}).get("schemas") or {}
    return [(n, e) for n, e in componentes.items() if isinstance(e, dict)]


def _nivel(valor: Any) -> str:
    return NIVELES.get(valor.strip().lower(), "no_aplica") if isinstance(valor, str) else "no_aplica"


def _legible(clave: str) -> str:
    t = clave.replace("_", " ")
    return t[:1].upper() + t[1:]


def _matrices(doc: dict[str, Any]) -> list[Matriz]:
    salida: list[Matriz] = []
    for nombre, esquema in _esquemas(doc):
        ext = esquema.get("x-flit-obligatoriedad")
        if not isinstance(ext, dict) or not isinstance(ext.get("tipos"), list):
            continue
        tipos = [
            TipoTramite(
                clave=texto(t.get("clave")),
                nombre=texto(t.get("nombre") or t.get("columna_excel")) or _legible(texto(t.get("clave"))),
                valor=None if t.get("valor") is None else str(t.get("valor")),
            )
            for t in ext["tipos"]
            if isinstance(t, dict) and texto(t.get("clave"))
        ]
        if not tipos:
            continue
        clase = "documentos" if isinstance(ext.get("documentos"), list) else "campos"
        propiedades = esquema.get("properties") or {}
        filas: list[FilaMatriz] = []
        for f in ext.get(clase) or []:
            if not isinstance(f, dict):
                continue
            if clase == "documentos":
                campo = texto(f.get("valor")) or texto(f.get("nombre"))
                detalle = texto(f.get("nombre"))
            else:
                campo = texto(f.get("campo"))
                prop = propiedades.get(campo)
                detalle = texto(prop.get("description")) if isinstance(prop, dict) else ""
            if campo:
                valores = {t.clave: _nivel(f.get(t.clave)) for t in tipos}
                filas.append(FilaMatriz(campo, detalle, valores, texto(f.get("condicion"))))
        salida.append(Matriz(nombre, texto(ext.get("propiedad")), clase, tipos, filas))
    return salida


def _tablas(doc: dict[str, Any]) -> list[Tabla]:
    salida: list[Tabla] = []
    for nombre, esquema in _esquemas(doc):
        valores = esquema.get("enum")
        if not isinstance(valores, list) or not valores:
            continue
        ext = esquema.get("x-flit-tabla") if isinstance(esquema.get("x-flit-tabla"), dict) else {}
        columnas = [texto(c) for c in ext.get("columnas") or [] if texto(c)]
        filas_ext = [f for f in ext.get("filas") or [] if isinstance(f, dict)]
        if columnas and filas_ext:
            filas = [[texto(f.get(c)) for c in columnas] for f in filas_ext]
        else:
            descripciones = esquema.get("x-enum-descriptions")
            columnas = ["Código", "Descripción"]
            filas = []
            for i, v in enumerate(valores):
                if isinstance(descripciones, list):
                    d = texto(descripciones[i]) if i < len(descripciones) else ""
                elif isinstance(descripciones, dict):
                    d = texto(descripciones.get(str(v)))
                else:
                    d = ""
                filas.append([texto(v), d])
        titulo = texto(ext.get("titulo")) or nombre
        salida.append(Tabla(nombre, titulo, texto(esquema.get("description")), columnas, filas))
    return sorted(salida, key=lambda t: t.titulo.lower())


def construir(spec: SpecLeido) -> Contrato:
    return Constructor(spec).construir()
