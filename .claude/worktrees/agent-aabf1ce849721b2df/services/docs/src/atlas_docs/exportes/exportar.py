"""Orquesta la exportación de un paquete: lee los specs, calcula el hash y genera cada formato pedido."""

import hashlib
import json
from collections.abc import Iterable
from dataclasses import dataclass, field
from importlib.metadata import PackageNotFoundError, version
from typing import Any

from atlas_docs.exportes import excel, html, pdf, postman, word
from atlas_docs.exportes.documento import Documento, armar
from atlas_docs.exportes.empaquetar import zip_determinista
from atlas_docs.exportes.modelo import Formato, PaqueteExportar
from atlas_docs.exportes.specs import SpecLeido, a_json, a_yaml, hash_contenido, leer

TIPOS_MIME = {
    ".yaml": "application/yaml",
    ".json": "application/json",
    ".html": "text/html; charset=utf-8",
    ".pdf": "application/pdf",
    ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ".xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    ".zip": "application/zip",
    ".md": "text/markdown; charset=utf-8",
}


def _version_generador() -> str:
    try:
        return version("atlas-docs")
    except PackageNotFoundError:  # pragma: no cover — sin instalar (solo en desarrollo)
        return "0.0.0"


def tipo_mime(ruta: str) -> str:
    return TIPOS_MIME.get(ruta[ruta.rfind(".") :], "application/octet-stream")


@dataclass
class Archivo:
    ruta: str
    formato: Formato
    datos: bytes

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.datos).hexdigest()


@dataclass
class ResultadoExportacion:
    hash_contenido: str
    archivos: list[Archivo] = field(default_factory=list)
    manifiesto: dict[str, Any] = field(default_factory=dict)

    def por_formato(self, formato: Formato) -> list[Archivo]:
        return [a for a in self.archivos if a.formato == formato]

    def archivo(self, ruta: str) -> Archivo:
        return next(a for a in self.archivos if a.ruta == ruta)

    @property
    def zip(self) -> Archivo | None:
        return next(iter(self.por_formato(Formato.ZIP)), None)


def _specs(specs: list[SpecLeido]) -> list[Archivo]:
    salida: list[Archivo] = []
    for s in specs:
        carpeta = "openapi" if s.tipo == "openapi" else "asyncapi"
        salida.append(Archivo(f"{carpeta}/{s.nombre_archivo}.yaml", Formato.OPENAPI, a_yaml(s.documento).encode()))
        salida.append(Archivo(f"{carpeta}/{s.nombre_archivo}.json", Formato.OPENAPI, a_json(s.documento).encode()))
    return salida


def _postman(specs: list[SpecLeido], documento: Documento) -> list[Archivo]:
    return [
        Archivo(
            f"postman/{s.nombre_archivo}.postman_collection.json",
            Formato.POSTMAN,
            postman.a_json(postman.coleccion(s, documento)).encode(),
        )
        for s in specs
        if s.tipo == "openapi"
    ]


DESCRIPCIONES = {
    Formato.OPENAPI: "Spec podado del contrato (YAML y JSON), tal como lo publicó Atlas.",
    Formato.POSTMAN: "Colección Postman v2.1 por contrato OpenAPI, con las variables {{baseUrl}} y {{token}}.",
    Formato.HTML: "Documento autocontenido para abrir en el navegador, sin conexión.",
    Formato.PDF: "Documento para imprimir o enviar.",
    Formato.WORD: "Documento de Word.",
    Formato.EXCEL: "Libro con una hoja por servicio, con el formato del Excel de contratos.",
}


def _manifiesto(documento: Documento, archivos: list[Archivo]) -> dict[str, Any]:
    return {
        "paquete": documento.paquete,
        "revision": documento.revision,
        "organizacion": documento.organizacion,
        "fecha": documento.fecha,
        "algoritmo": "SHA-256",
        "hash_contenido": documento.hash,
        "generador": f"atlas-docs {_version_generador()}",
        "archivos": [
            {"ruta": a.ruta, "formato": a.formato.value, "bytes": len(a.datos), "sha256": a.sha256} for a in archivos
        ],
    }


def _leeme(documento: Documento, archivos: list[Archivo]) -> str:
    lineas = [
        f"# {documento.paquete} · revisión {documento.revision}",
        "",
        f"{documento.leyenda}",
        "",
        f"- Organización: {documento.organizacion}",
        f"- Fecha: {documento.fecha} (hora de Bogotá)",
        f"- SHA-256 del contenido exportado: `{documento.hash}`",
        "",
        "## Contratos",
        "",
        *(f"- {c.titulo} {c.version} ({c.tipo})" for c in documento.contratos),
        "",
        "## Archivos",
        "",
        "| Archivo | Qué es |",
        "|---|---|",
        *(f"| `{a.ruta}` | {DESCRIPCIONES.get(a.formato, '')} |" for a in archivos),
        "| `manifiesto.json` | Hash del contenido y SHA-256 de cada archivo. |",
        "",
        "## Cómo usar la colección Postman",
        "",
        "1. Importa el archivo de `postman/` en Postman.",
        "2. En las variables de la colección, `baseUrl` apunta a QA; cámbiala solo si FLIT te lo indica.",
        "3. Pide el token con el servicio de autenticación del contrato y guárdalo en la variable `token`.",
        "",
        "## Cómo verificar los archivos",
        "",
        "Cada archivo tiene su SHA-256 en `manifiesto.json`. En Linux o macOS: `sha256sum <archivo>`;",
        "en Windows: `certutil -hashfile <archivo> SHA256`.",
        "",
    ]
    return "\n".join(lineas)


def exportar(paquete: PaqueteExportar, formatos: Iterable[Formato] | None = None) -> ResultadoExportacion:
    """Genera los formatos pedidos (todos si no se indica). Función pura: sin red, sin reloj, sin disco."""
    pedidos = set(formatos) if formatos is not None else set(Formato)
    specs = [leer(s) for s in paquete.specs]
    huella = hash_contenido(paquete, specs)
    documento = armar(paquete, specs, huella)
    base = documento.nombre_base

    archivos: list[Archivo] = []
    if Formato.OPENAPI in pedidos:
        archivos.extend(_specs(specs))
    if Formato.POSTMAN in pedidos:
        archivos.extend(_postman(specs, documento))
    if Formato.HTML in pedidos:
        archivos.append(Archivo(f"{base}.html", Formato.HTML, html.generar(documento).encode()))
    if Formato.PDF in pedidos:
        archivos.append(Archivo(f"{base}.pdf", Formato.PDF, pdf.generar(documento)))
    if Formato.WORD in pedidos:
        archivos.append(Archivo(f"{base}.docx", Formato.WORD, word.generar(documento)))
    if Formato.EXCEL in pedidos:
        archivos.append(Archivo(f"{base}.xlsx", Formato.EXCEL, excel.generar(documento)))

    manifiesto = _manifiesto(documento, archivos)
    if Formato.ZIP in pedidos:
        contenido = [
            (f"{base}/LEEME.md", _leeme(documento, archivos).encode()),
            (f"{base}/manifiesto.json", (json.dumps(manifiesto, ensure_ascii=False, indent=2) + "\n").encode()),
            *((f"{base}/{a.ruta}", a.datos) for a in archivos),
        ]
        archivos.append(Archivo(f"{base}.zip", Formato.ZIP, zip_determinista(contenido, documento.dia)))
    return ResultadoExportacion(huella, archivos, manifiesto)
