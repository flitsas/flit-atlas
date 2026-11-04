# atlas-docs (HU 4.5, ATLAS-ADR-0042). Se construye desde la raíz del repo:
#   docker build -f services/docs/Dockerfile -t atlas-docs .
# Pruebas dentro de la imagen (con Pango, como en el CI):
#   docker build -f services/docs/Dockerfile --target pruebas -t atlas-docs-pruebas . && docker run --rm atlas-docs-pruebas
# Imágenes base fijadas por digest. WeasyPrint necesita Pango y HarfBuzz (librerías del sistema) y una fuente: DejaVu.
# La final corre como un usuario sin privilegios y sin las herramientas de compilación.

# python 3.13.15-slim-trixie, publicada el 25/09/2026 (≥ 2 semanas). uv 0.11.16, publicada el 21/05/2026.
FROM python:3.13.15-slim-trixie@sha256:7c61056e61ac89e852de05f3dc6fa51a6dd2181797bceed46aa725dd7cb2cd3b AS base
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    TZ=America/Bogota
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      libpango-1.0-0 libpangoft2-1.0-0 libharfbuzz-subset0 fonts-dejavu-core tzdata \
 && rm -rf /var/lib/apt/lists/*

FROM base AS compilar
COPY --from=ghcr.io/astral-sh/uv:0.11.16@sha256:440fd6477af86a2f1b38080c539f1672cd22acb1b1a47e321dba5158ab08864d /uv /usr/local/bin/uv
ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never
WORKDIR /app
# Primero las dependencias (capa en caché mientras no cambie el lockfile).
COPY services/docs/pyproject.toml services/docs/uv.lock ./
RUN uv sync --locked --no-dev --no-install-project
COPY services/docs/src/ src/
RUN uv sync --locked --no-dev --no-editable

FROM compilar AS pruebas
RUN uv sync --locked --no-editable
COPY services/docs/tests/ tests/
COPY contracts/ /contracts/
ENV ATLAS_DOCS_EXIGIR_PDF=1
CMD ["uv", "run", "--locked", "--no-sync", "pytest", "-q"]

FROM base AS docs
RUN useradd --system --uid 10001 --no-create-home atlas
COPY --from=compilar /app/.venv /app/.venv
ENV PATH=/app/.venv/bin:$PATH
USER 10001
EXPOSE 4035
CMD ["sh", "-c", "exec uvicorn --factory atlas_docs.main:crear_app --host 0.0.0.0 --port ${ATLAS_DOCS_PORT:-4035}"]
