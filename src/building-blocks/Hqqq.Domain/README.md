# Hqqq.Domain

Pure domain model: entities, value objects, and domain services.

This project has **no infrastructure dependencies** — no Kafka, Redis, HTTP, or
database references. It expresses the core business rules of the HQQQ engine.

## Planned contents (Phase 2)

- `Entities/` — `BasketVersion`, `ConstituentWeight`, `LatestQuoteState`, `QuoteSnapshot`
- `ValueObjects/` — `PriceQuality`, `Fingerprint`
- `Services/` — `PremiumDiscountCalculator`
