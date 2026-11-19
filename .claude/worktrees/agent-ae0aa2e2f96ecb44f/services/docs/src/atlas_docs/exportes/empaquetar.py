"""ZIP deterministas: la misma entrada da los mismos bytes.

Un ZIP guarda la hora de cada entrada; Word y Excel son ZIP por dentro. Todas las entradas llevan la fecha del
paquete a las 00:00 y permisos fijos, en el orden en que se agregan.
"""

import io
import zipfile
from collections.abc import Iterable
from datetime import date

_PERMISOS = 0o644 << 16


def _info(nombre: str, fecha: date) -> zipfile.ZipInfo:
    info = zipfile.ZipInfo(nombre, date_time=(max(fecha.year, 1980), fecha.month, fecha.day, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = _PERMISOS
    info.create_system = 3  # Unix: el mismo valor en Windows y en Linux
    return info


def zip_determinista(entradas: Iterable[tuple[str, bytes]], fecha: date) -> bytes:
    salida = io.BytesIO()
    with zipfile.ZipFile(salida, "w", zipfile.ZIP_DEFLATED) as archivo:
        for nombre, datos in entradas:
            archivo.writestr(_info(nombre, fecha), datos, compresslevel=9)
    return salida.getvalue()


def normalizar_zip(datos: bytes, fecha: date) -> bytes:
    """Reescribe un ZIP (docx, xlsx) con fechas y permisos fijos, sin cambiar su contenido ni el orden."""
    with zipfile.ZipFile(io.BytesIO(datos)) as original:
        entradas = [(i.filename, original.read(i.filename)) for i in original.infolist()]
    return zip_determinista(entradas, fecha)
