"""Swagger de tsoa + Excel de contratos ICT → OpenAPI 3.1 enriquecido (prompt maestro §8.4, ATLAS-ADR-0037).

La base es el swagger que genera tsoa en cada `BackApiExternal*`. El Excel aporta descripciones, longitudes, ejemplos,
códigos de respuesta, tablas de referencia, la matriz de obligatoriedad por tipo de trámite, los webhooks y el
historial. Todo lo que no se puede llevar al spec, o que contradice al código, queda en el `Registro` para el informe.
"""

import copy
import json
import re
from collections import Counter, defaultdict
from dataclasses import dataclass, field

from atlas_docs.legado import mapeo
from atlas_docs.legado.ejemplos import Ejemplo, host, interpretar
from atlas_docs.legado.esquemas import Esquemas, a_openapi_31, inferir, norma, podar_componentes, romper_ciclos
from atlas_docs.legado.excel import Campo, Hoja, Libro, Tabla
from atlas_docs.legado.saneamiento import buscar_sensibles, sanear_arbol, sanear_ejemplo
from atlas_docs.legado.tipos import esquema_desde_tipo, familia

COLABORADOR_GENERICO = "Equipo de integraciones de FLIT"
METODOS = ("get", "put", "post", "delete", "patch", "options", "head")
_DESCRIPCIONES_GENERICAS = {"", "ok", "bad request", "not found", "forbidden", "conflict", "internal server error"}

TIPOS_INCONSISTENCIA: dict[str, str] = {
    "hosts_mezclados": "Hosts mezclados",
    "url_vacia": "URL de dev o pdn vacías",
    "hoja_faltante": "Servicios del índice sin hoja",
    "salida_incompleta": "Salidas incompletas",
    "operacion_sin_ruta": "Hojas sin operación en el código",
    "ruta_distinta": "Rutas del Excel que no coinciden con el swagger",
    "nombre_con_error": "Nombres con errores",
    "campo_fuera_del_spec": "Campos del Excel que no quedaron en el spec",
    "esquema_del_codigo": "Esquemas de tsoa que no describen la API real",
    "tipo_distinto": "Tipos distintos entre el Excel y el código",
    "obligatoriedad_distinta": "Obligatoriedad distinta entre el Excel y el código",
    "ejemplo_tabla": "Ejemplo y tabla de campos no coinciden",
    "ejemplo_ajustado": "Ejemplos reparados o ajustados al esquema",
    "seguridad": "Seguridad declarada distinta",
    "tabla_referencia": "Tablas de referencia con datos dudosos u omitidas",
    "codigo_respuesta": "Códigos de respuesta",
    "campo_solo_en_codigo": "Campos que exige el código y el Excel no documenta",
    "operation_id": "operationId duplicados en el swagger",
    "metadatos": "Metadatos de tsoa y del Excel",
    "celda_sin_interpretar": "Celdas del Excel sin interpretar",
}


@dataclass
class Inconsistencia:
    tipo: str
    detalle: str
    api: str | None = None
    hoja: str | None = None


@dataclass
class Registro:
    inconsistencias: list[Inconsistencia] = field(default_factory=list)
    traza: list[dict] = field(default_factory=list)
    saneamiento: Counter = field(default_factory=Counter)
    detectado_excel: Counter = field(default_factory=Counter)  # lo sensible que trae el Excel, por tipo
    changelog: dict[str, list[dict]] = field(default_factory=dict)
    resumen: list[dict] = field(default_factory=list)

    def agregar(self, tipo: str, detalle: str, api: str | None = None, hoja: str | None = None) -> None:
        if tipo not in TIPOS_INCONSISTENCIA:
            raise ValueError(tipo)
        nueva = Inconsistencia(tipo, detalle, api, hoja)
        if nueva not in self.inconsistencias:
            self.inconsistencias.append(nueva)

    def trazar(self, campo: Campo, destino: str, detalle: str) -> None:
        """destino: «spec» (detalle = archivo#puntero) o «informe» (detalle = motivo)."""
        self.traza.append(
            {
                "hoja": campo.hoja,
                "celda": campo.celda,
                "seccion": campo.seccion,
                "campo": campo.nombre,
                "destino": destino,
                "detalle": detalle,
            }
        )


@dataclass
class Resultado:
    specs: dict[str, dict]
    registro: Registro


def _escapar(segmento: str) -> str:
    return segmento.replace("~", "~0").replace("/", "~1")


def version_semantica(texto: str | None) -> str | None:
    if not texto:
        return None
    partes = re.findall(r"\d+", str(texto))
    if not partes:
        return None
    partes = (partes + ["0", "0"])[:3]
    return ".".join(str(int(p)) for p in partes)


def _documentos(ejemplo: Ejemplo | None) -> list:
    if ejemplo is None or ejemplo.tipo != "json":
        return []
    documentos = []
    for valor in ejemplo.valores:
        documentos.append(valor)
    return documentos


