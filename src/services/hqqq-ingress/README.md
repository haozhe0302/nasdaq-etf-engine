# hqqq-ingress

Tiingo WebSocket/REST adapter. Normalizes raw market ticks and publishes them
to Kafka.

**Future home of current `MarketData` module.**

## Responsibilities (Phase 2)

- Maintain Tiingo WebSocket connection and REST fallback
- Normalize provider payloads into internal event format
- Enrich with `providerTs`, `ingressTs`, `seq`, `symbol`, `provider`
- Publish to Kafka: `market.raw_ticks.v1`, `market.latest_by_symbol.v1` (compacted)
