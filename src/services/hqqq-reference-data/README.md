# hqqq-reference-data

Basket refresh, pending/active activation, corporate-action adjustment, and
basket event publishing.

**Future home of current `Basket` + `CorporateActions` modules.**

## Responsibilities (Phase 2)

- Manage active basket / pending basket lifecycle
- Manage basket fingerprint, as-of date, split adjustment, activation window
- Expose admin / internal API (`GET /api/basket/current`, `POST /api/basket/refresh`, etc.)
- Publish Kafka events: `refdata.basket.active.v1`, `refdata.basket.events.v1`
