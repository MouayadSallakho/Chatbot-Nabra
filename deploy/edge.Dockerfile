# syntax=docker/dockerfile:1
# OmniAI edge: TLS termination, the Angular SPA as static files, and the reverse
# proxy to the API instances. Build from the repository root:
#   docker build -f deploy/edge.Dockerfile -t omniai-edge:<version> .
#
# Certificates are NOT in the image: they are mounted at /etc/nginx/certs.
# Runs as the unprivileged nginx user on 8080 (HTTP redirect) and 8443 (HTTPS).

FROM node:24-alpine AS spa
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build
COPY deploy/edge/csp-hashes.mjs /csp-hashes.mjs
RUN node /csp-hashes.mjs dist/omniai-admin/browser/index.html > /spa-csp.conf

FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
USER root
RUN rm -f /etc/nginx/conf.d/default.conf
COPY deploy/edge/nginx.conf /etc/nginx/nginx.conf
COPY deploy/edge/omniai-proxy.conf /etc/nginx/omniai-proxy.conf
COPY --chmod=755 deploy/edge/readiness-watcher.sh /usr/local/bin/omniai-readiness-watcher
COPY --chmod=755 deploy/edge/40-omniai-readiness.sh /docker-entrypoint.d/40-omniai-readiness.sh
COPY deploy/edge/templates/ /etc/nginx/templates/
COPY --from=spa /spa-csp.conf /etc/nginx/spa-csp.conf
COPY --from=spa /app/dist/omniai-admin/browser/ /usr/share/nginx/html/
USER 101
ENV NGINX_ENVSUBST_FILTER=^OMNIAI_ \
    OMNIAI_PUBLIC_HTTPS_PORT_SUFFIX=""
EXPOSE 8080 8443
