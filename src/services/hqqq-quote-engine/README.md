# hqqq-quote-engine

Consumes raw ticks and basket events from Kafka. Computes iNAV snapshots.
Writes latest state to Redis, publishes snapshot events to Kafka.

**Future home of current `Pricing` module core logic.**

## Responsibilities (Phase 2)

- Consume `market.raw_ticks.v1` and `refdata.basket.active.v1` from Kafka
- Maintain in-memory state: latest quote by symbol, basket composition, running
  basket sum, freshness/stale/quote quality, scale factor/pricing basis
- Produce: Redis latest snapshot + constituents, Kafka `pricing.snapshots.v1`
- HA via Kafka consumer group rebalance (single active consumer per basketId)