class _Convertidor:
    def __init__(self, api: mapeo.Api, swagger: dict, libro: Libro | None, registro: Registro) -> None:
        self.api = api
        self.libro = libro
        self.registro = registro
        self.archivo = f"{api.codigo}.yaml"
        spec = a_openapi_31(copy.deepcopy(swagger))
        assert isinstance(spec, dict)  # noqa: S101 - el swagger de entrada es un objeto
        self.spec = spec
        self.esq = Esquemas(self.spec)
        self.destinos = [d for d in mapeo.DESTINOS if d.repo == api.repo]
        self.codigos = self._codigos_excel()

    # --- utilidades ---------------------------------------------------------------------------------------------
    def agregar(self, tipo: str, detalle: str, hoja: Hoja | None = None) -> None:
        self.registro.agregar(tipo, detalle, self.api.codigo, hoja.nombre if hoja else None)

    def hoja(self, destino: mapeo.Destino) -> Hoja | None:
        return self.libro.hoja(destino.hoja) if self.libro else None

    def _codigos_excel(self) -> dict[int, str]:
        codigos: dict[int, str] = {}
        for hoja in self.libro.hojas if self.libro else []:
            for codigo in hoja.codigos:
                codigos.setdefault(codigo.codigo, codigo.descripcion or codigo.texto)
        return codigos

    def operaciones(self):
        for ruta, metodos in self.spec.get("paths", {}).items():
            for metodo, op in metodos.items():
                if metodo in METODOS:
                    yield ruta, metodo, op
        for nombre, metodos in self.spec.get("webhooks", {}).items():
            for metodo, op in metodos.items():
                if metodo in METODOS:
                    yield f"webhook:{nombre}", metodo, op

    # --- pasos --------------------------------------------------------------------------------------------------
    def convertir(self) -> dict:
        self.spec["openapi"] = "3.1.0"
        self._limpiar_tsoa()
        hojas = [(d, self.hoja(d)) for d in self.destinos]
        for _, hoja in hojas:
            if hoja is not None:
                self._tablas(hoja)
        for destino, hoja in hojas:
            if destino.webhook:
                self._webhook(destino, hoja)
            elif destino.ruta:
                self._operacion(destino, hoja)
        self._visibilidad()
        self._respuestas()
        self._info(hojas)
        self._servidores(hojas)
        if ciclos := romper_ciclos(self.spec):
            self.agregar(
                "metadatos",
                "tsoa marca como obligatorias referencias que forman un ciclo sin fin (entidad que se contiene a sí "
                "misma); se dejan opcionales: " + ", ".join(f"`{c}`" for c in ciclos),
            )
        self._descripciones_finales()
        quitados = podar_componentes(self.spec)
        if quitados:
            self.agregar(
                "metadatos",
                f"{len(quitados)} esquemas de tsoa quedaron sin uso tras la conversión y se quitaron: "
                + ", ".join(f"`{q}`" for q in quitados),
            )
        self._ejemplos()
        self._etiquetas()
        cuenta: Counter = Counter()
        self.spec = sanear_arbol(self.spec, cuenta)  # type: ignore[assignment]
        self.registro.saneamiento.update(cuenta)
        texto = json.dumps(self.spec, ensure_ascii=False)
        if hallazgos := buscar_sensibles(texto):
            raise ValueError(f"{self.archivo}: quedan datos sensibles: {sorted({h.tipo for h in hallazgos})}")
        return self._ordenar()

    def _limpiar_tsoa(self) -> None:
        componentes = self.spec.setdefault("components", {})
        for clave in ("requestBodies", "examples", "headers", "parameters", "responses"):
            if componentes.get(clave) == {}:
                del componentes[clave]
        cabeceras_espurias = 0
        vistos: Counter = Counter(op.get("operationId") for _, _, op in self.operaciones())
        for ruta, metodo, op in self.operaciones():
            for respuesta in op.get("responses", {}).values():
                cabeceras = respuesta.get("headers", {})
                for nombre in [n for n, c in cabeceras.items() if n in componentes.get("schemas", {})]:
                    # tsoa convierte el genérico de @SuccessResponse<T> en una cabecera llamada como el tipo.
                    del cabeceras[nombre]
                    cabeceras_espurias += 1
                if "headers" in respuesta and not respuesta["headers"]:
                    del respuesta["headers"]
            for texto in ("description", "summary"):
                if isinstance(op.get(texto), str):
                    op[texto] = op[texto].replace("\r\n", "\n").strip()
            if vistos[op.get("operationId")] > 1:
                segmento = next((s for s in ruta.split("/") if s and s not in ("api", "v1") and "{" not in s), "op")
                prefijo = re.sub(r"[-_](\w)", lambda m: m.group(1).upper(), segmento)
                nuevo = f"{prefijo}{op['operationId']}"
                self.agregar(
                    "operation_id",
                    f"`{op['operationId']}` se repite; `{metodo.upper()} {ruta}` pasa a `{nuevo}`",
                )
                op["operationId"] = nuevo
            if not op.get("description"):
                op["description"] = op.get("summary") or (
                    f"Operación `{op.get('operationId')}` de `{self.api.repo}` (sin descripción en el código)."
                )
            for parametro in op.get("parameters", []):
                parametro.setdefault("description", f"Parámetro `{parametro.get('name')}` ({parametro.get('in')}).")
        if cabeceras_espurias:
            self.agregar(
                "metadatos",
                f"{cabeceras_espurias} cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como "
                "cabecera) se quitaron de las respuestas",
            )
        info = self.spec.get("info", {})
        if "boilerplate" in str(info.get("title", "")).lower() or info.get("version") in (None, "1.0.0"):
            self.agregar(
                "metadatos",
                f"tsoa publica el título genérico «{info.get('title')}», la versión {info.get('version')} y un "
                "contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json`",
            )

    # --- tablas de referencia -----------------------------------------------------------------------------------
    def _tablas(self, hoja: Hoja) -> None:
        componentes = self.esq.componentes
        for tabla in hoja.tablas:
            clave = tabla.titulo.strip().lower()
            if clave in mapeo.TABLAS_OMITIDAS:
                self.agregar(
                    "tabla_referencia",
                    f"«{tabla.titulo}» ({tabla.celda}) no pasa al spec: {mapeo.TABLAS_OMITIDAS[clave]}",
                    hoja,
                )
                continue
            conf = mapeo.TABLAS.get(clave)
            if conf is None:
                self.agregar("tabla_referencia", f"«{tabla.titulo}» ({tabla.celda}) no tiene componente asignado", hoja)
                continue
            componentes[conf["componente"]] = self._componente_tabla(tabla, conf, hoja)
            if conf["componente"] == "DocumentoAdjunto":
                componentes["DocumentoAdjunto"]["x-flit-obligatoriedad"] = self._guia_adjuntos(tabla, hoja)

    def _guia_adjuntos(self, tabla: Tabla, hoja: Hoja) -> dict:
        """Matriz de la guía de documentos adjuntos: documento × tipo de trámite → obligatorio | opcional."""
        leyenda = {k: v.lower() for k, v in hoja.leyenda.items()}
        if not leyenda:
            self.agregar("tabla_referencia", f"«{tabla.titulo}» sin leyenda de los valores 1 y 2", hoja)
        columnas = {
            i: mapeo.COLUMNAS_GUIA_ADJUNTOS[c]
            for i, c in enumerate(tabla.columnas)
            if c in mapeo.COLUMNAS_GUIA_ADJUNTOS
        }
        documentos = []
        for fila in tabla.filas:
            if fila[0] is None or not fila[0].isdigit():
                continue
            documento: dict = {"valor": int(fila[0]), "nombre": fila[1] or "Sin nombre en el Excel (por confirmar)"}
            for i, clave in columnas.items():
                documento[clave] = leyenda.get(fila[i] or "", "no_aplica") if fila[i] else "no_aplica"
            documentos.append(documento)
        return {
            "propiedad": "transaction_type del trámite al que se adjunta",
            "tipos": [{"clave": v, "columna_excel": k} for k, v in mapeo.COLUMNAS_GUIA_ADJUNTOS.items()],
            "documentos": documentos,
        }

    def _componente_tabla(self, tabla: Tabla, conf: dict, hoja: Hoja) -> dict:
        indice_valor = next((i for i, c in enumerate(tabla.columnas) if c.lower() == conf["valor"].lower()), 0)
        indice_nombre = next((i for i, c in enumerate(tabla.columnas) if c.upper() == "NOMBRE"), None)
        valores, descripciones, filas = [], [], []
        anteriores: list[int] = []
        for posicion, fila in enumerate(tabla.filas, start=1):
            crudo = fila[indice_valor]
            nombre = fila[indice_nombre] if indice_nombre is not None else None
            if conf["tipo"] == "integer":
                if crudo is not None and re.fullmatch(r"\d+", crudo):
                    valor: object = int(crudo)
                elif anteriores == list(range(1, posicion)) or posicion == 1:
                    valor = posicion
                    self.agregar(
                        "tabla_referencia",
                        f"«{tabla.titulo}»: la fila {posicion} trae en el ID un valor que no es número (un enlace "
                        f"interno); se toma {posicion} por la secuencia de la tabla. Confirmar",
                        hoja,
                    )
                else:
                    self.agregar(
                        "tabla_referencia", f"«{tabla.titulo}»: fila {posicion} sin ID numérico; se omite", hoja
                    )
                    continue
                anteriores.append(int(valor))  # type: ignore[arg-type]
            else:
                if crudo is None:
                    continue
                valor = crudo
            if not nombre:
                self.agregar(
                    "tabla_referencia",
                    f"«{tabla.titulo}»: el valor {valor} no tiene nombre en el Excel (se deja como «por confirmar»)",
                    hoja,
                )
            valores.append(valor)
            descripciones.append(nombre or "Sin nombre en el Excel (por confirmar)")
            fila_limpia = {c: v for c, v in zip(tabla.columnas, fila, strict=False) if v is not None}
            fila_limpia[tabla.columnas[indice_valor]] = valor  # el valor que viaja, no lo que traía la celda
            filas.append(fila_limpia)
        return {
            "type": conf["tipo"],
            "description": f"Valores de la «{tabla.titulo}» del Excel de contratos ICT (hoja «{hoja.nombre}»).",
            "enum": valores,
            "x-enum-descriptions": descripciones,
            "x-flit-tabla": {"hoja": hoja.nombre, "titulo": tabla.titulo, "columnas": tabla.columnas, "filas": filas},
        }

    def _componente_para_campo(self, campo: Campo) -> str | None:
        descripcion = norma(campo.descripcion or "")
        ultimo = campo.ruta[-1] if campo.ruta else ""
        # La más larga primero: «Tabla Tipo Operacion Garantia» antes que «Tabla Tipo Operacion».
        for clave, conf in sorted(mapeo.TABLAS.items(), key=lambda x: -len(x[0])):
            if conf["componente"] not in self.esq.componentes:
                continue
            if norma(clave) in descripcion or ultimo in conf.get("campos", []):
                return conf["componente"]
        return None

    # --- operaciones del Excel ----------------------------------------------------------------------------------
    def _operacion(self, destino: mapeo.Destino, hoja: Hoja | None) -> None:
        op = self.spec.get("paths", {}).get(destino.ruta, {}).get(destino.metodo)
        if op is None:
            self.agregar("ruta_distinta", f"`{destino.metodo} {destino.ruta}` del mapeo no existe en el swagger", hoja)
            for campo in hoja.campos if hoja else []:
                self.registro.trazar(campo, "informe", "la operación no existe en el swagger")
            return
        if hoja is None:
            return
        if destino.nota_ruta:
            self.agregar("ruta_distinta", destino.nota_ruta, hoja)
        op["x-flit-hoja-excel"] = hoja.nombre
        if destino.pendiente:
            op["x-flit-pendiente"] = destino.pendiente
        else:
            op["x-public"] = True
        descripcion_indice = self._descripcion_indice(hoja)
        if descripcion_indice:
            op["summary"] = descripcion_indice
            op["description"] = f"{descripcion_indice}.\n\n{op.get('description', '')}".strip()
        if hoja.metodo and hoja.metodo != destino.metodo:
            self.agregar(
                "ruta_distinta",
                f"el Excel dice {hoja.metodo.upper()} y el swagger {destino.metodo.upper()} en `{destino.ruta}`",
                hoja,
            )
        self._seguridad(op, hoja)
        self._campos_y_ejemplos(op, destino, hoja, reportar=True)

    def _campos_y_ejemplos(self, op: dict, destino: mapeo.Destino, hoja: Hoja, reportar: bool) -> None:
        entrada = interpretar(hoja.ejemplo_entrada)
        salidas = [interpretar(t) for t in hoja.ejemplos_salida]
        self._revisar_ejemplos(hoja, [entrada, *salidas])
        peticion = self._cuerpo_peticion(op, destino, hoja, entrada, reportar)
        respuesta = self._cuerpo_respuesta(op, destino, hoja, salidas, reportar)
        lugares = Counter(self._ubicar_campo(campo, op, destino, hoja, peticion, respuesta) for campo in hoja.campos)
        if peticion and peticion.get("construido") and any(c.por_tipo for c in hoja.campos):
            self._obligatoriedad(hoja, peticion)
        if peticion and not peticion["construido"]:
            self._solo_en_codigo(op, hoja, peticion, destino)
        # Hojas que listan el cuerpo de la petición bajo «Salida»: su ejemplo es de la petición.
        salida_es_peticion = (
            bool(lugares) and lugares.get("respuesta", 0) == 0 and lugares.get("salida-en-peticion", 0) > 0
        )
        self._ejemplos_operacion(op, hoja, destino, entrada, salidas, peticion, respuesta, salida_es_peticion)

    def _solo_en_codigo(self, op: dict, hoja: Hoja, peticion: dict, destino: mapeo.Destino) -> None:
        esquema, _ = self.esq.aplanar(peticion["contenido"].get("schema", {}))
        if esquema.get("type") == "array":
            esquema, _ = self.esq.aplanar(esquema.get("items", {}))
        documentados = {norma(destino.alias.get(c.nombre, c.ruta[0])) for c in hoja.campos if c.ruta}
        faltan = [r for r in esquema.get("required", []) if norma(r) not in documentados]
        if faltan:
            self.agregar(
                "campo_solo_en_codigo",
                f"`{op['operationId']}` exige " + ", ".join(f"`{f}`" for f in faltan) + " y el Excel no lo documenta",
                hoja,
            )

    def _descripcion_indice(self, hoja: Hoja) -> str | None:
        if not self.libro or not self.libro.indice:
            return None
        return next(
            (d for n, d in self.libro.indice.servicios if norma(n) == norma(hoja.servicio) and d),
            None,
        )

    def _seguridad(self, op: dict, hoja: Hoja) -> None:
        pide_token = "authorization" in (hoja.cabeceras or "").lower()
        parametros = op.get("parameters", [])
        cabecera = [p for p in parametros if p.get("in") == "header" and p.get("name", "").lower() == "authorization"]
        if cabecera:
            op["parameters"] = [p for p in parametros if p not in cabecera]
            op["security"] = [{"bearer": []}]
            self._esquema_bearer()
            self.agregar(
                "seguridad",
                f"`{op['operationId']}` lee la cabecera `authorization` a mano (parámetro de tsoa) en lugar de "
                "declarar seguridad; el spec la declara como esquema `bearer`",
                hoja,
            )
        elif pide_token and not op.get("security"):
            op["security"] = [{"bearer": []}]
            self._esquema_bearer()
            self.agregar(
                "seguridad",
                f"`{op['operationId']}`: el Excel pide `Authorization: Bearer` y el swagger no declara seguridad; "
                "el spec documenta el token. Confirmar si el servicio lo exige",
                hoja,
            )
        elif not pide_token and not op.get("security"):
            op["x-flit-sin-autenticacion"] = True

    def _descripciones_finales(self) -> None:
        for _, _, op in self.operaciones():
            cuerpo = op.get("requestBody")
            if cuerpo and not cuerpo.get("description"):
                hoja = op.get("x-flit-hoja-excel")
                cuerpo["description"] = (
                    f"Cuerpo de la petición según la hoja «{hoja}» del Excel." if hoja else "Cuerpo de la petición."
                )
        for esquema in self.spec.get("components", {}).get("securitySchemes", {}).values():
            if esquema.get("scheme") == "bearer":
                esquema["description"] = (
                    "Token JWT que entrega `POST /api/v1/auth/login` de `external-auth`; viaja en la cabecera "
                    "`Authorization: Bearer $TOKEN`. [por verificar] cumplimiento de RFC8725 en el emisor."
                )
        for nombre, esquema in self.spec.get("components", {}).get("schemas", {}).items():
            if isinstance(esquema, dict) and not esquema.get("description"):
                esquema["description"] = f"Esquema `{nombre}` del código (tsoa)."

    def _esquema_bearer(self) -> None:
        esquemas = self.spec.setdefault("components", {}).setdefault("securitySchemes", {})
        esquemas.setdefault("bearer", {"type": "http", "scheme": "bearer", "bearerFormat": "JWT"})

    def _media(self, contenedor: dict, preferida: str | None) -> tuple[str, dict]:
        contenido = contenedor.setdefault("content", {})
        if preferida:
            anterior = next(iter(contenido.values()), {})
            contenido.clear()
            contenido[preferida] = {"schema": anterior.get("schema", {})} if anterior else {"schema": {}}
        if not contenido:
            contenido["application/json"] = {"schema": {}}
        nombre = next(iter(contenido))
        return nombre, contenido[nombre]

    def _cuerpo_peticion(self, op, destino, hoja, entrada, reportar: bool) -> dict | None:
        campos = [
            c
            for c in hoja.campos
            if c.seccion == "parametros" and c.nombre.upper() != "N/A" and not self._es_parametro(op, c)
        ]
        if "requestBody" not in op and (not campos or destino.metodo == "get"):
            return None
        cuerpo = op.setdefault("requestBody", {"required": True})
        media, contenido = self._media(cuerpo, destino.media_peticion)
        esquema = contenido.get("schema") or {}
        construir = bool(destino.cuerpo_desde_excel) or not esquema
        if not construir:
            return {"media": media, "contenido": contenido, "construido": False}
        if reportar:
            motivo = destino.cuerpo_desde_excel or "tsoa deja vacío el esquema de la petición"
            self.agregar("esquema_del_codigo", f"`{op['operationId']}`: {motivo}", hoja)
        nombre = destino.componente_peticion or f"{op['operationId']}Peticion"
        documentos = _documentos(entrada)
        if entrada and entrada.tipo == "curl":
            documentos = [_formulario_tipado(entrada.valores[0]["formulario"])]
        muestra = documentos[0] if documentos else {}
        if isinstance(muestra, list):
            muestra = muestra[0] if muestra else {}
        self.esq.componentes[nombre] = {**inferir(muestra if isinstance(muestra, dict) else {}), "type": "object"}
        self.esq.componentes[nombre].setdefault("properties", {})
        referencia = {"$ref": f"#/components/schemas/{nombre}"}
        contenido["schema"] = {"type": "array", "items": referencia} if hoja.listado else referencia
        return {"media": media, "contenido": contenido, "construido": True, "componente": nombre}

    def _cuerpo_respuesta(self, op, destino, hoja, salidas, reportar: bool) -> dict | None:
        campos = [c for c in hoja.campos if c.seccion == "salida"]
        respuesta = op.setdefault("responses", {}).setdefault("200", {"description": "OK"})
        media, contenido = self._media(respuesta, None)
        esquema = contenido.get("schema") or {}
        if esquema or not campos:
            return {"media": media, "contenido": contenido, "construido": False}
        exitos = [d for s in salidas for d in _documentos(s) if not _es_error(d)]
        nombre = destino.componente_respuesta or f"{op['operationId']}Respuesta"
        if nombre not in self.esq.componentes:
            muestra = exitos[0] if exitos and isinstance(exitos[0], dict) else {}
            self.esq.componentes[nombre] = {**inferir(muestra), "type": "object"}
            self.esq.componentes[nombre].setdefault("properties", {})
        contenido["schema"] = {"$ref": f"#/components/schemas/{nombre}"}
        if reportar:
            self.agregar(
                "esquema_del_codigo", f"`{op['operationId']}`: tsoa deja vacío el esquema de la respuesta 200", hoja
            )
        return {"media": media, "contenido": contenido, "construido": True, "componente": nombre}

    def _es_parametro(self, op: dict, campo: Campo) -> dict | None:
        return next(
            (
                p
                for p in op.get("parameters", [])
                if p.get("in") in ("path", "query") and norma(p["name"]) == norma(campo.nombre)
            ),
            None,
        )

    def _ubicar_campo(self, campo, op, destino, hoja, peticion, respuesta) -> str | None:
        """Lleva el campo al spec. Devuelve «parametro», «peticion», «respuesta», «salida-en-peticion» o None."""
        if campo.nombre.upper() == "N/A":
            self.registro.trazar(campo, "informe", "la hoja indica que la operación no tiene parámetros")
            return None
        if campo.seccion == "parametros" and (parametro := self._es_parametro(op, campo)):
            if campo.descripcion:
                parametro["description"] = campo.descripcion
            tipo = esquema_desde_tipo(campo.tipo)
            actual = self.esq.resolver(parametro.get("schema"))[0]
            if tipo and actual.get("type") and familia(tipo.get("type")) != familia(actual.get("type")):
                self.agregar(
                    "tipo_distinto",
                    f"parámetro `{parametro['name']}` de `{op['operationId']}`: Excel «{campo.tipo}», código "
                    f"`{actual.get('type')}` (se conserva el del código)",
                    hoja,
                )
            indice = op["parameters"].index(parametro)
            self.registro.trazar(campo, "spec", f"{self.archivo}#{self._puntero_op(op)}/parameters/{indice}")
            return "parametro"
        orden = [("peticion", peticion), ("respuesta", respuesta)]
        if campo.seccion == "salida":
            orden.reverse()
        for posicion, (lugar, cuerpo) in enumerate(orden):
            if not cuerpo:
                continue
            construido = cuerpo["construido"] and posicion == 0
            ubicado = self._ubicar(cuerpo, campo, destino, crear=construido)
            if not ubicado:
                continue
            padre, nombre, puntero = ubicado
            self._aplicar(campo, padre, nombre, construido, destino, hoja)
            self.registro.trazar(campo, "spec", f"{self.archivo}#{puntero}")
            if posicion == 1 and campo.seccion == "salida":
                self.agregar(
                    "salida_incompleta",
                    f"`{campo.nombre}` está bajo «Salida» y es un campo de la petición; la respuesta real "
                    f"(`{op['operationId']}`) no está documentada en el Excel",
                    hoja,
                )
                return "salida-en-peticion"
            if posicion == 1:
                self.agregar(
                    "ejemplo_tabla",
                    f"`{campo.nombre}` está bajo «Parametros» y es un campo de la respuesta en el código",
                    hoja,
                )
            return lugar
        self.registro.trazar(campo, "informe", "el esquema del código no tiene esa propiedad")
        self.agregar(
            "campo_fuera_del_spec",
            f"`{campo.nombre}` ({campo.celda}) no está en el esquema de `{op['operationId']}`",
            hoja,
        )
        return None

    def _puntero_op(self, op: dict) -> str:
        for ruta, metodos in self.spec.get("paths", {}).items():
            for metodo, candidato in metodos.items():
                if candidato is op:
                    return f"/paths/{_escapar(ruta)}/{metodo}"
        for nombre, metodos in self.spec.get("webhooks", {}).items():
            for metodo, candidato in metodos.items():
                if candidato is op:
                    return f"/webhooks/{_escapar(nombre)}/{metodo}"
        return ""

    def _bajar(self, nodo: dict, puntero: str) -> tuple[dict, str]:
        while True:
            if isinstance(nodo.get("$ref"), str):
                nombre = nodo["$ref"].split("/")[-1]
                nodo, puntero = self.esq.componentes.setdefault(nombre, {}), f"/components/schemas/{_escapar(nombre)}"
            elif nodo.get("type") == "array" or "items" in nodo:
                nodo, puntero = nodo.setdefault("items", {}), f"{puntero}/items"
            elif nodo.get("anyOf"):
                indice = next((i for i, o in enumerate(nodo["anyOf"]) if o.get("type") != "null"), 0)
                nodo, puntero = nodo["anyOf"][indice], f"{puntero}/anyOf/{indice}"
            else:
                return nodo, puntero

    def _ubicar(self, cuerpo: dict, campo: Campo, destino: mapeo.Destino, crear: bool):
        nodo, puntero = cuerpo["contenido"].setdefault("schema", {}), ""
        puntero = self._puntero_contenido(cuerpo)
        segmentos = [destino.alias.get(campo.nombre, s) if len(campo.ruta) == 1 else s for s in campo.ruta]
        padre, nombre = None, None
        for i, segmento in enumerate(segmentos):
            nodo, puntero = self._bajar(nodo, puntero)
            propiedades = nodo.get("properties")
            if propiedades is None:
                if not crear:
                    return None
                nodo["type"] = "object"
                propiedades = nodo["properties"] = {}
            encontrado = next((p for p in propiedades if norma(p) == norma(segmento)), None)
            if encontrado is None and i == 0 and len(propiedades) == 1:
                # Respuesta envuelta (p. ej. {"data": [...]}) que la tabla del Excel no menciona.
                envoltura = next(iter(propiedades))
                interno, puntero_interno = self._bajar(
                    propiedades[envoltura], f"{puntero}/properties/{_escapar(envoltura)}"
                )
                if any(norma(p) == norma(segmento) for p in interno.get("properties", {})):
                    nodo, puntero, propiedades = interno, puntero_interno, interno["properties"]
                    encontrado = next(p for p in propiedades if norma(p) == norma(segmento))
            if encontrado is None:
                if not crear:
                    return None
                encontrado = segmento
                propiedades[encontrado] = {}
            padre, nombre = nodo, encontrado
            nodo, puntero = propiedades[encontrado], f"{puntero}/properties/{_escapar(encontrado)}"
        return (padre, nombre, puntero) if padre is not None else None

    def _puntero_contenido(self, cuerpo: dict) -> str:
        for _, _, op in self.operaciones():
            for clave, contenedor in [("requestBody", op.get("requestBody"))] + [
                (f"responses/{c}", r) for c, r in op.get("responses", {}).items()
            ]:
                if contenedor and contenedor.get("content", {}).get(cuerpo["media"]) is cuerpo["contenido"]:
                    return f"{self._puntero_op(op)}/{clave}/content/{_escapar(cuerpo['media'])}/schema"
        return ""

    def _aplicar(self, campo: Campo, padre: dict, nombre: str, construido: bool, destino, hoja: Hoja) -> None:
        nodo = padre["properties"][nombre]
        descripcion = " ".join(t for t in (campo.descripcion, campo.observacion) if t).strip()
        tipo = esquema_desde_tipo(campo.tipo)
        if campo.tipo and tipo is None and campo.tipo.upper() != "N/A":
            self.agregar("tipo_distinto", f"`{campo.nombre}`: tipo «{campo.tipo}» no reconocido", hoja)
        ultimo = campo.ruta[-1] if campo.ruta else nombre
        if componente := self._componente_para_campo(campo):
            padre["properties"][nombre] = {"$ref": f"#/components/schemas/{componente}", "description": descripcion}
        elif ultimo in destino.tipos_forzados:
            forzado = destino.tipos_forzados[ultimo]
            if tipo and tipo.get("contentEncoding") == "base64":
                self.agregar(
                    "tipo_distinto",
                    f"`{campo.nombre}`: el Excel dice base64 y el servicio recibe el archivo binario (multipart)",
                    hoja,
                )
            padre["properties"][nombre] = {**forzado, "description": descripcion}
        elif construido or not (nodo.get("type") or nodo.get("$ref") or nodo.get("anyOf")):
            if tipo:
                inferido = nodo.get("type")
                if inferido and familia(inferido) != familia(tipo["type"]):
                    self.agregar(
                        "ejemplo_tabla",
                        f"`{campo.nombre}`: el ejemplo trae {inferido} y la tabla dice «{campo.tipo}»",
                        hoja,
                    )
                if tipo["type"] == "array" and nodo.get("type") == "array":
                    tipo = {k: v for k, v in tipo.items() if k != "type"}
                limpio = {k: v for k, v in nodo.items() if k not in ("type",)} if tipo.get("type") else dict(nodo)
                if tipo.get("type") not in (None, "object", "array"):
                    limpio.pop("properties", None)
                    limpio.pop("items", None)
                nodo = {**limpio, **tipo}
            nodo["description"] = descripcion or nodo.get("description", "")
            padre["properties"][nombre] = nodo
        else:
            actual = self.esq.resolver(nodo)[0]
            if tipo and actual.get("type") and familia(actual.get("type")) != familia(tipo["type"]):
                self.agregar(
                    "tipo_distinto",
                    f"`{campo.nombre}`: Excel «{campo.tipo}», código `{actual.get('type')}` "
                    "(se conserva el del código)",
                    hoja,
                )
            elif tipo and "$ref" not in nodo:
                for restriccion in ("maxLength", "minLength", "maximum", "minimum"):
                    if restriccion in tipo:
                        nodo.setdefault(restriccion, tipo[restriccion])
            if descripcion:
                nodo["description"] = descripcion
        requeridos = padre.setdefault("required", [])
        if construido and not campo.por_tipo:
            if campo.obligatorio == "S" and nombre not in requeridos:
                requeridos.append(nombre)
        elif not construido:
            if campo.obligatorio == "S" and nombre not in requeridos:
                self.agregar(
                    "obligatoriedad_distinta",
                    f"`{campo.nombre}`: obligatorio en el Excel y opcional en el código",
                    hoja,
                )
            elif campo.obligatorio == "N" and nombre in requeridos:
                self.agregar(
                    "obligatoriedad_distinta",
                    f"`{campo.nombre}`: opcional en el Excel y obligatorio en el código (manda el código)",
                    hoja,
                )
        if not requeridos:
            padre.pop("required")

    # --- obligatoriedad por tipo de trámite ---------------------------------------------------------------------
    def _obligatoriedad(self, hoja: Hoja, peticion: dict) -> None:
        base = peticion["componente"]
        tipos = [t for t in mapeo.TIPOS_TRAMITE_OBLIGATORIEDAD if any(t in c.por_tipo for c in hoja.campos)]
        conf = mapeo.TIPOS_TRAMITE_OBLIGATORIEDAD
        matriz = []
        exigidos: dict[str, dict[tuple[str, ...], list[str]]] = {t: defaultdict(list) for t in tipos}
        condicionales = 0
        for campo in hoja.campos:
            if not campo.por_tipo or campo.seccion != "parametros":
                continue
            ruta = tuple(campo.ruta)
            fila = {"campo": campo.nombre}
            for t in tipos:
                fila[conf[t]["clave"]] = "obligatorio" if campo.por_tipo.get(t) == "S" else "opcional"
                if campo.por_tipo.get(t) == "S":
                    exigidos[t][ruta[:-1]].append(ruta[-1])
            if campo.observacion:
                fila["condicion"] = campo.observacion
                condicionales += 1
            matriz.append(fila)
        # Lo obligatorio en todos los tipos va al esquema base; el resto, a la variante de cada tipo.
        comunes: dict[tuple[str, ...], list[str]] = {}
        for padre in set().union(*(set(e) for e in exigidos.values())):
            comunes[padre] = [
                n for n in exigidos[tipos[0]].get(padre, []) if all(n in exigidos[t].get(padre, []) for t in tipos)
            ]
        esquema_base = self.esq.componentes[base]
        for padre, nombres in comunes.items():
            if nombres:
                self._exigir(esquema_base, esquema_base, padre, nombres)
        variantes = []
        for t in tipos:
            extra: dict = {"type": "object", "properties": {"transaction_type": {"const": conf[t]["valor"]}}}
            for padre, nombres in exigidos[t].items():
                propios = [n for n in nombres if n not in comunes.get(padre, [])]
                if propios:
                    self._exigir(extra, esquema_base, padre, propios)
            nombre = conf[t]["componente"]
            self.esq.componentes[nombre] = {
                "description": f"Registro de un trámite de tipo «{t.capitalize()}» con sus campos obligatorios.",
                "allOf": [{"$ref": f"#/components/schemas/{base}"}, extra],
            }
            variantes.append(nombre)
        tramites = self.esq.componentes.get("TipoTramite", {}).get("enum", [])
        otros = [v for v in tramites if v not in {conf[t]["valor"] for t in tipos}]
        if otros:
            self.esq.componentes["TramiteOtro"] = {
                "description": "Registro de otros trámites (blindaje, cambios, duplicados, prendas…): el Excel no "
                "define obligatoriedad propia para ellos.",
                "allOf": [
                    {"$ref": f"#/components/schemas/{base}"},
                    {"type": "object", "properties": {"transaction_type": {"enum": otros}}},
                ],
            }
            variantes.append("TramiteOtro")
        esquema_base["x-flit-obligatoriedad"] = {
            "propiedad": "transaction_type",
            "discriminador": "no viable: `transaction_type` es numérico y comparte tabla con otros trámites; las "
            "variantes se distinguen con `const` dentro de `oneOf`",
            "tipos": [{"clave": conf[t]["clave"], "nombre": t.capitalize(), "valor": conf[t]["valor"]} for t in tipos],
            "campos": matriz,
        }
        cuerpo = peticion["contenido"]["schema"]
        variante = {"oneOf": [{"$ref": f"#/components/schemas/{v}"} for v in variantes]}
        if cuerpo.get("type") == "array":
            cuerpo["items"] = variante
        else:
            peticion["contenido"]["schema"] = variante
        if condicionales:
            self.agregar(
                "obligatoriedad_distinta",
                f"{condicionales} reglas de obligatoriedad condicionales («si el vendedor es un NIT», «si usan "
                "mandante»…) quedan como texto en `x-flit-obligatoriedad`; no se modelan como esquema",
                hoja,
            )

    @staticmethod
    def _exigir(destino: dict, base: dict, padre: tuple[str, ...], nombres: list[str]) -> None:
        """Marca `nombres` como obligatorios dentro de `padre`, en `destino` (el base o la variante de un tipo),
        con la forma (objeto o lista) que tiene `base`."""

        def real(referencia: dict, nombre: str) -> str:
            return next((p for p in referencia.get("properties", {}) if norma(p) == norma(nombre)), nombre)

        def agregar(nodo: dict, nombre: str) -> None:
            requeridos = nodo.setdefault("required", [])
            if nombre not in requeridos:
                requeridos.append(nombre)

        nodo, referencia = destino, base
        for segmento in padre:
            nombre = real(referencia, segmento)
            agregar(nodo, nombre)
            propiedad = referencia.get("properties", {}).get(nombre, {})
            hijo = nodo if nodo is referencia else nodo.setdefault("properties", {}).setdefault(nombre, {})
            if nodo is referencia:
                hijo = propiedad
            if propiedad.get("type") == "array":
                hijo.setdefault("minItems", 1)
                hijo = hijo.setdefault("items", {})
                propiedad = propiedad.get("items", {})
            nodo, referencia = hijo, propiedad
        for nombre in nombres:
            agregar(nodo, real(referencia, nombre))

    # --- ejemplos de la operación -------------------------------------------------------------------------------
    def _revisar_ejemplos(self, hoja: Hoja, ejemplos: list[Ejemplo | None]) -> None:
        for ejemplo in ejemplos:
            if ejemplo is None:
                continue
            if ejemplo.error:
                self.agregar("ejemplo_ajustado", f"un ejemplo {ejemplo.error}; no pasa al spec", hoja)
            if ejemplo.reparaciones:
                self.agregar(
                    "ejemplo_ajustado", "ejemplo reparado: " + "; ".join(sorted(set(ejemplo.reparaciones))), hoja
                )

    def _ejemplos_operacion(self, op, hoja, destino, entrada, salidas, peticion, respuesta, salida_es_peticion) -> None:
        cuenta: Counter = Counter()
        contexto = op["operationId"]
        if entrada and entrada.tipo == "urls":
            for url in entrada.valores:
                ultimo = url.rstrip("/").rsplit("/", 1)[-1]
                for parametro in op.get("parameters", []):
                    if parametro.get("in") == "path" and re.fullmatch(r"\d+", ultimo) and "example" not in parametro:
                        tipo = self.esq.resolver(parametro.get("schema"))[0].get("type")
                        parametro["example"] = ultimo if tipo == "string" else int(ultimo)
        documentos = _documentos(entrada)
        if entrada and entrada.tipo == "curl":
            formulario = _formulario_tipado(entrada.valores[0]["formulario"])
            for clave, valor in formulario.items():
                if isinstance(valor, str) and valor.startswith("@"):
                    formulario[clave] = "documento.pdf"
                    cuenta["ruta_local"] += 1
            documentos = [formulario]
        exitos, errores = [], []
        for documento in (d for s in salidas for d in _documentos(s)):
            (errores if _es_error(documento) else exitos).append(documento)
        if salida_es_peticion and not documentos:
            documentos, exitos = exitos[:1], []
        if peticion and documentos:
            self._fijar_ejemplos(peticion, documentos[:1], f"{contexto} (petición)", hoja, cuenta)
            self._comparar_tabla(hoja, "salida" if salida_es_peticion else "parametros", documentos[0], destino.alias)
        if respuesta and exitos:
            self._fijar_ejemplos(respuesta, exitos, f"{contexto} (respuesta)", hoja, cuenta)
            self._comparar_tabla(hoja, "salida", exitos[0], destino.alias)
        for documento in errores:
            self._ejemplo_error(op, documento, hoja, cuenta)
        self.registro.saneamiento.update(cuenta)

    def _fijar_ejemplo(self, cuerpo: dict, documento, contexto: str, hoja: Hoja, cuenta: Counter) -> None:
        esquema = cuerpo["contenido"].get("schema", {})
        problemas: list[str] = []
        valor = self.esq.ajustar(sanear_ejemplo(documento, cuenta), esquema, "$", problemas)
        cuerpo["contenido"]["example"] = valor
        if problemas:
            self.agregar(
                "ejemplo_ajustado",
                f"{contexto}: " + "; ".join(problemas[:8]) + ("…" if len(problemas) > 8 else ""),
                hoja,
            )

    def _fijar_ejemplos(self, cuerpo: dict, documentos: list, contexto: str, hoja: Hoja, cuenta: Counter) -> None:
        if len(documentos) == 1:
            self._fijar_ejemplo(cuerpo, documentos[0], contexto, hoja, cuenta)
            return
        esquema = cuerpo["contenido"].get("schema", {})
        ejemplos = {}
        for i, documento in enumerate(documentos, start=1):
            problemas: list[str] = []
            valor = self.esq.ajustar(sanear_ejemplo(documento, cuenta), esquema, "$", problemas)
            ejemplos[f"excel-{i}"] = {"summary": f"Ejemplo {i} del Excel (saneado)", "value": valor}
            if problemas:
                self.agregar("ejemplo_ajustado", f"{contexto}, ejemplo {i}: " + "; ".join(problemas[:8]), hoja)
        cuerpo["contenido"].pop("example", None)
        cuerpo["contenido"]["examples"] = ejemplos

    def _ejemplo_error(self, op: dict, documento: dict, hoja: Hoja, cuenta: Counter) -> None:
        mensaje = str(next(iter(documento.values()), "")).lower()
        codigo = next((c for texto, c in mapeo.MENSAJES_CODIGO if texto in mensaje), None)
        if codigo is None:
            self.agregar(
                "codigo_respuesta", f"`{op['operationId']}`: un ejemplo de error no se pudo asociar a un código", hoja
            )
            return
        respuestas = op.setdefault("responses", {})
        if str(codigo) not in respuestas:
            respuestas[str(codigo)] = {
                "description": self.codigos.get(codigo, f"Error {codigo}."),
                "content": {"application/json": {"schema": self._esquema_mensaje()}},
            }
            self.agregar(
                "codigo_respuesta",
                f"`{op['operationId']}`: el Excel muestra un error {codigo} que el swagger no declara; se agrega",
                hoja,
            )
        contenido = next(iter(respuestas[str(codigo)].setdefault("content", {}).values()), None)
        if contenido is None:
            return
        problemas: list[str] = []
        valor = self.esq.ajustar(sanear_ejemplo(documento, cuenta), contenido.get("schema", {}), "$", problemas)
        if problemas:
            self.agregar(
                "codigo_respuesta",
                f"`{op['operationId']}`: el ejemplo de error {codigo} del Excel no coincide con el esquema del código "
                f"({'; '.join(problemas[:3])}); no se usa",
                hoja,
            )
            return
        ejemplos = contenido.setdefault("examples", {})
        contenido.pop("example", None)
        ejemplos[f"excel-{len(ejemplos) + 1}"] = {"summary": "Ejemplo de error del Excel (saneado)", "value": valor}

    def _esquema_mensaje(self) -> dict:
        if "DefaultResponse" in self.esq.componentes:
            return {"$ref": "#/components/schemas/DefaultResponse"}
        self.esq.componentes["DefaultResponse"] = {
            "type": "object",
            "properties": {"message": {"type": "string"}},
            "required": ["message"],
        }
        return {"$ref": "#/components/schemas/DefaultResponse"}

    def _comparar_tabla(self, hoja: Hoja, seccion: str, documento, alias: dict[str, str]) -> None:
        def claves(valor, prefijo=()):
            if isinstance(valor, list):
                for v in valor[:1]:
                    yield from claves(v, prefijo)
            elif isinstance(valor, dict):
                for k, v in valor.items():
                    yield (*prefijo, norma(k))
                    yield from claves(v, (*prefijo, norma(k)))

        campos = [c for c in hoja.campos if c.seccion == seccion and c.nombre.upper() != "N/A"]
        en_tabla = {tuple(norma(alias.get(c.nombre, s)) for s in c.ruta) for c in campos}
        en_ejemplo = set(claves(documento))
        # Envoltura que la tabla no menciona (p. ej. {"data": [...]}).
        raices = {k for k in en_ejemplo if len(k) == 1}
        if len(raices) == 1 and not any(t[:1] in raices for t in en_tabla):
            en_ejemplo = {k[1:] for k in en_ejemplo if len(k) > 1}
        prefijos_tabla = {t[:i] for t in en_tabla for i in range(1, len(t) + 1)}
        faltan = [c.nombre for c in campos if tuple(norma(alias.get(c.nombre, s)) for s in c.ruta) not in en_ejemplo]
        sobran = sorted(".".join(k) for k in en_ejemplo if k not in prefijos_tabla and k[:-1] in prefijos_tabla | {()})
        if faltan:
            self.agregar(
                "ejemplo_tabla",
                f"campos de la tabla («{seccion}») que el ejemplo no trae: " + ", ".join(f"`{f}`" for f in faltan),
                hoja,
            )
        if sobran:
            self.agregar(
                "ejemplo_tabla",
                f"claves del ejemplo («{seccion}») que la tabla no documenta: " + ", ".join(f"`{s}`" for s in sobran),
                hoja,
            )

    # --- webhooks -----------------------------------------------------------------------------------------------
    def _webhook(self, destino: mapeo.Destino, hoja: Hoja | None) -> None:
        if hoja is None:
            return
        nombre = destino.webhook
        assert nombre  # noqa: S101 - el destino es de webhook
        titulo = nombre[0].upper() + nombre[1:]
        op: dict = {
            "operationId": f"notificar{titulo}",
            "summary": f"Notificación «{hoja.servicio}» que FLIT envía al gestor",
            "description": (
                f"FLIT hace `POST` a la URL que el gestor registró (`url_web_hook` / `UrlWebHook`) cuando cambia el "
                f"estado. Fuente: hoja «{hoja.nombre}» del Excel. [por verificar] qué token envía FLIT en la cabecera "
                "`Authorization` de la notificación (el Excel solo dice `Bearer`)."
            ),
            "tags": ["Webhooks"],
            "x-flit-hoja-excel": hoja.nombre,
            "requestBody": {"required": True, "content": {"application/json": {"schema": {}}}},
            "responses": {
                "200": {
                    "description": "El gestor recibió la notificación.",
                    "content": {"application/json": {"schema": {}}},
                }
            },
        }
        if destino.pendiente:
            op["x-flit-pendiente"] = destino.pendiente
            op["x-internal"] = True
        else:
            op["x-public"] = True
        if "authorization" in (hoja.cabeceras or "").lower():
            op["security"] = [{"bearer": []}]
            self._esquema_bearer()
        self.spec.setdefault("webhooks", {})[nombre] = {"post": op}
        self._campos_y_ejemplos(op, destino, hoja, reportar=False)

    # --- visibilidad, respuestas e información ------------------------------------------------------------------

    def _visibilidad(self) -> None:
        sin_seguridad = []
        for ruta, metodo, op in self.operaciones():
            if not op.get("x-public"):
                op["x-internal"] = True
                if metodo != "get" and not op.get("security") and not ruta.startswith("webhook:"):
                    sin_seguridad.append(f"`{metodo.upper()} {ruta}`")
        if sin_seguridad:
            self.agregar(
                "seguridad",
                f"{len(sin_seguridad)} operaciones internas que cambian estado no declaran seguridad: "
                + ", ".join(sin_seguridad),
            )

    def _respuestas(self) -> None:
        for _, _, op in self.operaciones():
            respuestas = op.setdefault("responses", {})
            if op.get("x-public") and op.get("tags") != ["Webhooks"]:
                if op.get("security") and "401" not in respuestas and 401 in self.codigos:
                    respuestas["401"] = {
                        "description": self.codigos[401],
                        "content": {"application/json": {"schema": self._esquema_mensaje()}},
                    }
                if "500" not in respuestas and 500 in self.codigos:
                    respuestas["500"] = {
                        "description": self.codigos[500],
                        "content": {"application/json": {"schema": self._esquema_mensaje()}},
                    }
            for codigo, respuesta in respuestas.items():
                if str(respuesta.get("description", "")).strip().lower() in _DESCRIPCIONES_GENERICAS:
                    respuesta["description"] = (
                        self.codigos.get(int(codigo), "Respuesta exitosa.") if codigo.isdigit() else "Respuesta."
                    )
        no_usados = sorted(
            set(self.codigos)
            - {int(c) for _, _, op in self.operaciones() for c in op.get("responses", {}) if c.isdigit()}
        )
        if no_usados and self.api.hoja_version:
            self.agregar(
                "codigo_respuesta",
                "códigos de la tabla del Excel que ninguna operación de este repo declara: "
                + ", ".join(str(c) for c in no_usados),
            )

    def _info(self, hojas: list[tuple[mapeo.Destino, Hoja | None]]) -> None:
        hoja_version = self.libro.hoja(self.api.hoja_version) if (self.libro and self.api.hoja_version) else None
        version = version_semantica(hoja_version.version if hoja_version else None)
        if self.api.hoja_version and version is None:
            self.agregar(
                "metadatos",
                f"la hoja «{self.api.hoja_version}» no trae versión; el spec queda en 1.0.0",
                hoja_version,
            )
        visibles = [op for _, _, op in self.operaciones() if op.get("x-public")]
        fuentes = [h.nombre for _, h in hojas if h is not None]
        descripcion = self.api.descripcion
        if fuentes:
            descripcion += "\n\nFuente: swagger de tsoa del repo y hojas " + ", ".join(f"«{f}»" for f in fuentes)
            descripcion += " del Excel de contratos ICT."
        else:
            descripcion += (
                "\n\nFuente: swagger de tsoa del repo (el Excel de contratos ICT no documenta este servicio)."
            )
        descripcion += (
            "\n\nArchivo generado con `python -m atlas_docs.legado` (ATLAS-ADR-0037); no se edita a mano. Las "
            "operaciones con `x-public` son las que se pueden compartir con clientes; las `x-internal` las quita la "
            "poda de Atlas."
        )
        changelog = []
        for _, hoja in hojas:
            if hoja is not None and (hoja.version or hoja.fecha):
                changelog.append(
                    {
                        "version": version_semantica(hoja.version) or "sin versión",
                        "fecha": hoja.fecha.isoformat() if hoja.fecha else None,
                        "hoja": hoja.nombre,
                        "colaborador": COLABORADOR_GENERICO if hoja.colaborador else "sin dato en el Excel",
                        "cambio": f"Versión {hoja.version} de la hoja «{hoja.nombre}» del Excel de contratos ICT.",
                    }
                )
        changelog.sort(key=lambda c: (c["fecha"] or "", c["hoja"]))
        self.registro.changelog[self.api.codigo] = changelog
        self.spec["info"] = {
            "title": f"{self.api.titulo} ({self.api.repo})",
            "version": version or "1.0.0",
            "description": descripcion,
            "contact": {"name": "FLIT S.A.S."},
            "x-flit-visibilidad": "compartible" if visibles else "interna",
            "x-flit-fuente": {
                "repo": f"flitsas/{self.api.repo}",
                "swagger": "swagger.json",
                "excel": self.libro.archivo if self.libro and fuentes else None,
                "hojas": fuentes,
            },
            "x-flit-changelog": changelog,
        }
        self.registro.resumen.append(
            {
                "codigo": self.api.codigo,
                "repo": self.api.repo,
                "version": version or "1.0.0",
                "visibilidad": "compartible" if visibles else "interna",
                "publicas": len(
                    [1 for r, _, op in self.operaciones() if op.get("x-public") and not r.startswith("webhook:")]
                ),
                "internas": len(
                    [1 for r, _, op in self.operaciones() if op.get("x-internal") and not r.startswith("webhook:")]
                ),
                "webhooks": len(self.spec.get("webhooks", {})),
            }
        )

    def _servidores(self, hojas: list[tuple[mapeo.Destino, Hoja | None]]) -> None:
        hosts = Counter(
            host(h.urls["qa"])
            for _, h in hojas
            if h is not None and h.urls.get("qa") and re.match(mapeo.HOSTS_PUBLICOS, host(h.urls["qa"]))
        )
        ambientes = {"dev": "por confirmar", "qa": "por confirmar", "pdn": "por confirmar"}
        if hosts:
            elegido = hosts.most_common(1)[0][0]
            ambientes["qa"] = elegido
            self.spec["servers"] = [
                {
                    "url": "https://{host}",
                    "description": "QA. Host tomado del Excel [por verificar]; el Excel no trae URL de dev ni de pdn.",
                    "variables": {"host": {"default": elegido, "description": "Host de la API ICT en el ambiente."}},
                    "x-flit-ambiente": "qa",
                }
            ]
        else:
            self.spec.pop("servers", None)
        self.spec["info"]["x-flit-ambientes"] = ambientes

    # --- ejemplos en todo el spec -------------------------------------------------------------------------------
    def _ejemplos(self) -> None:
        for _, _, op in self.operaciones():
            for parametro in op.get("parameters", []):
                if "example" not in parametro and "examples" not in parametro:
                    parametro["example"] = self.esq.ficticio(parametro.get("schema", {}))
            contenedores = [op.get("requestBody")] + list(op.get("responses", {}).values())
            for contenedor in contenedores:
                for contenido in (contenedor or {}).get("content", {}).values():
                    if "example" in contenido or "examples" in contenido:
                        self._sembrar(
                            contenido.get(
                                "example", next(iter(contenido.get("examples", {}).values()), {}).get("value")
                            ),
                            contenido.get("schema", {}),
                        )
        for _, _, op in self.operaciones():
            contenedores = [op.get("requestBody")] + list(op.get("responses", {}).values())
            for contenedor in contenedores:
                for contenido in (contenedor or {}).get("content", {}).values():
                    esquema = contenido.setdefault("schema", {})
                    self._rellenar(esquema)
                    if "example" not in contenido and "examples" not in contenido:
                        contenido["example"] = self.esq.ficticio(esquema) if esquema else {}
        for esquema in self.esq.componentes.values():
            self._rellenar(esquema)
        for parametro_op in (p for _, _, op in self.operaciones() for p in op.get("parameters", [])):
            self._rellenar(parametro_op.get("schema", {}))

    def _sembrar(self, valor: object, esquema: dict, vistos: tuple[str, ...] = ()) -> None:
        if not isinstance(esquema, dict) or valor is None:
            return
        if "$ref" in esquema:
            nombre = esquema["$ref"].split("/")[-1]
            if nombre in vistos:
                return
            self._sembrar(valor, self.esq.componentes.get(nombre, {}), (*vistos, nombre))
            return
        if any(k in esquema for k in ("oneOf", "anyOf", "allOf")) or not esquema:
            return
        if "examples" not in esquema and "example" not in esquema:
            esquema["examples"] = [copy.deepcopy(valor)]
        if isinstance(valor, dict):
            for clave, interno in valor.items():
                self._sembrar(interno, esquema.get("properties", {}).get(clave), vistos)
        elif isinstance(valor, list) and valor:
            self._sembrar(valor[0], esquema.get("items"), vistos)

    def _rellenar(self, esquema: dict) -> None:
        if not isinstance(esquema, dict):
            return
        for propiedad in esquema.get("properties", {}).values():
            self._rellenar(propiedad)
        for clave in ("items", "additionalProperties"):
            if isinstance(esquema.get(clave), dict):
                self._rellenar(esquema[clave])
        # Las partes de allOf/oneOf/anyOf no llevan ejemplo propio: solas no cumplen los obligatorios del conjunto.
        if "example" in esquema:
            esquema["examples"] = [esquema.pop("example")]
        if "$ref" in esquema or "examples" in esquema or not esquema:
            return
        if set(esquema) <= {"description", "required", "x-flit-obligatoriedad"}:
            return
        esquema["examples"] = [self.esq.ficticio(esquema)]

    def _etiquetas(self) -> None:
        usadas: dict[str, str] = {}
        for _, _, op in self.operaciones():
            nuevas = []
            for etiqueta in op.get("tags", []) or ["General"]:
                nombre, _, descripcion = etiqueta.partition(": ")
                nombre = nombre.strip()
                usadas.setdefault(nombre, descripcion.strip() or nombre)
                nuevas.append(nombre)
            op["tags"] = nuevas
        if "Webhooks" in usadas:
            usadas["Webhooks"] = "Notificaciones que FLIT envía a la URL que registró el gestor."
        self.spec["tags"] = [{"name": n, "description": d} for n, d in usadas.items()]

    def _ordenar(self) -> dict:
        claves_op = [
            "operationId",
            "summary",
            "description",
            "tags",
            "security",
            "parameters",
            "requestBody",
            "responses",
        ]
        for ruta, metodo, op in list(self.operaciones()):
            ordenada = {k: op[k] for k in claves_op if k in op} | {k: v for k, v in op.items() if k not in claves_op}
            contenedor = self.spec["webhooks" if ruta.startswith("webhook:") else "paths"]
            contenedor[ruta.removeprefix("webhook:")][metodo] = ordenada
        orden = ["openapi", "info", "servers", "tags", "paths", "webhooks", "components"]
        return {k: self.spec[k] for k in orden if k in self.spec} | {
            k: v for k, v in self.spec.items() if k not in orden
        }


