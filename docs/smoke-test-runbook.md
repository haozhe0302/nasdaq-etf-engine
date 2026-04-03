# Smoke-Test Runbook — MVP

Manual validation checklist for verifying a working HQQQ deployment.

---

## Prerequisites

- `.env` exists with valid `TIINGO_API_KEY` and `ALPHA_VANTAGE_API_KEY`
- .NET 8 SDK installed
- Node.js 22 + npm 10 installed

---

## 1. Environment configuration sanity

```bash
# Verify .env exists and contains required keys (no real secrets in output)
grep -c "TIINGO_API_KEY" .env        # expect: 1
grep -c "ALPHA_VANTAGE_API_KEY" .env # expect: 1
```

Confirm neither key contains the placeholder `YOUR_..._HERE`.

---

## 2. Backend build

```bash
dotnet build src/hqqq-api
```

**Pass criteria:** Exit code 0, no errors.

---

## 3. Unit tests

```bash
dotnet test src/hqqq-api.tests --verbosity normal
```

**Pass criteria:** All tests pass (0 failures).

---

## 4. Frontend build

```bash
cd src/hqqq-ui
npm install
npm run build
```

**Pass criteria:** Exit code 0, `dist/` directory created.

---

## 5. Start backend

```bash
dotnet run --project src/hqqq-api
```

Wait for log output showing:
- `Now listening on: http://localhost:5015`
- Basket refresh logs (Stock Analysis, Schwab, Alpha Vantage fetches)

---

## 6. API endpoint checks

Run these from a separate terminal while the backend is running.

### `/api/system/health`

```bash
curl -s http://localhost:5015/api/system/health | python -m json.tool
```

**Expected:** HTTP 200 with JSON containing `status`, `uptime`, `services` array.

### `/api/basket/current`

```bash
curl -s http://localhost:5015/api/basket/current | python -m json.tool
```

**Expected:** HTTP 200 with `active` object containing `fingerprint`, `summary`, `constituents` array. If the basket has not loaded yet, HTTP 503 with an error message — wait a moment and retry.

### `/api/marketdata/status`

```bash
curl -s http://localhost:5015/api/marketdata/status | python -m json.tool
```

**Expected:** HTTP 200 with `isRunning: true`, `health` object with `symbolsTracked > 0`.

### `/api/quote`

```bash
curl -s http://localhost:5015/api/quote | python -m json.tool
```

**Expected:**
- During market hours with sufficient data: HTTP 200 with `nav`, `marketPrice`, `freshness`, `feeds`.
- Before bootstrap completes: HTTP 503 with `status: "initializing"`.

### `/api/constituents`

```bash
curl -s http://localhost:5015/api/constituents | python -m json.tool
```

**Expected:** HTTP 200 with `holdings` array, `concentration`, `quality`, `source`.

---

## 7. SignalR quote stream

Open a browser console at `http://localhost:5173` (after starting the frontend dev server) and verify:

1. Navigate to the Market page (`/market`).
2. Open the browser's Network tab, filter by "WS".
3. Confirm a WebSocket connection to `/hubs/market` is established.
4. Verify `QuoteUpdate` messages arrive approximately every second (during market hours).

Alternatively, use a SignalR test client:

```javascript
// Browser console on http://localhost:5173
// The app already connects — check for live quote updates in the Market page UI.
// If the iNAV value updates every ~1 second, SignalR is working.
```

---

## 8. Frontend dev server

```bash
cd src/hqqq-ui
npm run dev
```

Open `http://localhost:5173` and verify:

| Page | Check |
|------|-------|
| `/market` | iNAV value visible, chart rendering, movers list populated |
| `/constituents` | Holdings table with > 50 rows, weight/price columns filled |
| `/history` | Charts render (mock data), no blank page |
| `/system` | Service health cards show green/healthy status |

---

## 9. Swagger UI

Open `http://localhost:5015/swagger` in a browser.

**Expected:** Swagger UI loads with endpoint groups: Basket, MarketData, Pricing, System.

---

## Quick one-liner check (all API endpoints)

```bash
for endpoint in /api/system/health /api/basket/current /api/marketdata/status /api/quote /api/constituents; do
  status=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5015$endpoint)
  echo "$endpoint -> HTTP $status"
done
```

**Expected during market hours after bootstrap:**
All endpoints return HTTP 200.

**Expected before bootstrap:**
`/api/quote` and `/api/constituents` may return 503 until the pricing engine calibrates.
