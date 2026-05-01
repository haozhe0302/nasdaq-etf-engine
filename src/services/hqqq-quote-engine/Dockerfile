# syntax=docker/dockerfile:1.7
#
# hqqq-quote-engine — Kafka consumer + iNAV compute worker.
# Management host serves /healthz/* and /metrics on Management:Port (8081).
# Crash-recovery checkpoint lives at /data/quote-engine/checkpoint.json
# (path overridable via QuoteEngine__CheckpointPath). Compose maps a named
# volume here so engine state survives container restarts.
#
# Build context: REPO ROOT.
# Build:
#   docker build -f src/services/hqqq-quote-engine/Dockerfile -t hqqq-quote-engine:dev .

# ── build ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/building-blocks/ ./src/building-blocks/
COPY src/services/hqqq-quote-engine/ ./src/services/hqqq-quote-engine/

RUN dotnet restore "src/services/hqqq-quote-engine/hqqq-quote-engine.csproj"
RUN dotnet publish "src/services/hqqq-quote-engine/hqqq-quote-engine.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ── runtime ──────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /app /data/quote-engine \
 && chown -R app:app /app /data

WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./

USER $APP_UID

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    Management__Port=8081 \
    Management__BindAddress=0.0.0.0 \
    QuoteEngine__CheckpointPath=/data/quote-engine/checkpoint.json

VOLUME ["/data/quote-engine"]
EXPOSE 8081

ENTRYPOINT ["dotnet", "hqqq-quote-engine.dll"]
