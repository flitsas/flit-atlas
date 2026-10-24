"""Utilidades de JSON Schema para la conversión: 3.0 → 3.1, ejemplos ficticios, ajuste de ejemplos y poda."""

import copy
import re

from atlas_docs.ict.saneamiento import CORREO_FICTICIO

_REF = "#/components/schemas/"


class Esquemas:
    """Resuelve `$ref` locales contra `components.schemas` del spec."""

    def __init__(self, spec: dict) -> None:
        self.spec = spec

    @property
    def componentes(self) -> dict:
        return self.spec.setdefault("components", {}).setdefault("schemas", {})

    def resolver(self, esquema: dict | None, vistos: tuple[str, ...] = ()) -> tuple[dict, tuple[str, ...]]:
        """Sigue los `$ref`; las palabras clave hermanas del `$ref` (OpenAPI 3.1) se aplican sobre el destino."""
        hermanas: dict = {}
        while isinstance(esquema, dict) and isinstance(esquema.get("$ref"), str) and esquema["$ref"].startswith(_REF):
            nombre = esquema["$ref"][len(_REF) :]
            hermanas = {k: v for k, v in esquema.items() if k not in ("$ref", "description", "examples")} | hermanas
            if nombre in vistos:
                return {}, vistos
            vistos = (*vistos, nombre)
            esquema = self.componentes.get(nombre, {})
        esquema = esquema if isinstance(esquema, dict) else {}
        return ({**esquema, **hermanas} if hermanas else esquema), vistos

    def aplanar(self, esquema: dict | None, vistos: tuple[str, ...] = ()) -> tuple[dict, tuple[str, ...]]:
        """Resuelve `$ref` y une los `allOf` en un solo esquema (propiedades y obligatorios)."""
        esquema, vistos = self.resolver(esquema, vistos)
        if "allOf" not in esquema:
            return esquema, vistos
        unido: dict = {k: v for k, v in esquema.items() if k != "allOf"}
        for parte in esquema["allOf"]:
            plano, _ = self.aplanar(parte, vistos)
            # El ejemplo de una parte no sirve para el conjunto (le faltan las restricciones de las demás).
            unido = fusionar(unido, {k: v for k, v in plano.items() if k not in ("examples", "example")})
        return unido, vistos

    # --- ejemplos ficticios -------------------------------------------------------------------------------------
    def ficticio(self, esquema: dict | None, vistos: tuple[str, ...] = (), profundidad: int = 0) -> object:
        esquema, vistos = self.aplanar(esquema, vistos)
        if "const" in esquema:
            return esquema["const"]
        if esquema.get("enum"):
            return esquema["enum"][0]
        for clave in ("examples", "example"):
            if clave in esquema:
                valor = esquema[clave]
                return copy.deepcopy(valor[0] if clave == "examples" and isinstance(valor, list) and valor else valor)
        for clave in ("oneOf", "anyOf"):
            if esquema.get(clave):
                opcion = next((o for o in esquema[clave] if _tipo(o) != "null"), esquema[clave][0])
                return self.ficticio(opcion, vistos, profundidad)
        tipo = _tipo(esquema)
        if tipo == "object" or "properties" in esquema:
            if profundidad > 6:
                return {}
            objeto = {}
            for nombre, propiedad in esquema.get("properties", {}).items():
                objeto[nombre] = self.ficticio(propiedad, vistos, profundidad + 1)
            return objeto
        if tipo == "array":
            items = esquema.get("items", {})
            _, vistos_item = self.resolver(items, ())
            if any(v in vistos for v in vistos_item) or profundidad > 6:
                return []  # corta ciclos (entidad → lista de entidades) con una lista vacía
            return [self.ficticio(items, vistos, profundidad + 1)] * max(1, esquema.get("minItems", 1))
        if tipo == "string":
            return _texto_ficticio(esquema)
        if tipo in ("integer", "number"):
            minimo = esquema.get("minimum", 1 if esquema.get("maximum", 1) >= 1 else 0)
            valor = max(minimo, min(1, esquema.get("maximum", 1)))
            return int(valor) if tipo == "integer" else valor
        if tipo == "boolean":
            return True
        if tipo == "null":
            return None
        return "texto"

    # --- ajuste de ejemplos reales al esquema ------------------------------------------------------------------
    def ajustar(self, valor: object, esquema: dict | None, ruta: str, problemas: list[str], vistos=()) -> object:
        """Devuelve `valor` ajustado a `esquema`; cada cambio queda descrito (sin valores) en `problemas`."""
        esquema, vistos = self.aplanar(esquema, vistos)
        if not esquema:
            return valor
        for clave in ("oneOf", "anyOf"):
            if esquema.get(clave):
                mejores = []
                for opcion in esquema[clave]:
                    propios: list[str] = []
                    ajustado = self.ajustar(copy.deepcopy(valor), opcion, ruta, propios, vistos)
                    mejores.append((len(propios), ajustado, propios))
                    if not propios:
                        break
                cuantos, ajustado, propios = min(mejores, key=lambda x: x[0])
                problemas.extend(propios)
                return ajustado
        if "const" in esquema and valor != esquema["const"]:
            problemas.append(f"{ruta}: valor fuera del permitido")
            return esquema["const"]
        tipos = esquema.get("type")
        tipos = tipos if isinstance(tipos, list) else [tipos] if tipos else []
        if valor is None:
            if "null" in tipos or not tipos:
                return None
            problemas.append(f"{ruta}: nulo en un campo que no admite nulos (se usa un valor ficticio)")
            return self.ficticio(esquema, vistos)
        tipo = next((t for t in tipos if t != "null"), None)
        if tipo == "object" or (tipo is None and "properties" in esquema):
            return self._ajustar_objeto(valor, esquema, ruta, problemas, vistos)
        if tipo == "array":
            if not isinstance(valor, list):
                problemas.append(f"{ruta}: se esperaba una lista")
                valor = [valor]
            return [
                self.ajustar(v, esquema.get("items"), f"{ruta}[{i}]", problemas, vistos) for i, v in enumerate(valor)
            ]
        if tipo == "string":
            if not isinstance(valor, str):
                problemas.append(f"{ruta}: número o lógico donde se espera texto")
                valor = str(valor).lower() if isinstance(valor, bool) else str(valor)
            if "maxLength" in esquema and len(valor) > esquema["maxLength"]:
                problemas.append(f"{ruta}: más largo que maxLength {esquema['maxLength']}")
                valor = valor[: esquema["maxLength"]]
            if len(valor) < esquema.get("minLength", 0):
                problemas.append(f"{ruta}: más corto que minLength {esquema['minLength']}")
                valor = valor.ljust(esquema["minLength"], "0")
        elif tipo in ("integer", "number"):
            if isinstance(valor, bool) or not isinstance(valor, (int, float)):
                try:
                    valor = float(str(valor).strip())
                    problemas.append(f"{ruta}: texto donde se espera número")
                except ValueError:
                    problemas.append(f"{ruta}: no es un número (se usa un valor ficticio)")
                    return self.ficticio(esquema, vistos)
            if tipo == "integer" or float(valor).is_integer():
                if tipo == "integer" and not float(valor).is_integer():
                    problemas.append(f"{ruta}: decimal donde se espera entero")
                valor = int(valor)
            if "maximum" in esquema and valor > esquema["maximum"]:
                problemas.append(f"{ruta}: mayor que maximum")
                valor = self.ficticio(esquema, vistos)
            if "minimum" in esquema and valor < esquema["minimum"]:
                problemas.append(f"{ruta}: menor que minimum")
                valor = self.ficticio(esquema, vistos)
        elif tipo == "boolean" and not isinstance(valor, bool):
            problemas.append(f"{ruta}: texto donde se espera un lógico")
            valor = str(valor).strip().lower() == "true"
        if esquema.get("enum") and valor not in esquema["enum"]:
            problemas.append(f"{ruta}: valor que no está en la tabla de referencia")
            valor = esquema["enum"][0]
        return valor

    def _ajustar_objeto(self, valor, esquema, ruta, problemas, vistos):
        if not isinstance(valor, dict):
            problemas.append(f"{ruta}: se esperaba un objeto")
            return self.ficticio(esquema, vistos)
        propiedades = esquema.get("properties", {})
        cerrado = esquema.get("additionalProperties") is False
        resultado = {}
        por_norma = {norma(p): p for p in propiedades}
        for clave, interno in valor.items():
            if clave not in propiedades and norma(clave) in por_norma:
                # El Excel escribe la clave con otra capitalización («StatusValidation» y no «statusValidation»).
                problemas.append(f"{ruta}.{clave}: clave con otra capitalización (se usa la del contrato)")
                clave = por_norma[norma(clave)]
            if clave in propiedades:
                resultado[clave] = self.ajustar(interno, propiedades[clave], f"{ruta}.{clave}", problemas, vistos)
            elif cerrado:
                problemas.append(f"{ruta}.{clave}: propiedad que el esquema no admite (se quita)")
            else:
                resultado[clave] = interno
        for clave in esquema.get("required", []):
            if clave not in resultado and clave in propiedades:
                problemas.append(f"{ruta}.{clave}: obligatorio que faltaba (se agrega con un valor ficticio)")
                resultado[clave] = self.ficticio(propiedades[clave], vistos)
        return resultado


