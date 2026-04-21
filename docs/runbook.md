# HQQQ Runbook (Local Run + Smoke Test)

This document is the single place for setup/startup commands, validation
commands, and shutdown procedures across both phases.

## 0) Read this first

The repo is in a transitional state. Pick the path that matches what
you want to do and jump directly to the relevant section.

| I want to … | Go here |
|-------------|---------|
| Run the **Phase 1 reference** monolith locally (still backs the public demo) | §§1–10 of this file |
| Run the **Phase 2 stack locally on host** (`dotnet run` per service) | §11 of this file, plus [`phase2/local-dev.md`](phase2/local-dev.md) for the deeper walkthrough |
| Run the **containerized Phase 2 app tier** (D3) | §12 of this file, `scripts/phase2-up.{ps1,sh}` |
| Run the **multi-gateway replica-smoke** (D5) | §13 of this file, `scripts/replica-smoke-up.{ps1,sh}` |
| Deploy the **Phase 2 app tier to Azure Container Apps** (D4) | §14 of this file, plus [`phase2/azure-deploy.md`](phase2/azure-deploy.md) |
| Walk a release through pre/post-deploy gates | [`phase2/release-checklist.md`](phase2/release-checklist.md) |
| Roll back a Phase 2 release | [`phase2/rollback.md`](phase2/rollback.md) |
| Look up per-service configuration surface | [`phase2/config-matrix.md`](phase2/config-matrix.md) |
| Cross-replica health check URLs | §15 of this file |
| Expected degraded behaviors / failure-mode triage | §§16–17 of this file |

Phase 1 sections (§§1–10) and Phase 2 sections (§§11–17) are
self-contained; you do not need to run both.

---

## 1) Prerequisites — Phase 1 (legacy / public-demo) local run path

> **Sections §§1–10 describe the Phase 1 modular monolith (`src/hqqq-api`).**
> This is the legacy reference path that still backs the public live demo
> linked at the top of the root [`README.md`](../README.md). It is **not**
> the default Phase 2 self-sufficient runtime path. If you want to run the
> current Phase 2 services locally, **skip to §11**; for the containerized
> Phase 2 app tier, skip to §12. Phase 1 sections (§§1–10) and Phase 2
> sections (§§11–17) are self-contained — you do not need to run both.

| Tool | Version |
|---|---|
| .NET SDK | 10.0+ |
| Node.js | 22 LTS |
| npm | 10.x |

Required API keys in `.env` for the legacy monolith path:
- `TIINGO_API_KEY`
- `ALPHA_VANTAGE_API_KEY`

---

## 2) Environment setup

### PowerShell

```powershell
Copy-Item .env.example .env
```

### Bash

```bash
cp .env.example .env
```

Edit `.env` and replace placeholder API keys.

---

## 3) Build and test

### Docker infra setup

```bash
docker compose pull
docker compose up -d
docker compose ps
```

Check container logs when needed:

```bash
docker compose logs -f
```

### Backend build

```bash
dotnet build src/hqqq-api
```

### Backend tests

```bash
dotnet test src/hqqq-api.tests --verbosity normal
```

### hqqq-api Docker image (version embedded in `/api/system/health`)

The image bakes **MSBuild `InformationalVersion`** into the DLL (API returns it; the UI prefixes `v`). **ACR image tags** for releases use **`vX.Y.Z`** so the registry matches what you see on the System page (`v1.0.x`).

| Mode | Behavior |
|------|----------|
| **CI (GitHub Actions)** | On push to `main` that touches `src/hqqq-api/**` (or this workflow), reads the highest `X.Y.Z` among ACR tags, **patch +1**, pushes `:vX.Y.Z` and `:latest`. Jobs in the same repo are **queued** so two pushes do not reuse the same version number. |
| **Explicit** | `-Version 1.0.3` → `InformationalVersion=1.0.3`, image `:v1.0.3` |
| **Bump patch (git)** | `-BumpPatch` → latest `v*.*.*` **git** tag, patch +1 (no tags → `0.0.1`), image `:vX.Y.Z` |
| **Default (local)** | HEAD on `v*` tag → that version, `:vX.Y.Z`; else dev `0.0.0+<sha>`, image `:0.0.0-<sha>` |