def _es_error(documento: object) -> bool:
    return isinstance(documento, dict) and set(documento) <= {"message"}


def _formulario_tipado(formulario: dict) -> dict:
    """Los valores de un `--form` de curl son texto; números y lógicos se tipan para inferir el esquema."""
    tipado: dict = {}
    for clave, valor in formulario.items():
        if re.fullmatch(r"-?\d+", valor):
            tipado[clave] = int(valor)
        elif valor.lower() in ("true", "false"):
            tipado[clave] = valor.lower() == "true"
        else:
            tipado[clave] = valor
    return tipado


def _revisiones_globales(libro: Libro | None, swaggers: dict[str, dict], registro: Registro) -> None:
    if libro is None:
        registro.agregar("metadatos", "se convirtió sin el Excel: solo swagger de tsoa")
        return
    nombres_hojas = {norma(h.servicio) for h in libro.hojas}
    if libro.indice:
        for servicio, descripcion in libro.indice.servicios:
            if norma(servicio) not in nombres_hojas:
                registro.agregar(
                    "hoja_faltante",
                    f"«{servicio}» está en el índice («{descripcion or 'sin descripción'}») y no tiene hoja",
                )
            elif not descripcion:
                registro.agregar("metadatos", f"«{servicio}» no tiene descripción en el índice")
    for vacia in libro.hojas_vacias:
        registro.agregar("metadatos", f"la hoja «{vacia}» está vacía")
    hosts: dict[str, set[str]] = defaultdict(set)
    for hoja in libro.hojas:
        textos = [hoja.cabeceras, hoja.ejemplo_entrada, *hoja.ejemplos_salida, *hoja.urls.values()]
        textos += [c.descripcion for c in hoja.campos] + [v for _, v in hoja.celdas_sin_interpretar]
        textos += [v for tabla in hoja.tablas for fila in tabla.filas for v in fila]
        for texto in textos:
            for hallazgo in buscar_sensibles(texto or ""):
                registro.detectado_excel[hallazgo.tipo] += 1
        if hoja.colaborador:
            registro.detectado_excel["colaborador"] += 1
        vacias = [a for a in ("dev", "qa", "pdn") if not hoja.urls.get(a)]
        if vacias:
            registro.agregar("url_vacia", f"sin URL de {', '.join(vacias)}", hoja=hoja.nombre)
        if not hoja.version:
            registro.agregar("metadatos", "sin versión, fecha ni colaborador", hoja=hoja.nombre)
        for url in hoja.urls.values():
            if url:
                hosts[host(url)].add(hoja.nombre)
        for texto in [hoja.ejemplo_entrada, *hoja.ejemplos_salida]:
            ejemplo = interpretar(texto)
            if ejemplo and ejemplo.tipo == "urls":
                for url in ejemplo.valores:
                    hosts[host(url)].add(hoja.nombre)
            elif ejemplo and ejemplo.tipo == "curl" and ejemplo.valores[0].get("url"):
                hosts[host(ejemplo.valores[0]["url"])].add(hoja.nombre)
        if not any(c.seccion == "salida" for c in hoja.campos) or not hoja.ejemplos_salida:
            registro.agregar("salida_incompleta", "la hoja no trae tabla o ejemplo de salida", hoja=hoja.nombre)
        for celda, _ in hoja.celdas_sin_interpretar:
            registro.agregar("celda_sin_interpretar", f"celda {celda}", hoja=hoja.nombre)
    if len(hosts) > 1:
        for nombre, hojas in sorted(hosts.items()):
            registro.agregar(
                "hosts_mezclados",
                f"`{_host_para_informe(nombre)}` en {', '.join(f'«{h}»' for h in sorted(hojas))}",
            )
    _nombres_con_error(libro, swaggers, registro)


