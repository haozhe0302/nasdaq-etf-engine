# HQQQ — Nasdaq-100 ETF Engine

Real-time indicative NAV (iNAV) calculation engine for a synthetic Nasdaq-100 ETF.

HQQQ ingests live market data for basket constituents, computes indicative net
asset value with sub-second latency, and streams quote snapshots to a
terminal-style dashboard.

$$iNAV_t = \text{ScaleFactor} \times \sum_{i}(P_{i,t} \times Q_i)$$

> **MVP** — this is a deployable live prototype. It calculates a
> synthetic iNAV from a hybrid basket of publicly scraped holdings data, prices
> that basket in real time via Tiingo IEX, and streams results to a React
> frontend. Infrastructure for persistence, replay, and high-availability
> (Redis, Kafka, TimescaleDB, Grafana) is planned for future phases and is
> **not required** to run the current MVP.

---

## What this MVP delivers

| Capability | Status |
|---|---|
| **Hybrid basket construction** — anchor (Stock Analysis top-25 or Schwab top-20) + tail (Alpha Vantage filtered, or Nasdaq proxy), merged with weight normalization | Live |
| **Active / pending basket semantics** — new fetch creates a pending basket; activation happens at market open with continuity-preserving scale recalibration | Live |
| **Tiingo WebSocket streaming** — real-time IEX quotes via persistent WebSocket with automatic reconnect | Live |
| **5-second REST fallback** — if the WebSocket drops or during bootstrap, Tiingo REST is polled every 5 seconds | Live |
| **Stale detection** — any price older than 5 seconds is marked stale; freshness metrics are surfaced to the frontend | Live |
| **Continuity-preserving basket activation** — when a pending basket activates, the scale factor is recalibrated so iNAV doesn't jump: `newScale = oldNAV / newRawValue` | Live |
| **Scale-state persistence** — calibration state (scale factor, basis entries, basket fingerprint) is saved to JSON and restored on restart | Live |
| **Merged-basket fingerprinting** — deterministic SHA-256 hash of sorted symbols + weights + as-of date; idempotent: a re-fetch that produces the same fingerprint does not create a new pending basket | Live |
| **Raw-source caching** — each upstream fetch (Stock Analysis, Schwab, Alpha Vantage, Nasdaq) is cached to `data/raw/`; a failed fetch falls back to the cached version | Live |
| **Frontend — Market page** — live iNAV, QQQ market price, premium/discount, intraday chart, top movers, freshness gauges | Live |
| **Frontend — Constituents page** — full holdings table with weight, price, change%, shares origin, concentration metrics, data quality | Live |
| **Frontend — System page** — service health cards, runtime metrics, dependency status | Live |
| **Frontend — History page** — UI shell with charts rendered from **static mock data** | Static / mock |

### What remains future-phase

**Phase 2 target architecture (event-driven serving layer):**

```text
Tiingo WS -> ingress-service -> Kafka(market.raw_ticks)
                                  |-> quote-engine (in-memory hot path) -> Redis(latest materialized views + update channel) -> API/WebSocket gateway
                                  |-> persistence-service -> Postgres/Timescale (history/replay/audit)
                                  |-> analytics/replay workers
```

| Future item | Planned role |
|---|---|
| Kafka primary event bus | Source-of-truth tick stream (`market.raw_ticks`), multi-consumer fan-out (quote, persistence, analytics) |
| Quote-engine + Redis serving split | Engine computes in memory; Redis stores latest snapshots (`hqqq:snapshot:latest`, `hqqq:constituents:latest`) and update pub/sub |
| API/WebSocket gateway scale-out | REST reads latest from Redis; WebSocket sends latest-on-connect + broadcast updates via Redis backplane |
| Postgres/Timescale persistence | Raw/normalized tick history, replay, audit, history page backend |
| Observability stack | Prometheus + Grafana for consumer lag, tick->snapshot latency, stale metrics, gateway push latency, Redis latency |
| History page live mode | Replace static mock data with persisted historical/replay-backed queries |
| CI/CD + container orchestration | Automated build/test/deploy pipeline and production-ready runtime topology |

---

## Repository structure

