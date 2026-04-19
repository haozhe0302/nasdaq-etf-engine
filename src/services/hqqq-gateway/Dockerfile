# syntax=docker/dockerfile:1.7
#
# hqqq-gateway — REST + SignalR serving gateway.
# Native /api/system/health aggregator scrapes each Phase 2 service's
# /healthz/ready (URLs configured via Gateway:Health:Services:*:BaseUrl).
#
# Build context: REPO ROOT.
# Build:
#   docker build -f src/services/hqqq-gateway/Dockerfile -t hqqq-gateway:dev .

# ── build ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/building-blocks/ ./src/building-blocks/
COPY src/services/hqqq-gateway/ ./src/services/hqqq-gateway/

RUN dotnet restore "src/services/hqqq-gateway/hqqq-gateway.csproj"
RUN dotnet publish "src/services/hqqq-gateway/hqqq-gateway.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ── runtime ──────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

RUN useradd -u 10001 -r -s /usr/sbin/nologin app \
 && mkdir -p /app \
 && chown -R app:app /app

WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./

USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "hqqq-gateway.dll"]
