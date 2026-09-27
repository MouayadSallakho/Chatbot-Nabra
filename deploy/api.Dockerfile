# syntax=docker/dockerfile:1
# OmniAI API. Build from the repository root:
#   docker build -f deploy/api.Dockerfile -t omniai-api:<version> .
#
# Runtime image: ASP.NET Core on Debian, non-root (the image's built-in `app`
# user), no SDK, no shell tooling added, read-only root filesystem friendly: the
# only writable paths are the report volume and /tmp (see compose).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ backend/
RUN --mount=type=cache,id=omniai-nuget,target=/root/.nuget/packages \
    dotnet restore backend/src/OmniAI.Api/OmniAI.Api.csproj \
 && dotnet publish backend/src/OmniAI.Api/OmniAI.Api.csproj \
      -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /var/lib/omniai/reports && chown "$APP_UID" /var/lib/omniai/reports
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OmniAI.Api.dll"]
