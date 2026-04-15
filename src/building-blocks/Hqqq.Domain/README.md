# Hqqq.Domain

Pure domain model: entities, value objects, and domain services.

This project has **no infrastructure dependencies** — no Kafka, Redis, HTTP, or
database references. It expresses the core business rules of the HQQQ engine.

## Contents

- `ValueObjects/`
  - `PriceQuality` — enum: Live, Stale, Unknown
  - `Fingerprint` — typed wrapper for basket SHA-256 fingerprints
  - `BasketStatus` — enum: Pending, Active, Retired
- `Entities/`
  - `BasketVersion` — versioned basket definition (maps to `basket_versions` table)
  - `ConstituentWeight` — single constituent in a basket
- `Services/`
  - `PremiumDiscountCalculator` — pure premium/discount percentage calculation
