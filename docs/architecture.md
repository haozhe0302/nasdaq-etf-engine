# Architecture

## Overview

HQQQ is a full-stack ETF analytics engine with two main components:

- **hqqq-api** — an ASP.NET Core modular monolith that owns basket composition,
  market data ingestion, iNAV calculation, and system observability.
- **hqqq-ui** — a React/Vite terminal-style dashboard that presents real-time
  iNAV, constituent holdings, historical analytics, and system health.

The backend is a single deployable unit with clear internal module boundaries.
The frontend communicates with the backend over HTTP (REST) and WebSocket
(SignalR) for real-time quote streaming.

This is an intentional starting point. A single-process backend keeps
development and debugging simple for a solo/small-team project. Modules can
be extracted into separate services later if scale demands it.

---

## Backend (hqqq-api)

### Module map

```
src/hqqq-api/
├── Modules/
│   ├── Basket/          # ETF basket composition and reference data
│   ├── MarketData/      # Live constituent price ticks
│   ├── Pricing/         # Indicative NAV / quote snapshot calculation
│   └── System/          # Health, readiness, config, observability
└── Program.cs           # Composition root
```

A `Shared/` directory may be introduced later for genuinely cross-cutting
primitives. It does not exist yet — avoid creating it until a real need arises.

### Basket

Owns the hybrid basket composition: which securities are in the ETF, their
weights and shares held, and the reference date. Constructs baskets from
multiple public scraped sources (Stock Analysis, Schwab, Alpha Vantage, Nasdaq)
using an anchor + tail merge strategy.

Key contract: `BasketConstituent`

### MarketData

Owns the ingestion-side representation of live constituent prices. Defines
the `PriceTick` contract that upstream providers produce and downstream
consumers (Pricing) depend on.

### Pricing

Owns the iNAV / quote snapshot domain. Depends on Basket (composition) and
MarketData (prices) contracts to compute the indicative NAV.

Key contract: `QuoteSnapshot`

### System

Health checks, readiness probes, version reporting, and observability
endpoints. Exposes `/api/system/health`.

Key contracts: `SystemHealth`, `DependencyHealth`

### Dependency direction

```
Pricing  →  Basket
Pricing  →  MarketData
System   →  (standalone, may probe other modules for health)
Basket   →  (standalone)
MarketData → (standalone)
```

Contracts flow **inward**: a module exposes its contracts, and dependents
import those contracts. No module reaches into another module's internals.
Cyclic dependencies between modules are not allowed.

### Why modular monolith?

- Single deployable keeps local dev, debugging, and CI simple.
- Module boundaries enforce separation of concerns without network overhead.
- Extraction to separate services is straightforward if needed later:
  each module already owns its own contracts and registration.

---

## Frontend (hqqq-ui)

### Structure

```
src/hqqq-ui/src/
├── app/            # BrowserRouter, route definitions
├── components/     # Reusable UI primitives
├── layout/         # Application shell (persists across route changes)
├── lib/            # Data types, data sourcing, page-level hooks
├── pages/          # Page components (one per route)
└── styles/         # Tailwind CSS theme and design tokens
```

### Application shell

The shell is composed of three persistent layout components:

- **TopStatusBar** — compact header showing HQQQ branding, symbol count,
  UTC clock, data mode indicator, refresh cadence, and system health badge.
- **SidebarNav** — left navigation with active-state highlighting for the
  four primary routes.
- **Content area** — renders the active page via React Router `<Outlet />`,
  with overflow scrolling.

### Pages

| Route            | Component          | Purpose                                                  |
|------------------|--------------------|----------------------------------------------------------|
| `/market`        | `MarketPage`       | Command-center view: KPI strip, iNAV vs market price chart, premium/discount chart, quote freshness, top movers, basket summary, feed status |
| `/constituents`  | `ConstituentsPage` | Holdings visibility: toolbar with search/filter, 9-column data table, concentration metrics, weight distribution, data quality panel |
| `/history`       | `HistoryPage`      | Analytics workspace: date range toolbar, historical iNAV vs QQQ comparison chart, P/D history, tracking error metrics, P/D distribution histogram, replay diagnostics |
| `/system`        | `SystemPage`       | Ops monitoring: service health cards with latency, runtime metrics (uptime, memory, CPU, throughput), pipeline status table, recent events log |

`/` redirects to `/market`. Unknown routes also redirect to `/market`.

### Components

Thin, reusable UI primitives shared across pages:

