# HQQQ Runbook (Local Run + Smoke Test)

This document is the single place for setup/startup commands, validation
commands, and shutdown procedures.

---

## 1) Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0+ |
| Node.js | 22 LTS |
| npm | 10.x |

Required API keys in `.env`:
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
