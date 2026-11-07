# Servidor MCP de LikeC4 para correr en el equipo de cada persona (HU 6.9, ATLAS-ADR-0056 §8). No se despliega: no tiene autenticación.
# Se construye desde esta carpeta y lee el modelo de una carpeta montada en solo lectura:
#   docker build -t atlas-likec4-mcp tools/likec4-mcp
#   docker run --rm -p 127.0.0.1:33335:33335 -v "<ruta a los .c4>:/modelo:ro" atlas-likec4-mcp
FROM node:22.23.4-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914
WORKDIR /mcp
COPY package.json package-lock.json ./
# --ignore-scripts: no baja los navegadores de Playwright; las herramientas que dibujan PNG no están disponibles aquí.
RUN npm ci --ignore-scripts --omit=dev --no-audit --no-fund
COPY probar.mjs ./
USER node
ENV LIKEC4_WORKSPACE=/modelo
EXPOSE 33335
ENTRYPOINT ["node_modules/.bin/likec4", "mcp", "--http", "/modelo"]