def _host_para_informe(nombre: str) -> str:
    if re.match(mapeo.HOSTS_PUBLICOS, nombre):
        return nombre
    if nombre.endswith(".amazonaws.com"):
        return "*." + ".".join(nombre.split(".")[1:]) + " (API Gateway de AWS; id omitido)"
    if nombre in ("localhost", "127.0.0.1"):
        return "servidor local de pruebas (mock)"
    return "host externo omitido"


def _nombres_con_error(libro: Libro, swaggers: dict[str, dict], registro: Registro) -> None:
    """Cada error de nombre conocido, una vez por palabra y lugar (campos, descripciones, hojas, índice, rutas)."""
    lugares: dict[str, set[str]] = defaultdict(set)

    def anotar(texto: str | None, donde: str) -> None:
        for palabra in re.findall(r"[A-Za-zÁÉÍÓÚáéíóúÑñ_]+", texto or ""):
            if any(error.lower() in palabra.lower() for error in mapeo.NOMBRES_CON_ERROR):
                lugares[palabra].add(donde)

    for hoja in libro.hojas:
        anotar(hoja.nombre, "nombre de hoja")
        for campo in hoja.campos:
            for segmento in campo.ruta:
                anotar(segmento, f"campos de «{hoja.nombre}»")
            anotar(campo.descripcion, f"descripciones de «{hoja.nombre}»")
        for texto in [hoja.ejemplo_entrada, *hoja.ejemplos_salida]:
            anotar(texto, f"ejemplos de «{hoja.nombre}»")
    if libro.indice:
        for servicio, descripcion in libro.indice.servicios:
            anotar(servicio, "índice del Excel")
            anotar(descripcion, "índice del Excel")
    for repo, swagger in swaggers.items():
        for ruta in swagger.get("paths", {}):
            anotar(ruta.replace("/", " "), f"rutas de `{repo}`")
    for palabra, donde in sorted(lugares.items()):
        error, correcto = next((e, c) for e, c in mapeo.NOMBRES_CON_ERROR.items() if e.lower() in palabra.lower())
        registro.agregar(
            "nombre_con_error",
            f"`{palabra}` en {', '.join(sorted(donde))}: «{error}» por «{correcto}». El spec conserva el nombre que "
            "usa la API",
        )


