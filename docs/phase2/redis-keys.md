# Phase 2 -- Redis Key Design

All key patterns and builder methods are defined in
`src/building-blocks/Hqqq.Infrastructure/Redis/RedisKeys.cs`.

## Key inventory

| Key pattern | Example | Data type | Writer | Reader(s) | TTL |
|-------------|---------|-----------|--------|-----------|-----|
| `hqqq:snapshot:{basketId}` | `hqqq:snapshot:HQQQ` | Hash | hqqq-quote-engine | hqqq-gateway | None (overwritten each cycle) |
| `hqqq:latest:{symbol}` | `hqqq:latest:AAPL` | Hash | hqqq-quote-engine | hqqq-gateway | None (overwritten on tick) |
| `hqqq:basket:active:{basketId}` | `hqqq:basket:active:HQQQ` | Hash | hqqq-reference-data | hqqq-quote-engine, hqqq-gateway | None |
| `hqqq:freshness:{basketId}` | `hqqq:freshness:HQQQ` | Hash | hqqq-quote-engine | hqqq-gateway | None (overwritten each cycle) |

## Channels

| Channel | Publisher | Subscriber(s) | Purpose |
|---------|----------|---------------|---------|
| `hqqq:channel:snapshot` | hqqq-quote-engine | hqqq-gateway | Notifies gateway that a new snapshot is available in Redis; gateway reads the hash and broadcasts via SignalR |

## Naming conventions

- Prefix: `hqqq:` for all keys to avoid collisions in shared Redis instances
- Hierarchy: `hqqq:{domain}:{entity}` or `hqqq:{domain}:{entity}:{id}`
- Channels: `hqqq:channel:{event}`

## Data format

All hash values are JSON-serialized using `HqqqJsonDefaults.Options`. Field names use camelCase to match the Kafka event and REST response formats.

## Eviction and persistence

- Phase 2 Redis is configured as a cache (no AOF/RDB persistence required).
- No TTL is set on serving keys -- they are overwritten on every compute cycle (~500ms for snapshots, per-tick for latest quotes).
- If Redis restarts, the quote-engine re-populates all keys within one compute cycle from its in-memory state.

## SignalR backplane

The gateway uses `AddSignalR().AddStackExchangeRedis()` for SignalR message distribution across gateway instances. This uses Redis pub/sub internally with its own key prefix (`SignalR:...`) and does not conflict with the `hqqq:` keyspace.