```powershell
# Manual release aligned with ACR + System page (example 1.0.5)
.\scripts\build-hqqq-api-docker.ps1 -Version 1.0.5 -Push

# Or bump from latest v* tag in git, then push
.\scripts\build-hqqq-api-docker.ps1 -BumpPatch -Push

# Local dev image (not a semver release tag)
.\scripts\build-hqqq-api-docker.ps1
```

Raw `docker build` (must pass both args; tag as `vX.Y.Z` if you want consistency with CI):

```powershell
docker build -f .\src\hqqq-api\Dockerfile `
  --build-arg VERSION=1.0.3 `
  --build-arg INFORMATIONAL_VERSION=1.0.3 `
  -t acrhqqqmvp001.azurecr.io/hqqq-api:v1.0.3 `
  .\src\hqqq-api
```

**CI:** `.github/workflows/hqqq-api-docker.yml` — requires `ACR_USERNAME` / `ACR_PASSWORD` with permission to **list tags** (pull scope) and **push**. Configure the Azure Web App container to use **`latest`** (or a fixed `:vX.Y.Z`) so each new image is what runs; CI does not redeploy the Web App by itself—enable **Continuous Deployment** from ACR or refresh the container manually after a push.

### Frontend install + build

```bash
cd src/hqqq-ui
npm install
npm run build
```

---

## 4) Start services (local)

Open two terminals from repo root.

### Terminal A: backend

```bash
dotnet run --project src/hqqq-api
```

Expected startup signal:
- `Now listening on: http://localhost:5015`

Swagger (dev): <http://localhost:5015/swagger>

### Terminal B: frontend

```bash
cd src/hqqq-ui
npm install
npm run dev
```

Frontend: <http://localhost:5173>

---

## 5) API smoke checks

Run from another terminal while backend is running.

### PowerShell

```powershell
$base = "http://localhost:5015"
$endpoints = @(
  "/api/system/health",
  "/api/basket/current",
  "/api/marketdata/status",
  "/api/quote",
  "/api/constituents",
  "/api/history?range=1D",
  "/metrics"
)

foreach ($ep in $endpoints) {
  try {
    $resp = Invoke-WebRequest -Uri "$base$ep" -Method GET
    Write-Host "$ep -> HTTP $($resp.StatusCode)"
  } catch {
    $code = $_.Exception.Response.StatusCode.value__
    Write-Host "$ep -> HTTP $code"
  }
}
```

### Bash

```bash
for endpoint in \
  /api/system/health \
  /api/basket/current \
  /api/marketdata/status \
  /api/quote \
  /api/constituents \
  "/api/history?range=1D" \
  /metrics; do
  status=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:5015$endpoint")
  echo "$endpoint -> HTTP $status"
done
```

Expected:
- `/api/system/health`, `/api/marketdata/status`, `/metrics`: typically `200`
- `/api/quote`, `/api/constituents`: may return `503` before pricing bootstrap completes
- `/api/basket/current`: may return `503` before first successful basket load

---

## 6) Frontend smoke checks

Open <http://localhost:5173> and verify:

| Route | Expected result |
|---|---|
| `/market` | iNAV card, chart, movers, feed freshness visible and updating |
| `/constituents` | Holdings table populated, concentration/quality metrics present |
| `/history` | Range selector works, history chart + tracking stats render |
| `/system` | Health cards and runtime metrics render without errors |

SignalR check:
- Browser DevTools Network tab shows `/hubs/market` WebSocket connection on Market page

---

## 7) Optional benchmark workflow

Enable recording:

```bash
HQQQ_RECORDING_ENABLED=true dotnet run --project src/hqqq-api
```

Generate report:

```bash
dotnet run --project src/hqqq-bench -- --input data/recordings/YYYY-MM-DD
```

---

## 8) Shutdown

- Stop backend/frontend terminals with `Ctrl+C`

If you started Docker infra:

```bash
docker compose down        # stop containers, keep volumes
docker compose down -v     # stop containers and remove volumes
```

---

## 9) Container deployments

- `QuoteEngine:CheckpointPath` (default `./data/quote-engine/checkpoint.json`) must map to a persistent volume in container deployments so checkpoint state survives restarts.

---

## 10) Live demo endpoints

- Frontend live: <https://delightful-dune-08a7a390f.1.azurestaticapps.net/>
- Backend live health: <https://app-hqqq-api-mvp-cdgffghwf8c4hgdh.eastus-01.azurewebsites.net/api/system/health>

