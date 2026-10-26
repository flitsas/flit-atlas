# atlas-web (HU 1.8): build de Vite servido por nginx sin privilegios. Se construye desde apps/web:
#   docker build -t atlas-web apps/web
# En el contenedor: ATLAS_WEB_PORT (5032 en qa, 6032 en pdn), ATLAS_API_URL (p. ej. http://atlas-api:5031) y
# ATLAS_VISOR_CONEXIONES (hosts de QA y sandbox a los que llama «Probar» del visor de contratos, ATLAS-ADR-0039) y
# ATLAS_HSTS (Strict-Transport-Security; vacío para no enviarla, p. ej. al probar la imagen en local sin TLS, ATLAS-ADR-0048)
# y ATLAS_S3_PUBLICO (origen público del S3 que firma las descargas, p. ej. https://s3.ejemplo.com; va en connect-src).
# Imágenes base fijadas por digest.

FROM node:22.23.4-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914 AS compilar
WORKDIR /web
RUN corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
RUN pnpm build

FROM nginxinc/nginx-unprivileged:1.30-alpine@sha256:f5116ed4606f3eb015dd4a8e28cb9b351476dc0ad4bc6e9f93b19cec130cdf03
ENV ATLAS_WEB_PORT=5032 \
    ATLAS_API_URL=http://atlas-api:5031 \
    ATLAS_VISOR_CONEXIONES="https://*.qa.flitsas.online https://*.qa.flitsas.com" \
    ATLAS_HSTS="max-age=31536000; includeSubDomains" \
    ATLAS_S3_PUBLICO="" \
    NGINX_ENVSUBST_FILTER=^ATLAS_
COPY deploy/atlas-proxy.conf deploy/atlas-seguridad.conf deploy/atlas-seguridad-visor.conf /etc/nginx/
COPY deploy/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=compilar /web/dist /usr/share/nginx/html
EXPOSE 5032
