"""Acceso al almacenamiento S3 (MinIO en dev y qa, Contabo en pdn)."""

import boto3
from botocore.client import BaseClient
from botocore.config import Config as BotoConfig
from botocore.exceptions import ClientError

from atlas_docs.config import Configuracion


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
