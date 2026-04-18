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

| Channel | Publisher | Subscriber(s) | Purpose | Status |
|---------|----------|---------------|---------|--------|
| `hqqq:channel:snapshot` | (planned) hqqq-quote-engine | (planned) hqqq-gateway | Notify the gateway that a new snapshot is available in Redis so it can push over SignalR without polling | **Planned, not yet active** — no service publishes or subscribes today. The reserved key/channel name is still listed here so it can be wired in D2 without a rename. |

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

> **Status: deferred to Phase 2D2 — not active today.**

The gateway currently uses only `AddSignalR()` (see
`src/services/hqqq-gateway/Program.cs`). `/hubs/market` is served in-process
by a single gateway replica; there is **no** Redis pub/sub backplane yet and
no `SignalR:*` keys are written.

When multi-replica fan-out is introduced in Phase 2D2, the gateway will add
`AddStackExchangeRedis(...)` on top of `AddSignalR()`, reusing the existing
`Redis:Configuration`. SignalR's own key prefix (`SignalR:...`) does not
conflict with the `hqqq:` keyspace documented above, so no key migration
is required at that point.
