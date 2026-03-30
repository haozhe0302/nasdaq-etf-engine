# Architecture

## Overview

HQQQ is a full-stack ETF analytics engine with two main components:

- **hqqq-api** — an ASP.NET Core modular monolith that owns basket composition,
  market data ingestion, iNAV calculation, and system observability.
- **hqqq-ui** — a React/Vite terminal-style dashboard that presents real-time
  iNAV, constituent holdings, historical analytics, and system health.

The backend is a single deployable unit with clear internal module boundaries.
The frontend communicates with the backend over HTTP (REST) and will evolve to
include WebSocket streaming for high-frequency quote snapshots.

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

Owns the daily basket composition: which securities are in the ETF, how many
shares are held, and the reference date of the snapshot. This is the
authoritative source of "what the ETF contains."

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
├── types.ts    # View model interfaces (MarketSnapshot, Constituent, etc.)
├── mock.ts     # Data source implementation
└── hooks.ts    # Page-level React hooks (useMarketData, useSystemData, etc.)
```

Pages consume data exclusively through hooks (`useMarketData()`,
`useConstituentData()`, `useHistoryData()`, `useSystemData()`). The hook
return types are the stable API contract. The underlying data source can be
swapped from mock to REST or WebSocket without changing any page code.

**Refresh model**: Market, Constituents, and System pages poll at a 1-second
cadence. History data is static (historical series do not change in real-time).
Only the active page's hook runs; hooks clean up their intervals on unmount.

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

## Infrastructure (local dev)

| Service       | Purpose                        | Port  |
|---------------|--------------------------------|-------|
| TimescaleDB   | Historical iNAV time-series    | 5432  |
| Redis         | Price cache, calculation cache | 6379  |
| Kafka (KRaft) | Event streaming                | 9092  |
| Kafka UI      | Topic/consumer inspection      | 8080  |
| Prometheus    | Metrics scraping               | 9090  |
| Grafana       | Dashboards                     | 3000  |

All infrastructure runs via `docker compose up -d`. The API runs on the
host for fast iteration (`dotnet watch run`).

---

## Future evolution

| Concern                        | Current                  | Planned                                  |
|--------------------------------|--------------------------|------------------------------------------|
| Market data transport          | Polling (1s)             | WebSocket streaming for sub-second quotes |
| Backend calculation            | Contract stubs           | Full iNAV engine with parallel pricing    |
| Data persistence               | —                        | TimescaleDB for historical snapshots      |
| Caching                        | —                        | Redis for latest prices and calc cache    |
| Event streaming                | —                        | Kafka for tick ingestion and quote pub    |
| Primary chart library          | ECharts                  | Lightweight Charts for Market chart       |
| Authentication                 | —                        | TBD                                       |
| CI/CD                          | —                        | TBD                                       |
