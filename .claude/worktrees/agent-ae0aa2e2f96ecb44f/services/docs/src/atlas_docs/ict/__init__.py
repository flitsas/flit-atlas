"""Enriquecimiento del contrato de ICT de FLIT 2 con el Excel (HU 3.8, ATLAS-ADR-0037).

La base es `contracts/openapi/core-ict.v1.yaml` del repo `flit`; el Excel de contratos ICT lo enriquece y la salida
es un solo OpenAPI 3.1 con una URL base por ambiente.

Uso: `uv run python -m atlas_docs.ict --excel <ruta del Excel> --base-flit <ruta del repo flit>`.
"""
