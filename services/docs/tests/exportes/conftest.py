"""Exportaciones de los paquetes de prueba, una vez por sesión (las del contrato de Atlas tardan)."""

import pytest
from apoyo_exportes import ATLAS, ICT, formatos_disponibles, paquete

from atlas_docs.exportes import PaqueteExportar, ResultadoExportacion, exportar


@pytest.fixture(scope="session")
def paquete_ict() -> PaqueteExportar:
    return paquete(("core-ict", "1.1.0", ICT))


@pytest.fixture(scope="session")
def paquete_atlas() -> PaqueteExportar:
    return paquete(("atlas", "1.0.0", ATLAS))


@pytest.fixture(scope="session")
def ict(paquete_ict: PaqueteExportar) -> ResultadoExportacion:
    return exportar(paquete_ict, formatos_disponibles())


@pytest.fixture(scope="session")
def atlas(paquete_atlas: PaqueteExportar) -> ResultadoExportacion:
    return exportar(paquete_atlas, formatos_disponibles())
