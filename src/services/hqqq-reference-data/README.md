# hqqq-reference-data

Basket refresh, pending/active activation, corporate-action adjustment, and
basket event publishing.

**Future home of current `Basket` + `CorporateActions` modules.**

## Current status (Phase 2A2 — stub skeleton)

The service compiles and runs with deterministic stub data.
No real data-source integrations or Kafka wiring yet.

### Endpoints

| Method | Path | Status |
|--------|------|--------|
| GET | `/healthz` | 200 — health check |
| GET | `/api/basket/current` | 200 — returns stub active basket + constituents |
| POST | `/api/basket/refresh` | 501 — not yet implemented |

## Folder structure

```
hqqq-reference-data/
├── Endpoints/          # Minimal API route definitions
│   └── BasketEndpoints.cs
├── Services/           # Business logic interfaces + stubs
│   ├── IBasketService.cs
│   └── StubBasketService.cs
├── Repositories/       # Persistence boundary interfaces + stubs
│   ├── IBasketRepository.cs
│   └── InMemoryBasketRepository.cs
├── Jobs/               # Background jobs (scheduled refresh, activation)
│   └── BasketRefreshJob.cs
├── Models/             # REST response DTOs
│   └── BasketCurrentResponse.cs
└── Program.cs
```

## Responsibilities (Phase 2 — planned)

- Manage active basket / pending basket lifecycle
- Manage basket fingerprint, as-of date, split adjustment, activation window
- Expose admin / internal API (`GET /api/basket/current`, `POST /api/basket/refresh`, etc.)
- Publish Kafka events: `refdata.basket.active.v1`, `refdata.basket.events.v1`