def fusionar(a: dict, b: dict) -> dict:
    """Une dos esquemas de un allOf: propiedades e items en profundidad, obligatorios sumados."""
    unido = dict(a)
    for clave, valor in b.items():
        previo = unido.get(clave)
        if clave == "required":
            unido[clave] = list(dict.fromkeys([*(previo or []), *valor]))
        elif clave == "properties" and isinstance(previo, dict):
            propiedades = dict(previo)
            for nombre, propiedad in valor.items():
                anterior = propiedades.get(nombre)
                propiedades[nombre] = fusionar(anterior, propiedad) if isinstance(anterior, dict) else propiedad
            unido[clave] = propiedades
        elif clave == "items" and isinstance(previo, dict) and isinstance(valor, dict):
            unido[clave] = fusionar(previo, valor)
        else:
            unido[clave] = valor if clave not in unido or clave in ("const", "enum", "minItems") else previo
    return unido


def _tipo(esquema: dict) -> str | None:
    tipo = esquema.get("type") if isinstance(esquema, dict) else None
    if isinstance(tipo, list):
        return next((t for t in tipo if t != "null"), "null")
    return tipo


def _texto_ficticio(esquema: dict) -> str:
    formato = esquema.get("format")
    if formato == "date-time":
        texto = "2025-01-31T15:00:00Z"
    elif formato == "date":
        texto = "2025-01-31"
    elif formato == "email":
        texto = CORREO_FICTICIO
    elif formato in ("uri", "url"):
        texto = "https://cliente.ejemplo.com/notificaciones"
    elif esquema.get("contentMediaType") or esquema.get("format") == "binary":
        texto = "documento.pdf"
    else:
        texto = "texto"
    texto = texto.ljust(esquema.get("minLength", 0), "0")
    return texto[: esquema["maxLength"]] if "maxLength" in esquema else texto