| Component     | Purpose                                    |
|---------------|--------------------------------------------|
| `Panel`       | Bordered container with optional title bar  |
| `StatCard`    | KPI display with label, value, status color |
| `StatusBadge` | Colored dot + label health indicator        |
| `MetricRow`   | Label–value pair for dense metric display   |
| `Chart`       | ECharts wrapper with ResizeObserver         |

### Data layer

```
lib/
├── types.ts           # View model interfaces (MarketSnapshot, Constituent, etc.)
├── api.ts             # REST fetch helpers, SignalR hub connection
├── adapters.ts        # Backend DTO → UI view model mapping
├── hooks.ts           # Page-level React hooks (useMarketData, useSystemData, etc.)
├── mock.ts            # Static mock data (used only by History page)
└── updateTracker.ts   # EMA-smoothed update interval tracking for status bar
```

Pages consume data exclusively through hooks (`useMarketData()`,
`useConstituentData()`, `useHistoryData()`, `useSystemData()`). The hook
return types are the stable API contract.

**Refresh model**: Market page uses SignalR `QuoteUpdate` events (~1s) with
REST `/api/quote` as initial load. Constituents and System pages poll their
respective REST endpoints every 5 seconds. History data is static mock
(pending persisted series storage). Only the active page's hook runs; hooks
clean up their connections/intervals on unmount.

### Design system

Dark terminal-style theme defined via Tailwind CSS v4 `@theme` tokens:

| Token       | Hex       | Usage                         |
|-------------|-----------|-------------------------------|
| `canvas`    | `#0a0e17` | Page background               |
| `surface`   | `#111827` | Panels, cards, sidebar        |
| `overlay`   | `#1a2233` | Hover states                  |
| `edge`      | `#1e293b` | Borders, dividers             |
| `content`   | `#e1e4e8` | Primary text                  |
| `muted`     | `#8b949e` | Secondary text, labels        |
| `accent`    | `#3b82f6` | Active elements, links        |
| `positive`  | `#22c55e` | Positive changes, healthy     |
| `negative`  | `#ef4444` | Negative changes, unhealthy   |

Typography: Inter (sans) for UI text, JetBrains Mono / Fira Code (mono) for
data values. Compact spacing throughout — designed for information density.

### Charting

ECharts is used for all chart regions. The `Chart` component is a thin wrapper
(~30 lines) that handles initialization, option updates, and resize observation.

The primary Market chart (iNAV vs market price) is architecturally isolated:
data flows as `TimeSeriesPoint[]` through the hook, and chart options are
constructed in the page component. This allows the chart implementation to be
swapped to Lightweight Charts in a future phase without affecting the data
layer or other components.

---

## Infrastructure

### MVP (current)

No external infrastructure is required. The backend runs standalone with:
- In-memory price store (replaces Redis)
- Local JSON file persistence for basket cache, scale state, and series
- Tiingo WebSocket + REST for live market data

### Future phases (docker-compose.yml)

| Service       | Purpose                        | Port  | MVP status |
|---------------|--------------------------------|-------|----------------|
| TimescaleDB   | Historical iNAV time-series    | 5432  | Not connected  |
| Redis         | Price cache, calculation cache  | 6379  | Not connected  |
| Kafka (KRaft) | Event streaming                | 9092  | Not connected  |
| Kafka UI      | Topic/consumer inspection      | 8080  | Not connected  |
| Prometheus    | Metrics scraping               | 9090  | Not connected  |
| Grafana       | Dashboards                     | 3000  | Not connected  |

`docker compose up -d` provisions these for experimentation, but the
MVP codebase does not use them.

---

## Future evolution

| Concern                        | Current (MVP)                    | Planned                                  |
|--------------------------------|--------------------------------------|------------------------------------------|
| Market data transport          | Tiingo WS + 5s REST fallback         | Additional data providers                |
| iNAV calculation               | Live hybrid basket pricing engine    | Parallel pricing, more sources           |
| Data persistence               | Local JSON files                     | TimescaleDB for historical snapshots     |
| Caching                        | In-memory ConcurrentDictionary       | Redis for distributed price cache        |
| Event streaming                | —                                    | Kafka for tick ingestion and quote pub   |
| History page                   | Static mock data                     | Live replay from persisted series        |
| Primary chart library          | ECharts                              | Lightweight Charts for Market chart      |
| Authentication                 | —                                    | TBD                                      |
| CI/CD                          | —                                    | GitHub Actions                           |