---

## 11) Phase 2 local runbook (services split)

This section covers the Phase 2 services. Phase 2 is **self-sufficient**:
`hqqq-ingress` opens the real Tiingo IEX websocket, `hqqq-reference-data`
owns the active basket and Phase-2-native corporate-action adjustment,
`hqqq-quote-engine` runs the iNAV compute and live `QuoteUpdate`
publish, and `hqqq-gateway` serves REST + SignalR with a native
`/api/system/health` aggregator. The legacy `hqqq-api` monolith is **not**
in this runtime path — it stays in the repo as reference code and still
backs the public live demo Web App on Azure App Service.

### 11.1 Infra startup

```bash
docker compose up -d
docker compose ps
```

Wait for `db`, `cache`, and `kafka` to report healthy. Kafka has a 30-second
start period.

### 11.2 Bootstrap Kafka topics

Topic auto-creation is **disabled** in the broker config. Topics must be
created explicitly (idempotent):

```powershell
.\scripts\bootstrap-kafka-topics.ps1
```

```bash
./scripts/bootstrap-kafka-topics.sh
```

Expected partitions:

| Topic | Partitions | Cleanup |
|-------|-----------:|---------|
| `market.raw_ticks.v1` | 3 | delete |
| `market.latest_by_symbol.v1` | 3 | compact |
| `refdata.basket.active.v1` | 1 | compact |
| `refdata.basket.events.v1` | 1 | delete |
| `pricing.snapshots.v1` | 1 | delete |
| `ops.incidents.v1` | 1 | delete |

### 11.3 Start Phase 2 services (order matters)

Each command in its own terminal.

```powershell
dotnet build Hqqq.sln

dotnet run --project src/services/hqqq-reference-data
dotnet run --project src/services/hqqq-quote-engine
dotnet run --project src/services/hqqq-ingress         # requires Tiingo__ApiKey (real Tiingo IEX websocket; fail-fast on missing/placeholder key)
dotnet run --project src/services/hqqq-persistence
dotnet run --project src/services/hqqq-gateway
```

Recommended gateway mode for a C-phase end-to-end smoke:

```powershell
$env:Gateway__DataSource        = "stub"
$env:Gateway__Sources__Quote    = "redis"
$env:Gateway__Sources__Constituents = "redis"
$env:Gateway__Sources__History  = "timescale"
$env:Gateway__BasketId          = "HQQQ"
$env:Redis__Configuration       = "localhost:6379"
$env:Timescale__ConnectionString = "Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme"
dotnet run --project src/services/hqqq-gateway
```

### 11.4 Gateway smoke checks

Default gateway port is `5030`.

```powershell
$base = "http://localhost:5030"
foreach ($ep in @("/healthz/live","/healthz/ready","/api/history?range=1D")) {
  try {
    $resp = Invoke-WebRequest -Uri "$base$ep" -Method GET
    Write-Host "$ep -> HTTP $($resp.StatusCode)"
  } catch {
    $code = $_.Exception.Response.StatusCode.value__
    Write-Host "$ep -> HTTP $code"
  }
}
```

```bash
base="http://localhost:5030"
for ep in /healthz/live /healthz/ready "/api/history?range=1D"; do
  status=$(curl -s -o /dev/null -w "%{http_code}" "$base$ep")
  echo "$ep -> HTTP $status"
done
```

Expected in C2 Timescale mode:

| Endpoint | Expected | Notes |
|---|---|---|
| `/healthz/live` | `200` | Always, if process is up |
| `/healthz/ready` | `200` | Degraded-but-ready is fine |
| `/api/history?range=1D` (populated) | `200` | Real series data |
| `/api/history?range=1D` (no data yet) | `200` | Render-safe empty payload: `pointCount=0`, `series=[]`, stable 21-bucket `distribution` |
| `/api/history?range=XYZ` (bad range) | `400` | `{"error":"history_range_unsupported","range":"XYZ","supported":[...]}` |
| `/api/history` (Timescale unreachable) | `503` | `{"error":"history_unavailable",...}` — never silently falls back |

An empty history window is the expected state on a fresh local run before
`hqqq-persistence` has written any rows.

You can also run the consolidated helper:

