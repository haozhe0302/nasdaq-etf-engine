# Architecture

## Overview

HQQQ is a modular monolith built as a single ASP.NET Core application.
All backend logic lives in one deployable unit (`hqqq-api`) with clear
internal module boundaries. The frontend is a separate React/Vite SPA
that communicates with the backend over HTTP.

This is an intentional starting point. A single-process architecture keeps
the development and debugging experience simple for a solo/small-team project.
Modules can be extracted into separate services later if scale demands it.

## Module map

```
src/hqqq-api/
├── Modules/
│   ├── Basket/          # ETF basket composition and reference data
│   ├── MarketData/      # Live constituent price ticks
│   ├── Pricing/         # Indicative NAV / quote snapshot calculation
│   └── System/          # Health, readiness, config, observability
└── Program.cs           # Composition root
```

### Basket

Owns the daily basket composition: which securities are in the ETF, how many
shares are held, and the reference date of the snapshot. This is the
authoritative source of "what the ETF contains."

Key contract: `BasketConstituent`

### MarketData

Owns the ingestion-side representation of live constituent prices. Defines
the `PriceTick` contract that upstream providers produce and downstream
consumers (Pricing) depend on.

No real provider implementation exists yet — only the contract.

### Pricing

Owns the iNAV / quote snapshot domain. Depends on Basket (composition) and
MarketData (prices) contracts to compute the indicative NAV. The actual
calculation engine is deferred to a later phase.

Key contract: `QuoteSnapshot`

### System

Health checks, readiness probes, version reporting, and observability
endpoints. Currently exposes a basic `/api/system/health` endpoint.

Key contracts: `SystemHealth`, `DependencyHealth`

## Dependency direction

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

## Why modular monolith?

- Single deployable keeps local dev, debugging, and CI simple.
- Module boundaries enforce separation of concerns without network overhead.
- Extraction to separate services is straightforward if needed later:
  each module already owns its own contracts and registration.

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

## Intentionally deferred

The following are **not** part of Phase A and will be addressed in later phases:

- Real iNAV calculation engine
- Kafka consumers/producers
- Redis cache integration
- Database entities, migrations, and repositories
- Market data provider connections (Finnhub, Polygon)
- WebSocket/SignalR real-time push
- Authentication and authorization
- CI/CD pipeline
- Grafana dashboards and alerting rules
- Production deployment configuration