```
nasdaq-etf-engine/
├── README.md
├── .env.example                    # Environment variable template
├── docker-compose.yml              # Future-phase infrastructure (not required for MVP)
├── docs/
│   └── architecture.md
├── infra/
│   └── prometheus/prometheus.yml
├── src/
│   ├── hqqq-api/                   # ASP.NET Core 8 backend (modular monolith)
│   │   ├── Configuration/          # Options classes + env-var mapper
│   │   ├── Hubs/                   # SignalR MarketHub
│   │   ├── Modules/
│   │   │   ├── Basket/             # Hybrid basket construction & caching
│   │   │   ├── MarketData/         # Tiingo WS + REST, in-memory price store
│   │   │   ├── Pricing/            # iNAV engine, scale state, series, movers
│   │   │   └── System/             # Health endpoint, runtime metrics
│   │   ├── data/                   # Runtime data (git-ignored)
│   │   │   ├── basket-cache.json
│   │   │   ├── scale-state.json
│   │   │   ├── raw/               # Per-source cached JSON
│   │   │   └── merged/            # Rolling basket history
│   │   └── Program.cs
│   ├── hqqq-api.tests/            # Unit tests (xUnit)
│   └── hqqq-ui/                   # React 19 + Vite + Tailwind 4
│       └── src/
│           ├── app/               # Router
│           ├── components/        # Panel, StatCard, StatusBadge, Chart, etc.
│           ├── layout/            # AppShell, TopStatusBar, SidebarNav
│           ├── lib/               # Adapters, hooks, types, mock data
│           ├── pages/             # Market, Constituents, History, System
│           └── styles/            # Tailwind theme tokens
```

---

## Prerequisites

| Tool       | Version | Notes |
|------------|---------|-------|
| .NET SDK   | 8.0+    | `dotnet --version` |
| Node.js    | 22 LTS  | Pinned in `src/hqqq-ui/.nvmrc` |
| npm        | 10.x    | Ships with Node 22 |

> Docker Desktop is **not required** for MVP. The `docker-compose.yml`
> provisions future-phase infrastructure (Postgres, Redis, Kafka, Prometheus,
> Grafana) that the current codebase does not connect to.

---

## Quick start

### 1. Clone and configure

```bash
git clone https://github.com/<you>/nasdaq-etf-engine.git
cd nasdaq-etf-engine
cp .env.example .env
```

Edit `.env` and set real API keys:

- **`TIINGO_API_KEY`** — required. Get a free key at <https://api.tiingo.com/>.
- **`ALPHA_VANTAGE_API_KEY`** — required for full hybrid basket. Free key at <https://www.alphavantage.co/support/#api-key>.

All other values have working defaults.

Start infrastructure

```bash
docker compose up -d
docker compose up -d
```

### 2. Run the backend

```bash
dotnet run --project src/hqqq-api
```