```powershell
.\scripts\phase2-smoke.ps1
```

```bash
./scripts/phase2-smoke.sh
```

### 11.5 Analytics report (on-demand one-shot)

`hqqq-analytics` is not a long-running service. It reads persisted
Timescale data for an explicit basket + UTC window, logs a summary, and
exits. Run it only when you want a report.

```powershell
$env:Timescale__ConnectionString = "Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme"
$env:Analytics__Mode      = "report"
$env:Analytics__BasketId  = "HQQQ"
$env:Analytics__StartUtc  = "2026-04-17T00:00:00Z"
$env:Analytics__EndUtc    = "2026-04-18T00:00:00Z"
$env:Analytics__EmitJsonPath = "artifacts/hqqq-report.json"   # optional
$env:Analytics__IncludeRawTickAggregates = "false"            # optional

dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj
```

Exit codes: `0` success (including empty window), `1` job failure,
`2` unsupported `Analytics:Mode`. An empty window is not a failure — the
host logs a single `WARN`, emits `hasData=false` / zeroed numeric fields,
and exits with `0`.

### 11.6 Intentionally deferred

- Provider-specific holdings scrape adapters (Schwab / StockAnalysis /
  AlphaVantage / Nasdaq) ported behind `IHoldingsSource` —
  `hqqq-reference-data` runs on `File`/`Http` drops + the deterministic
  fallback seed today; the legacy monolith retains the scrapers as
  reference only.
- Wider corp-action coverage: dividends, spin-offs, mergers, cross-exchange
  moves, ISIN/CUSIP-level remaps. Phase 2 implements forward / reverse
  splits, ticker renames, constituent transition detection, and
  scale-factor continuity — explicit and narrow.
- Replay / anomaly / backfill in `hqqq-analytics`.
- HA topologies for Kafka / Redis / Timescale themselves; multi-instance
  quote-engine / persistence / ingress / reference-data (D5 only
  duplicates the gateway).
- Kubernetes app-tier operationalization — Phase 3.

D-phase items already shipped (no longer "deferred"):

- D1 — gateway-native `/api/system/health` aggregator (default).
- D2 — live `QuoteUpdate` fan-out via Redis pub/sub
  `hqqq:channel:quote-update`.
- D3 — containerized Phase 2 app tier (`docker-compose.phase2.yml`).
- D4 — Azure Container Apps deploy assets under `infra/azure/`.
- D5 — multi-gateway replica smoke
  (`docker-compose.replica-smoke.yml`, `tests/Hqqq.Gateway.ReplicaSmoke/`).

---

## 12) Containerized Phase 2 app tier (D3)

In addition to the host-`dotnet run` flow in §11, the entire Phase 2 app
tier can be run as containers on top of the existing infra compose.

### 12.1 Bring-up

```powershell
.\scripts\phase2-up.ps1
.\scripts\bootstrap-kafka-topics.ps1
.\scripts\phase2-smoke.ps1
```

```bash
./scripts/phase2-up.sh
./scripts/bootstrap-kafka-topics.sh
./scripts/phase2-smoke.sh
```

`phase2-up.*` is a thin wrapper around:

```bash
docker compose -f docker-compose.yml -f docker-compose.phase2.yml up -d --build
```

### 12.2 Container DNS, ports, and health

| Service | Container DNS:port | Host port | Health endpoint |
|---------|--------------------|-----------|-----------------|
| `hqqq-reference-data` | `hqqq-reference-data:8080` | `5020` | `GET /healthz/ready` |
| `hqqq-ingress`        | `hqqq-ingress:8081`        | `5081` | `GET /healthz/ready` |
| `hqqq-quote-engine`   | `hqqq-quote-engine:8081`   | `5082` | `GET /healthz/ready` |
| `hqqq-persistence`    | `hqqq-persistence:8081`    | `5083` | `GET /healthz/ready` |
| `hqqq-gateway`        | `hqqq-gateway:8080`        | `5030` | `GET /healthz/ready` |
| `hqqq-analytics` (profile=`analytics`) | `hqqq-analytics:8081` | `5084` | n/a — exit code is the signal |

Workers expose only their management host (port 8081) inside the
container, configured with `Management__BindAddress=0.0.0.0` so the
gateway aggregator can reach `/healthz/ready` and `/metrics` across
the docker network.