def convertir(libro: Libro | None, swaggers: dict[str, dict]) -> Resultado:
    registro = Registro()
    _revisiones_globales(libro, swaggers, registro)
    specs: dict[str, dict] = {}
    for api in mapeo.APIS:
        if api.repo not in swaggers:
            continue
        specs[api.codigo] = _Convertidor(api, swaggers[api.repo], libro, registro).convertir()
    if libro is not None:
        for destino in mapeo.DESTINOS:
            hoja = libro.hoja(destino.hoja)
            if hoja is None:
                continue
            if destino.repo is None:
                registro.agregar("operacion_sin_ruta", destino.pendiente or "sin operación", hoja=hoja.nombre)
                for campo in hoja.campos:
                    registro.trazar(campo, "informe", f"la hoja no tiene operación en el código: {destino.pendiente}")
                for tabla in hoja.tablas:
                    registro.agregar(
                        "tabla_referencia",
                        f"«{tabla.titulo}» ({tabla.celda}) queda fuera: la hoja no tiene operación (repite la de "
                        "transacciones con códigos sin ID numérico)",
                        hoja=hoja.nombre,
                    )
            elif destino.pendiente:
                registro.agregar("operacion_sin_ruta", destino.pendiente, hoja=hoja.nombre)
            if destino.hoja == "SearchPlate":
                registro.agregar(
                    "salida_incompleta",
                    "la salida solo confirma el registro de la placa; el estado y los gastos no tienen salida "
                    "documentada en esta hoja (§8.4, punto 8)",
                    hoja=hoja.nombre,
                )
        registradas = {(t["hoja"], t["celda"]) for t in registro.traza}
        for hoja in libro.hojas:
            if all(d.hoja.lower() != hoja.servicio.lower() for d in mapeo.DESTINOS):
                registro.agregar("operacion_sin_ruta", "la hoja no está en el mapeo", hoja=hoja.nombre)
            for campo in hoja.campos:
                if (campo.hoja, campo.celda) not in registradas:
                    registro.trazar(campo, "informe", "la hoja no está asociada a ninguna operación")
    return Resultado(specs, registro)
