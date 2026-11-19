"""Informe de inconsistencias en Markdown (pasa markdownlint con la configuración del repo)."""

from collections import Counter, defaultdict
from datetime import date

from atlas_docs.legado.conversion import TIPOS_INCONSISTENCIA, Registro

_SANEAMIENTO = {
    "jwt": "Tokens con forma de JWT → `$TOKEN`",
    "bearer": "Valores después de `Bearer` → `$TOKEN`",
    "credencial": "Contraseñas y tokens por nombre de campo → `$CLAVE` / `$TOKEN`",
    "correo": "Correos → `usuario@ejemplo.com`",
    "ruta_local": "Rutas locales de equipos personales → `documento.pdf`",
    "ip_privada": "IP privadas → `192.0.2.10`",
    "host_interno": "Hosts internos (API Gateway de AWS, servidor local, Azure DevOps) → `api.ejemplo.com`",
    "persona": "Nombres de personas → `Nombre Demo` / `Apellido Demo`",
    "organizacion": "Nombres de empresas → `Organismo Demo`",
    "documento": "Números de documento → `900123456`",
    "telefono": "Teléfonos → `3001234567`",
    "placa": "Placas → `ABC123`",
    "vin": "VIN → `VIN00000000000001`",
}


_DETECTADO = {
    "jwt": "Tokens con forma de JWT",
    "bearer": "Valores después de `Bearer`",
    "correo": "Correos de dominios reales",
    "ruta_windows": "Rutas locales de Windows",
    "ruta_unix": "Rutas locales de Unix",
    "ip_privada": "IP privadas",
    "host_interno": "Hosts internos",
    "colaborador": "Hojas con el nombre de un colaborador (el changelog lo deja genérico)",
}


def _celda(texto: object) -> str:
    return str(texto if texto is not None else "").replace("|", "\\|").replace("\n", " ").strip()


def _fecha(valor: str | None) -> str:
    if not valor:
        return "sin fecha"
    return date.fromisoformat(valor).strftime("%d/%m/%Y")