### 12.3 One-shot analytics in compose

```powershell
$env:Analytics__StartUtc = '2026-04-17T00:00:00Z'
$env:Analytics__EndUtc   = '2026-04-18T00:00:00Z'
docker compose -f docker-compose.yml -f docker-compose.phase2.yml `
    --profile analytics run --rm hqqq-analytics
```

### 12.4 Tear-down

```powershell
.\scripts\phase2-down.ps1                      # stop containers, keep volumes
.\scripts\phase2-down.ps1 -RemoveVolumes       # also drop Timescale/Redis/checkpoint volumes
.\scripts\phase2-down.ps1 -IncludeReplicaSmoke # also tear down the D5 overlay
```

```bash
./scripts/phase2-down.sh
./scripts/phase2-down.sh --remove-volumes
./scripts/phase2-down.sh --include-replica-smoke
```

`-RemoveVolumes` / `--remove-volumes` is destructive — Timescale data,
Redis state, and the `quote_engine_data` checkpoint volume will be lost.

---

## 13) Multi-gateway replica smoke (D5)

D5 stands up a second gateway replica (`hqqq-gateway-b` on host port
`5031`) sharing the same Redis as the default gateway, then asserts that
both replicas receive the same live `QuoteUpdate` over the
`hqqq:channel:quote-update` Redis pub/sub channel. Scope is
gateway-replica correctness, not full HA — only the gateway is duplicated.

### 13.1 Bring-up

```powershell
.\scripts\replica-smoke-up.ps1
.\scripts\bootstrap-kafka-topics.ps1
.\scripts\replica-smoke.ps1
```

```bash
./scripts/replica-smoke-up.sh
./scripts/bootstrap-kafka-topics.sh
./scripts/replica-smoke.sh
```

### 13.2 What the harness asserts

The `Hqqq.Gateway.ReplicaSmoke` console exe:

1. probes `GET /healthz/ready` and `GET /api/quote` on both gateways
   (requires `2xx` from each);
2. opens two SignalR clients — one to `5030/hubs/market`, one to
   `5031/hubs/market` — and registers a `QuoteUpdate` handler on each;
3. publishes a single deterministic `QuoteUpdateEnvelope` to
   `hqqq:channel:quote-update` via Redis pub/sub, asserting at least 2
   subscribers received the `PUBLISH`;
4. waits for both SignalR clients to receive the same `QuoteUpdate`
   within `HQQQ_REPLICA_SMOKE_TIMEOUT_SECONDS` (default 15 s) and
   checks that `Nav`, `QuoteState`, `AsOf`, and `IsLive` round-trip
   unchanged.

Exit code `0` is the only "PASS"; everything else exits `1`.

### 13.3 Tear-down

```powershell
.\scripts\phase2-down.ps1 -IncludeReplicaSmoke
```

```bash
./scripts/phase2-down.sh --include-replica-smoke
```

---

## 14) Azure deployment (D4)

Azure Container Apps is the cloud target for Phase 2. AKS / Helm /
Kubernetes are explicitly **out of scope** for Phase 2 — they are
Phase 3 work.

The bootstrap (resource group, GitHub OIDC federated identity, role
assignments, repo + environment secrets) is documented in
[`infra/azure/README.md`](../infra/azure/README.md). The day-to-day
operator walkthrough (deploy loop, analytics on demand, container
hardening, adding a second environment) lives in
[`phase2/azure-deploy.md`](phase2/azure-deploy.md).

### 14.1 Deploy a new revision

1. Merge to `main` (or run [`phase2-images.yml`](../.github/workflows/phase2-images.yml) manually). Note the
   `vsha-...` tag from the run summary.
2. Run [`phase2-deploy.yml`](../.github/workflows/phase2-deploy.yml)
   with `image_tag=vsha-<short-sha>`. Set `what_if_only=true` first for
   a dry run.
3. Read the run summary for the gateway FQDN and the smoke commands.

The deploy is idempotent: re-running with the same `image_tag` is a
no-op for the images, and Bicep only re-applies what changed.

### 14.2 Smoke

```bash
RG=rg-hqqq-p2-demo-eus-01
GATEWAY=$(az containerapp show -g $RG -n ca-hqqq-p2-gateway-demo-01 \
  --query properties.configuration.ingress.fqdn -o tsv)

