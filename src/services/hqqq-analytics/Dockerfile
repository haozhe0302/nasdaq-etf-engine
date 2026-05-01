# syntax=docker/dockerfile:1.7
#
# hqqq-analytics — one-shot Timescale report job.
#
# This image is NOT meant to be a long-running service. The intended
# invocation is `docker compose --profile analytics run --rm hqqq-analytics`
# with Analytics__StartUtc / Analytics__EndUtc set on the host. The process
# reads Timescale, prints a summary, optionally writes an artifact, then
# exits. Exit codes: 0 success (incl. empty window), 1 job failure, 2
# unsupported Analytics:Mode.
#
# A management host is still registered (mainly so a long-running mode
# could expose /healthz/* in the future); compose does not wire a
# healthcheck because the exit code is the signal.
#
# Build context: REPO ROOT.
# Build:
#   docker build -f src/services/hqqq-analytics/Dockerfile -t hqqq-analytics:dev .

# ── build ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/building-blocks/ ./src/building-blocks/
COPY src/services/hqqq-analytics/ ./src/services/hqqq-analytics/

RUN dotnet restore "src/services/hqqq-analytics/hqqq-analytics.csproj"
RUN dotnet publish "src/services/hqqq-analytics/hqqq-analytics.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ── runtime ──────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /app /artifacts \
 && chown -R app:app /app /artifacts

WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./

USER $APP_UID

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    Management__Port=8081 \
    Management__BindAddress=0.0.0.0

EXPOSE 8081

ENTRYPOINT ["dotnet", "hqqq-analytics.dll"]
