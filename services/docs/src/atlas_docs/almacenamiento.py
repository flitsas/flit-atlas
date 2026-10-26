"""Acceso al almacenamiento S3 (MinIO en dev y qa, Contabo en pdn)."""

import boto3
from botocore.client import BaseClient
from botocore.config import Config as BotoConfig
from botocore.exceptions import ClientError

from atlas_docs.config import Configuracion
from atlas_docs.exportes.exportar import ResultadoExportacion, tipo_mime


def crear_cliente(config: Configuracion) -> BaseClient:
    return boto3.client(
        "s3",
        endpoint_url=config.DOCS_S3_ENDPOINT,
        aws_access_key_id=config.DOCS_S3_ACCESS_KEY.get_secret_value(),
        aws_secret_access_key=config.DOCS_S3_SECRET_KEY.get_secret_value(),
        region_name=config.DOCS_S3_REGION,
        config=BotoConfig(
            # MinIO y Contabo usan rutas (endpoint/bucket), no subdominios por bucket.
            s3={"addressing_style": "path"},
            connect_timeout=3,
            read_timeout=5,
            retries={"max_attempts": 1},
        ),
    )


def asegurar_bucket(cliente: BaseClient, bucket: str) -> None:
    """Crea el bucket si no existe. Solo se llama en local (ATLAS_DOCS_S3_CREAR_BUCKET)."""
    try:
        cliente.head_bucket(Bucket=bucket)
    except ClientError as error:
        if error.response.get("Error", {}).get("Code") not in {"404", "NoSuchBucket"}:
            raise
        cliente.create_bucket(Bucket=bucket)


def subir(cliente: BaseClient, bucket: str, clave: str, datos: bytes, tipo: str, sha256: str) -> None:
    """Sube un archivo de una exportación. El SHA-256 va en los metadatos para verificarlo al descargar."""
    cliente.put_object(Bucket=bucket, Key=clave, Body=datos, ContentType=tipo, Metadata={"sha256": sha256})


class ObjetoDemasiadoGrandeError(Exception):
    """El objeto supera el tope: no se lee entero (F4-SEC-12)."""


def leer(cliente: BaseClient, bucket: str, clave: str, tope: int | None = None) -> bytes | None:
    """Lee un objeto; `None` si no existe. Cualquier otro error (S3 caído, credenciales) se propaga.

    Con `tope`, un objeto más grande lanza `ObjetoDemasiadoGrandeError` sin leerlo entero: se mira `ContentLength` y,
    por si no viene o miente, se leen como mucho `tope + 1` bytes.
    """
    try:
        respuesta = cliente.get_object(Bucket=bucket, Key=clave)
    except ClientError as error:
        if error.response.get("Error", {}).get("Code") in {"404", "NoSuchKey"}:
            return None
        raise
    cuerpo = respuesta["Body"]
    if tope is None:
        return cuerpo.read()
    try:
        largo = respuesta.get("ContentLength")
        if isinstance(largo, int) and largo > tope:
            raise ObjetoDemasiadoGrandeError(clave)
        datos = cuerpo.read(tope + 1)
        if len(datos) > tope:
            raise ObjetoDemasiadoGrandeError(clave)
        return datos
    finally:
        cuerpo.close()


def subir_resultado(
    cliente: BaseClient, bucket: str, exportacion_id: str, resultado: ResultadoExportacion
) -> list[dict]:
    """Sube cada archivo a `exportaciones/<exportacionId>/<ruta>` y devuelve su descripción.

    Es lo que va en `atlas.contratos.exportacion_lista`.

    La salida es determinista: subir otra vez la misma exportación deja los mismos bytes en las mismas claves.
    """
    objetos = []
    for a in resultado.archivos:
        clave = f"exportaciones/{exportacion_id}/{a.ruta}"
        tipo = tipo_mime(a.ruta)
        subir(cliente, bucket, clave, a.datos, tipo, a.sha256)
        objetos.append(
            {
                "clave": clave,
                "ruta": a.ruta,
                "formato": a.formato.value,
                "bytes": len(a.datos),
                "sha256": a.sha256,
                "tipo": tipo,
            }
        )
    return objetos