The API starts on **http://localhost:5015**. Swagger UI is available at
[http://localhost:5015/swagger](http://localhost:5015/swagger) in development mode.

> For hot-reload during development: `dotnet watch run --project src/hqqq-api`

### 3. Run the frontend

```bash
cd src/hqqq-ui
npm install
npm run dev
```

The dev server starts on **http://localhost:5173**. API calls to `/api/*` and
SignalR connections to `/hubs/*` are proxied to the backend automatically.

### 4. Open the UI

Navigate to **http://localhost:5173**. You'll land on the Market page.

During market hours (9:30 AM – 4:00 PM ET), the engine bootstraps automatically
once it has sufficient price coverage. Outside market hours, the REST fallback
still fetches last-known prices, but the iNAV won't update until fresh ticks
arrive.

---

## API endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/quote` | Current iNAV quote snapshot (Market page data) |
| GET | `/api/constituents` | Holdings with prices, weights, quality metrics |
| GET | `/api/basket/current` | Active + pending basket state, fingerprints, source outcomes |
| POST | `/api/basket/refresh` | Force a basket re-fetch + merge |
| GET | `/api/marketdata/status` | Ingestion health, coverage, WebSocket state |
| GET | `/api/marketdata/latest` | Latest prices for all or specific symbols |
| GET | `/api/system/health` | Service health, runtime metrics, dependency status |
| WS | `/hubs/market` | SignalR hub — `QuoteUpdate` event every ~1 second |

Swagger UI: **http://localhost:5015/swagger**

---

## Frontend pages

| Route | Page | Data source |
|-------|------|-------------|
| `/market` | Market | Live — SignalR `QuoteUpdate` + REST `/api/quote` |
| `/constituents` | Constituents | Live — REST polling `/api/constituents` every 5s |
| `/history` | History | **Static mock data** (not yet backed by live replay) |
| `/system` | System | Live — REST polling `/api/system/health` every 5s |

`/` redirects to `/market`.

---

## Environment variables

See [`.env.example`](.env.example) for the full list with comments.

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `TIINGO_API_KEY` | Yes | — | Tiingo IEX WebSocket + REST |
| `ALPHA_VANTAGE_API_KEY` | Yes | — | ETF_PROFILE endpoint for tail holdings |
| `TIINGO_WS_URL` | No | `wss://api.tiingo.com/iex` | WebSocket endpoint |
| `TIINGO_REST_BASE_URL` | No | `https://api.tiingo.com/iex` | REST fallback endpoint |
| `TIINGO_REST_POLLING_INTERVAL_SECONDS` | No | `5` | REST poll interval |
| `TIINGO_STALE_AFTER_SECONDS` | No | `5` | Stale threshold |
| `HQQQ_ENABLE_LIVE_MODE` | No | `true` | Enable live market data |
| `HQQQ_ENABLE_MOCK_FALLBACK` | No | `false` | Fall back to mock if live fails |
| `HQQQ_MARKET_TIME_ZONE` | No | `America/New_York` | Market hours time zone |
| `HQQQ_BASKET_CACHE_FILE` | No | `data/basket-cache.json` | Merged basket cache path |
| `HQQQ_BASKET_RAW_CACHE_DIR` | No | `data/raw` | Per-source raw cache directory |
| `HQQQ_BASKET_MERGED_HISTORY_DIR` | No | `data/merged` | Rolling basket history directory |
| `HQQQ_BASKET_REFRESH_TIME` | No | `08:00` | Daily basket refresh time (local) |
| `HQQQ_SCALE_STATE_FILE` | No | `data/scale-state.json` | Scale state persistence path |
| `HQQQ_QUOTE_BROADCAST_INTERVAL_MS` | No | `1000` | SignalR broadcast interval |
| `HQQQ_SERIES_CAPACITY` | No | `5000` | In-memory series ring buffer size |

---

## Running tests

```bash
dotnet test src/hqqq-api.tests
```

See [docs/smoke-test-runbook.md](docs/smoke-test-runbook.md) for the manual
validation checklist.

---

## Known limitations (MVP)

1. **Hybrid basket, not official full-holdings reconstruction.**
   The basket is assembled from publicly scraped data (Stock Analysis, Schwab,
   Alpha Vantage, Nasdaq API). It covers ~100 constituents with anchor weights
   from the top 20-25 holdings and proportionally normalized tail weights. It
   is not an official, authorized holdings feed.

2. **`marketPrice` is a QQQ proxy, not a real HQQQ traded price.**
   Since HQQQ is a synthetic/educational ETF that does not trade on any
   exchange, `marketPrice` in the quote snapshot is the live QQQ price used as
   a reference for premium/discount calculation.

3. **History page is static mock data.**
   The History page renders charts and metrics from hardcoded mock data. It is
   not yet backed by persisted live replay or historical storage. This requires
   TimescaleDB or equivalent time-series storage (future phase).

4. **No database persistence.**
   All runtime state is in-memory or in local JSON files under `data/`.
   Process restarts lose the in-memory series buffer (scale state and basket
   cache are persisted to disk).

5. **Single-instance only.**
   The in-memory price store and pricing engine are singletons. There is no
   distributed state or horizontal scaling.

6. **No authentication or authorization.**
   All API endpoints and the frontend are publicly accessible.

7. **Scraping fragility.**
   The Stock Analysis and Schwab adapters parse HTML and are subject to
   upstream layout changes. The raw-source cache provides resilience against
   transient failures but not permanent structural changes.

8. **Future infrastructure is deferred.**
   Redis, Kafka, TimescaleDB, Prometheus, and Grafana are defined in
   `docker-compose.yml` for future phases. The current codebase does not
   connect to any of them.

---

## Shutdown

The backend and frontend are standalone processes — Ctrl+C stops each.

If you started Docker infrastructure for experimentation:

```bash
docker compose down        # stop containers, keep data
docker compose down -v     # stop containers and remove volumes
```

---

## Architecture

See [docs/architecture.md](docs/architecture.md) for module responsibilities
and dependency rules.


## Screenshots

### Market — Real-time iNAV command center
![Market page](images/hqqq-ui-market-demo.png)

### Constituents — Holdings table and basket insights
![Constituents page](images/hqqq-ui-constituents-demo.png)

### History — Historical analytics and tracking error
![History page](images/hqqq-ui-history-demo.png)

### System — Service health and pipeline monitoring
![System page](images/hqqq-ui-system-demo.png)