curl -fsS https://$GATEWAY/healthz/ready
curl -fsS https://$GATEWAY/api/system/health
curl -fsS https://$GATEWAY/api/quote
curl -fsS "https://$GATEWAY/api/history?range=1D"
```

### 14.3 Run analytics on demand

```bash
RG=rg-hqqq-p2-demo-eus-01
JOB=caj-hqqq-p2-analytics-demo-01

az containerapp job start \
  --name $JOB \
  --resource-group $RG \
  --env-vars \
    Analytics__StartUtc=2026-04-17T00:00:00Z \
    Analytics__EndUtc=2026-04-18T00:00:00Z

EXEC=$(az containerapp job execution list -n $JOB -g $RG --query '[0].name' -o tsv)
az containerapp job logs show -n $JOB -g $RG --execution $EXEC --container $JOB --follow
```

Exit codes: `0` success (incl. empty window), `1` failure, `2`
unsupported `Analytics:Mode`.

---

## 15) Health check URL matrix

| Mode | Process / container | `/healthz/live` | `/healthz/ready` | `/api/system/health` | `/metrics` |
|------|---------------------|-----------------|------------------|----------------------|------------|
| Host (`dotnet run`) | `hqqq-gateway` | `http://localhost:5030/healthz/live` | `http://localhost:5030/healthz/ready` | `http://localhost:5030/api/system/health` | `http://localhost:5030/metrics` |
| Host (`dotnet run`) | `hqqq-reference-data` | `http://localhost:5020/healthz/live` | `http://localhost:5020/healthz/ready` | n/a | `http://localhost:5020/metrics` |
| Host (`dotnet run`) workers (`hqqq-ingress` / `hqqq-quote-engine` / `hqqq-persistence`) | management host | `http://localhost:<mgmtPort>/healthz/live` | `http://localhost:<mgmtPort>/healthz/ready` | n/a | `http://localhost:<mgmtPort>/metrics` |
| Containerized (D3) | `hqqq-gateway` | `http://localhost:5030/healthz/live` | `http://localhost:5030/healthz/ready` | `http://localhost:5030/api/system/health` | `http://localhost:5030/metrics` |
| Containerized (D3) | workers | host ports `5020`, `5081`, `5082`, `5083` | same | n/a | same |
| Replica-smoke (D5) | `hqqq-gateway-b` | `http://localhost:5031/healthz/live` | `http://localhost:5031/healthz/ready` | `http://localhost:5031/api/system/health` | `http://localhost:5031/metrics` |
| Azure Container Apps (D4) | gateway only (external) | `https://<gatewayFqdn>/healthz/live` | `https://<gatewayFqdn>/healthz/ready` | `https://<gatewayFqdn>/api/system/health` | `https://<gatewayFqdn>/metrics` |
| Azure Container Apps (D4) | workers (internal-only) | reached by the gateway aggregator via internal CAE FQDNs | same | n/a | same |

---

## 16) Expected degraded behaviors

These are the documented, deliberate degraded responses. None of them
should be treated as a regression on their own.

