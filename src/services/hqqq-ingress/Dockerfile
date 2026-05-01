# syntax=docker/dockerfile:1.7
#
# hqqq-ingress — Tiingo ingest worker (no primary HTTP surface).
# Management host serves /healthz/* and /metrics on Management:Port (8081).
#
# Build context: REPO ROOT.
# Build:
#   docker build -f src/services/hqqq-ingress/Dockerfile -t hqqq-ingress:dev .

# ── build ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/building-blocks/ ./src/building-blocks/
COPY src/services/hqqq-ingress/ ./src/services/hqqq-ingress/

RUN dotnet restore "src/services/hqqq-ingress/hqqq-ingress.csproj"
RUN dotnet publish "src/services/hqqq-ingress/hqqq-ingress.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ── runtime ──────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /app \
 && chown -R app:app /app

WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./

USER $APP_UID

# Worker has no primary Kestrel surface; the management host owns the only
# port. Compose must set Management__BindAddress=0.0.0.0 so the gateway
# aggregator can scrape across the docker network.
ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    Management__Port=8081 \
    Management__BindAddress=0.0.0.0

EXPOSE 8081

ENTRYPOINT ["dotnet", "hqqq-ingress.dll"]