def inferir(valor: object) -> dict:
    """Esquema mínimo a partir de un ejemplo (para las respuestas que tsoa deja vacías)."""
    if isinstance(valor, dict):
        return {"type": "object", "properties": {k: inferir(v) for k, v in valor.items()}}
    if isinstance(valor, list):
        items: dict = {}
        for elemento in valor:
            items = unir(items, inferir(elemento))
        return {"type": "array", "items": items}
    if isinstance(valor, bool):
        return {"type": "boolean"}
    if isinstance(valor, int):
        return {"type": "integer"}
    if isinstance(valor, float):
        return {"type": "number"}
    if valor is None:
        return {}
    return {"type": "string"}


def unir(a: dict, b: dict) -> dict:
    if not a:
        return b
    if not b:
        return a
    if a.get("type") == b.get("type") == "object":
        propiedades = dict(a.get("properties", {}))
        for clave, valor in b.get("properties", {}).items():
            propiedades[clave] = unir(propiedades.get(clave, {}), valor)
        return {**a, "properties": propiedades}
    return a


def a_openapi_31(nodo: object) -> object:
    """`nullable` y `example` de OpenAPI 3.0 a su forma de 3.1 (tipos con "null" y `examples`)."""
    if isinstance(nodo, list):
        return [a_openapi_31(v) for v in nodo]
    if not isinstance(nodo, dict):
        return nodo
    nodo = {k: a_openapi_31(v) for k, v in nodo.items()}
    if nodo.pop("nullable", False):
        tipo = nodo.get("type")
        if isinstance(tipo, str):
            nodo["type"] = [tipo, "null"]
        elif "type" not in nodo:
            nodo = {"anyOf": [{k: v for k, v in nodo.items() if k not in ("description",)}, {"type": "null"}]} | (
                {"description": nodo["description"]} if "description" in nodo else {}
            )
    return nodo


