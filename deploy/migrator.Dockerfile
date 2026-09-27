# syntax=docker/dockerfile:1
# OmniAI schema migration job. Run ONCE per release, before any API of that
# release starts, as the owner role -- never from the API, never over HTTP.
#   docker build -f deploy/migrator.Dockerfile -t omniai-migrator:<version> .
#
# The bundle is compiled from the same EF model as the API and applies the
# generated migrations in order, each in its own transaction. Its connection is
# read from OMNIAI_DESIGN_CONNECTION (the design-time factory), so no credential
# appears on a command line.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ backend/
WORKDIR /src/backend
RUN --mount=type=cache,id=omniai-nuget,target=/root/.nuget/packages \
    dotnet tool restore \
 && dotnet restore src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj -r linux-x64 \
 && dotnet dotnet-ef migrations bundle \
      -p src/OmniAI.Infrastructure -s src/OmniAI.Infrastructure \
      --configuration Release --self-contained -r linux-x64 \
      -o /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /migrator
COPY --from=build /out/efbundle ./efbundle
# A single-file bundle unpacks itself before running; the root filesystem is
# read-only in production, so it unpacks into the /tmp tmpfs.
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/efbundle
USER $APP_UID
ENTRYPOINT ["./efbundle"]
