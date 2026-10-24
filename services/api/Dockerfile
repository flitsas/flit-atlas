# atlas-api y atlas-migrador (HU 1.8). Se construye desde la raíz del repo:
#   docker build -f services/api/Dockerfile --target api -t atlas-api .
#   docker build -f services/api/Dockerfile --target migrador -t atlas-migrador .
# Imágenes base fijadas por digest. La final es «chiseled» (sin shell ni gestor de paquetes) con ICU y tzdata
# («extra»), porque Atlas usa la cultura es-CO y la hora de Bogotá; corre como el usuario sin privilegios «app».

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS compilar
WORKDIR /src

# Primero lo que define las dependencias, para que la restauración quede en caché mientras no cambien.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY services/api/src/Flit.Atlas.ServiceDefaults/Flit.Atlas.ServiceDefaults.csproj services/api/src/Flit.Atlas.ServiceDefaults/
COPY services/api/src/Flit.Atlas.Plataforma/Flit.Atlas.Plataforma.csproj services/api/src/Flit.Atlas.Plataforma/
COPY services/api/src/Flit.Atlas.Identidad/Flit.Atlas.Identidad.csproj services/api/src/Flit.Atlas.Identidad/
# Módulos funcionales y la composición que los registra (ATLAS-ADR-0026): un COPY por módulo nuevo.
COPY services/api/src/Flit.Atlas.Modulos.Catalogo/Flit.Atlas.Modulos.Catalogo.csproj services/api/src/Flit.Atlas.Modulos.Catalogo/
COPY services/api/src/Flit.Atlas.Modulos.Contratos/Flit.Atlas.Modulos.Contratos.csproj services/api/src/Flit.Atlas.Modulos.Contratos/
COPY services/api/src/Flit.Atlas.Modulos.Equipo/Flit.Atlas.Modulos.Equipo.csproj services/api/src/Flit.Atlas.Modulos.Equipo/
COPY services/api/src/Flit.Atlas.Modulos.Hola/Flit.Atlas.Modulos.Hola.csproj services/api/src/Flit.Atlas.Modulos.Hola/
COPY services/api/src/Flit.Atlas.Modulos.Soporte/Flit.Atlas.Modulos.Soporte.csproj services/api/src/Flit.Atlas.Modulos.Soporte/
COPY services/api/src/Flit.Atlas.Composicion/Flit.Atlas.Composicion.csproj services/api/src/Flit.Atlas.Composicion/
COPY services/api/src/Flit.Atlas.Api/Flit.Atlas.Api.csproj services/api/src/Flit.Atlas.Api/
COPY services/api/src/Flit.Atlas.Migrador/Flit.Atlas.Migrador.csproj services/api/src/Flit.Atlas.Migrador/
RUN dotnet restore services/api/src/Flit.Atlas.Api/Flit.Atlas.Api.csproj \
 && dotnet restore services/api/src/Flit.Atlas.Migrador/Flit.Atlas.Migrador.csproj

COPY services/api/src/ services/api/src/
ARG VERSION=0.0.0
RUN dotnet publish services/api/src/Flit.Atlas.Api/Flit.Atlas.Api.csproj -c Release --no-restore \
      -o /salida/api /p:Version=${VERSION} /p:UseAppHost=false \
 && dotnet publish services/api/src/Flit.Atlas.Migrador/Flit.Atlas.Migrador.csproj -c Release --no-restore \
      -o /salida/migrador /p:Version=${VERSION} /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra@sha256:9e3919aff68057f3e45125c5255a71d3b5f40364e45ede883348688851b9f0df AS base
ENV DOTNET_EnableDiagnostics=0 \
    TZ=America/Bogota
USER app
WORKDIR /app

FROM base AS migrador
COPY --from=compilar /salida/migrador ./
ENTRYPOINT ["dotnet", "Flit.Atlas.Migrador.dll"]

FROM base AS api
COPY --from=compilar /salida/api ./
# El puerto lo fija ASPNETCORE_URLS en el ConfigMap del ambiente (F0.3: 5031 en qa, 6031 en pdn).
ENTRYPOINT ["dotnet", "Flit.Atlas.Api.dll"]