def referencias(nodo: object) -> set[str]:
    encontradas: set[str] = set()
    if isinstance(nodo, dict):
        for clave, valor in nodo.items():
            if clave == "$ref" and isinstance(valor, str) and valor.startswith(_REF):
                encontradas.add(valor[len(_REF) :])
            else:
                encontradas |= referencias(valor)
    elif isinstance(nodo, list):
        for valor in nodo:
            encontradas |= referencias(valor)
    return encontradas


def podar_componentes(spec: dict) -> list[str]:
    """Quita los esquemas que nadie referencia. Devuelve sus nombres."""
    componentes = spec.get("components", {}).get("schemas", {})
    raiz = {k: v for k, v in spec.items() if k != "components"}
    vivos = referencias(raiz)
    pendientes = list(vivos)
    while pendientes:
        nombre = pendientes.pop()
        for ref in referencias(componentes.get(nombre, {})):
            if ref not in vivos:
                vivos.add(ref)
                pendientes.append(ref)
    quitados = sorted(set(componentes) - vivos)
    for nombre in quitados:
        del componentes[nombre]
    return quitados


def romper_ciclos(spec: dict) -> list[str]:
    """Quita de `required` las referencias que cierran un ciclo de objetos obligatorios (entidad ↔ entidad).

    Un ciclo así no tiene instancia finita: ningún ejemplo lo cumple y vacuum lo marca como referencia circular
    infinita. Devuelve «Esquema.propiedad» de cada obligatorio quitado.
    """
    componentes = spec.get("components", {}).get("schemas", {})

    def aristas(nombre: str) -> list[tuple[str, str]]:
        esquema = componentes.get(nombre, {})
        salida = []
        for propiedad in esquema.get("required", []):
            nodo = esquema.get("properties", {}).get(propiedad, {})
            # Una lista obligatoria también cierra el ciclo para libopenapi (vacuum).
            for candidato in [nodo, *nodo.get("allOf", []), nodo.get("items", {})]:
                ref = candidato.get("$ref", "") if isinstance(candidato, dict) else ""
                if ref.startswith(_REF):
                    salida.append((propiedad, ref[len(_REF) :]))
        return salida

    quitados: list[str] = []
    cambio = True
    while cambio:
        cambio = False
        for inicio in sorted(componentes):
            pila = [(inicio, [inicio])]
            while pila and not cambio:
                actual, camino = pila.pop()
                for propiedad, destino in aristas(actual):
                    if destino == inicio:
                        componentes[actual]["required"].remove(propiedad)
                        if not componentes[actual]["required"]:
                            del componentes[actual]["required"]
                        quitados.append(f"{actual}.{propiedad}")
                        cambio = True
                        break
                    if destino not in camino:
                        pila.append((destino, [*camino, destino]))
            if cambio:
                break
    return quitados


def norma(nombre: str) -> str:
    return re.sub(r"[^a-z0-9]", "", nombre.lower())