| Trigger | Endpoint | Response | Notes |
|---------|----------|----------|-------|
| Redis snapshot key missing for the configured `Gateway:BasketId` | `GET /api/quote` | `503 {"error":"quote_unavailable", ...}` | Gateway never silently substitutes stub data when `Sources:Quote=redis`. Quote-engine re-populates on the next compute cycle. |
| Redis constituents key missing | `GET /api/constituents` | `503 {"error":"constituents_unavailable", ...}` | Same shape as quote; clears on next cycle. |
| Malformed Redis payload | `GET /api/quote` / `/api/constituents` | `502 {"error":"quote_malformed"}` / `{"error":"constituents_malformed"}` | Counter `hqqq.gateway.quote_updates_malformed` increments on the SignalR fan-out path. |
| Timescale unreachable | `GET /api/history?range=...` | `503 {"error":"history_unavailable", ...}` | Live serving (Redis) is unaffected. |
| Empty history window | `GET /api/history?range=1D` | `200` with render-safe empty payload (`pointCount=0`, empty `series`, 21-bucket `distribution`) | Expected on a fresh stack before persistence has written rows. |
| Unsupported `range` | `GET /api/history?range=XYZ` | `400 {"error":"history_range_unsupported", ...}` | Supported ranges: `1D`, `5D`, `1M`, `3M`, `YTD`, `1Y`. |
| Downstream worker not configured / unreachable | `GET /api/system/health` (aggregated) | `200` overall, that dependency reports `status: "unknown"` (or `"idle"` if no `BaseUrl` configured); top-level status rolls up to `degraded` | The aggregator never returns a non-200 unless the gateway itself catastrophically fails. |
| Quote-engine restart | live SignalR `/hubs/market` | momentary gap in `QuoteUpdate` events; no error to clients | Checkpoint at `QuoteEngine__CheckpointPath` rehydrates basket + scale-state on the next start. |
| Corp-action provider failure (Phase 2 `hqqq-reference-data`) | `/api/system/health` (aggregated rollup); `/api/basket/current` `adjustmentSummary` | `corporate-actions` dependency flagged via `corporate-actions-fetch` health probe and `hqqq_refdata_corp_action_fetch_errors_total`. With `ReferenceData:CorporateActions:Tiingo:Enabled=true`, the composite provider falls back to file-only with lineage `file+tiingo-degraded`; basket activation continues. | Live serving continues. |
| `hqqq-analytics` empty window | exit code | `0`, log line `WARN ... hasData=false` | Empty window is not a failure. |

---

## 17) Common failure modes & first checks

| Symptom | First check |
|---------|-------------|
| `phase2-smoke` reports `503` on `/api/quote` | `docker exec cache redis-cli EXISTS hqqq:snapshot:HQQQ` — if `0`, quote-engine has not produced yet. Tail `hqqq-quote-engine` logs. |
| `phase2-smoke` reports `503` on `/api/history` | `docker exec db psql -U admin -d hqqq -c "SELECT COUNT(*) FROM quote_snapshots;"` — if `0`, persistence has not consumed yet, or `pricing.snapshots.v1` is empty. |
| Kafka topics missing (consumer crashes on startup) | `docker exec kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list` — re-run `bootstrap-kafka-topics.{ps1,sh}`. |
| `kafka` container marked `unhealthy` | `docker compose ps`; Kafka has a 30 s start period. Wait, then re-run bootstrap. |
| Gateway returns `502 quote_malformed` | A producer wrote a non-JSON or shape-mismatched payload to Redis. Check `hqqq-quote-engine` recent deploys; metric `hqqq.gateway.quote_updates_malformed`. |
| `phase2-deploy.yml` fails on Bicep step | Read the workflow's `what-if` output; check the `phase2-demo` environment secrets exist (`KAFKA_BOOTSTRAP_SERVERS`, `KAFKA_SECURITY_PROTOCOL`, `KAFKA_SASL_MECHANISM`, `KAFKA_SASL_USERNAME`, `KAFKA_SASL_PASSWORD`, `REDIS_CONFIGURATION`, `TIMESCALE_CONNECTION_STRING`). The preflight aggregates every missing secret in one error block. |
| Container App stuck in `Provisioning` / `Failed` | `az containerapp revision list -g $RG -n <appName>` then `az containerapp logs show -g $RG -n <appName> --revision <revName>` — typical causes are image-pull failure (check ACR + UAMI `AcrPull`) or env-var validation (`IValidateOptions` failing fast). |
| `analytics` job exit code `2` | `Analytics:Mode` is unsupported. Re-run with `Analytics__Mode=report` and a valid `StartUtc`/`EndUtc`. |
| Replica-smoke fails on SignalR wait | Confirm both gateways report `2xx` on `/healthz/ready`; confirm both resolve the same Redis (`Redis__Configuration`) and same `Gateway__BasketId`; raise `HQQQ_REPLICA_SMOKE_TIMEOUT_SECONDS`. |
| `/api/system/health` aggregated payload shows `status: "unknown"` for a worker | The gateway can't reach that worker's `/healthz/ready`. Check the corresponding `Gateway:Health:Services:<Name>:BaseUrl` and that the worker's management host is bound to `0.0.0.0` (containerized workers set `Management__BindAddress=0.0.0.0`). |
| `quote-engine` checkpoint not restored after restart | `QuoteEngine:CheckpointPath` must map to a persistent volume in containers (`quote_engine_data` named volume in compose; ephemeral `/tmp` in Azure today — documented gap). |
