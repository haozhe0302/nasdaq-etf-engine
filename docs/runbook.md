# HQQQ Runbook (Local Run + Smoke Test)

This document is the single place for setup/startup commands, validation
commands, and shutdown procedures.

---

## 1) Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 8.0+ |
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

The image bakes **MSBuild `InformationalVersion`** into the DLL. Use the PowerShell helper (repo root) so tags and semver stay consistent:

| Mode | Behavior |
|------|----------|
| **Explicit** | `-Version 1.0.3` → `InformationalVersion=1.0.3`, image `:1.0.3` |
| **Bump patch** | `-BumpPatch` → latest `v*.*.*` tag, patch +1 (no tags → `0.0.1`) |
| **Default** | HEAD exactly on `v*` tag → that version; else `0.0.0+<short-sha>` |

```powershell
# Explicit version (also tags image as 1.0.3)
.\scripts\build-hqqq-api-docker.ps1 -Version 1.0.3

# Auto: latest semver tag + 0.0.1, build, push to ACR
.\scripts\build-hqqq-api-docker.ps1 -BumpPatch -Push

# Auto: dev build (0.0.0+gitsha) — default when not on a release tag
.\scripts\build-hqqq-api-docker.ps1
```

Raw `docker build` (same as the script passes through):

```powershell
docker build -f .\src\hqqq-api\Dockerfile `
  --build-arg VERSION=1.0.3 `
  --build-arg INFORMATIONAL_VERSION=1.0.3 `
  -t hqqq-api:1.0.3 `
  .\src\hqqq-api
```

**CI:** `.github/workflows/hqqq-api-docker.yml` — push to `main` → `0.0.0+<sha>`; push tag `v*` → tag name; optional manual run with `version` input. Requires secrets `ACR_USERNAME` and `ACR_PASSWORD`.

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

## 9) Live demo endpoints

- Frontend live: <https://delightful-dune-08a7a390f.1.azurestaticapps.net/>
- Backend live health: <https://app-hqqq-api-mvp-cdgffghwf8c4hgdh.eastus-01.azurewebsites.net/api/system/health>