def generar(registro: Registro, archivo_excel: str | None) -> str:
    lineas: list[str] = [
        "# Informe de la conversión del legado ICT",
        "",
        "Generado por `python -m atlas_docs.legado` (HU 3.8, ATLAS-ADR-0037) desde el swagger de tsoa de cada "
        f"`BackApiExternal*` y el Excel `{archivo_excel or 'sin Excel'}`. No se edita a mano: se regenera junto con "
        "los specs. No trae valores sensibles: los ejemplos y los hosts internos se describen sin su valor.",
        "",
        "## Resumen",
        "",
        "| Contrato | Repo | Versión | Visibilidad | Operaciones públicas | Operaciones internas | Webhooks |",
        "|---|---|---|---|---|---|---|",
    ]
    for fila in registro.resumen:
        lineas.append(
            f"| `{fila['codigo']}` | `{fila['repo']}` | {fila['version']} | {fila['visibilidad']} | "
            f"{fila['publicas']} | {fila['internas']} | {fila['webhooks']} |"
        )
    conteo = Counter(i.tipo for i in registro.inconsistencias)
    lineas += ["", "## Inconsistencias por tipo", "", "| Tipo | Cantidad |", "|---|---|"]
    for clave, titulo in TIPOS_INCONSISTENCIA.items():
        if conteo[clave]:
            lineas.append(f"| [{titulo}](#{_ancla(titulo)}) | {conteo[clave]} |")
    lineas.append(f"| **Total** | **{sum(conteo.values())}** |")

    pendientes = [
        "Confirmar las URL vigentes de dev, qa y pdn de cada API: el Excel solo trae QA y mezcla hosts (no se "
        "adivinaron; los specs solo declaran el host de QA del Excel).",
        "Decidir si RequestSign y StatusSign existen (están en el índice y no tienen hoja).",
        "Confirmar el servicio y la ruta de SearchPlate y StatusPlate, y si WebHookPlate se publica.",
        "Confirmar si los specs enriquecidos viven en cada repo legado o en un repo de contratos legados (§8.4, "
        "punto 9) y aprobar los PR propuestos en `propuestas/`.",
        "Revisar los campos que el código exige y el Excel no documenta, y los tipos que no coinciden.",
    ]
    lineas += ["", "## Pendientes para el líder técnico", ""]
    lineas += [f"- [por verificar] {p}" for p in pendientes]

    por_tipo: dict[str, list] = defaultdict(list)
    for inconsistencia in registro.inconsistencias:
        por_tipo[inconsistencia.tipo].append(inconsistencia)
    for clave, titulo in TIPOS_INCONSISTENCIA.items():
        if not por_tipo[clave]:
            continue
        lineas += ["", f"## {titulo}", "", "| Contrato | Hoja | Detalle |", "|---|---|---|"]
        for i in por_tipo[clave]:
            lineas.append(
                f"| {_celda(i.api and f'`{i.api}`') or '—'} | {_celda(i.hoja) or '—'} | {_celda(i.detalle)} |"
            )

    fuera = [t for t in registro.traza if t["destino"] == "informe"]
    lineas += [
        "",
        "## Campos del Excel fuera del spec",
        "",
        f"De {len(registro.traza)} campos del Excel, {len(registro.traza) - len(fuera)} quedaron en un spec y "
        f"{len(fuera)} solo en este informe. `trazabilidad.json` dice dónde quedó cada uno (archivo y puntero JSON).",
        "",
        "| Hoja | Celda | Sección | Campo | Motivo |",
        "|---|---|---|---|---|",
    ]
    for t in fuera:
        lineas.append(
            f"| {_celda(t['hoja'])} | {t['celda']} | {t['seccion']} | `{_celda(t['campo'])}` | {_celda(t['detalle'])} |"
        )

    lineas += [
        "",
        "## Changelog inicial",
        "",
        "Sale de la versión, la fecha y el colaborador de cada hoja. El colaborador queda genérico: los specs se "
        "comparten con clientes y el nombre de una persona es un dato personal (Ley 1581, ATLAS-ADR-0037).",
        "",
        "| Contrato | Versión | Fecha | Hoja | Colaborador |",
        "|---|---|---|---|---|",
    ]
    for codigo, entradas in registro.changelog.items():
        for e in entradas:
            lineas.append(
                f"| `{codigo}` | {e['version']} | {_fecha(e['fecha'])} | {_celda(e['hoja'])} | {e['colaborador']} |"
            )

    lineas += [
        "",
        "## Saneamiento",
        "",
        "Lo sensible que trae el Excel (se cuenta, no se copia):",
        "",
        "| Tipo | Apariciones en el Excel |",
        "|---|---|",
    ]
    for clave, veces in sorted(registro.detectado_excel.items()):
        lineas.append(f"| {_DETECTADO.get(clave, clave)} | {veces} |")
    if not registro.detectado_excel:
        lineas.append("| Nada sensible | 0 |")
    lineas += [
        "",
        "Lo que se reemplazó al llevar ejemplos y textos a los specs (lo que no se usa, como las cabeceras del curl, "
        "no llega a ningún archivo):",
        "",
        "| Qué se reemplazó | Veces |",
        "|---|---|",
    ]
    for clave, veces in sorted(registro.saneamiento.items()):
        lineas.append(f"| {_SANEAMIENTO.get(clave, clave)} | {veces} |")
    if not registro.saneamiento:
        lineas.append("| Nada que sanear | 0 |")
    return "\n".join(lineas) + "\n"


def _ancla(titulo: str) -> str:
    """Ancla de un título como la calcula markdownlint (MD051)."""
    ancla = titulo.strip().lower()
    ancla = "".join(c for c in ancla if c.isalnum() or c in " -_")
    return ancla.replace(" ", "-")
