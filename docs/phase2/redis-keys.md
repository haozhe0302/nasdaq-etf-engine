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
| `hqqq:channel:quote-update` | `hqqq-quote-engine` | every `hqqq-gateway` replica (independent subscribers) | Live `QuoteUpdateEnvelope` fan-out: each gateway replica receives every published envelope, validates the JSON, and broadcasts the inner `QuoteUpdate` DTO to its own connected SignalR clients on `/hubs/market` (event name `QuoteUpdate`). | **Live (D2; multi-replica-safe per D5).** Constant: `RedisKeys.QuoteUpdateChannel` in `src/building-blocks/Hqqq.Infrastructure/Redis/RedisKeys.cs`. |
| `hqqq:channel:snapshot` | _(reserved — no service publishes today)_ | _(reserved — no service subscribes today)_ | Originally proposed as a "snapshot-available" notification before D2 settled on per-update envelopes. **Superseded by `hqqq:channel:quote-update`.** | **Reserved name only.** Constant: `RedisKeys.SnapshotChannel`. Kept to avoid a future rename collision; safe to ignore. |

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

## Live SignalR fan-out (D2 + D5) — no Redis backplane

**SignalR's Redis backplane is deliberately NOT enabled.** Multi-replica
fan-out on `/hubs/market` is solved at the application layer by the
`hqqq:channel:quote-update` Redis pub/sub channel listed above:

1. `hqqq-quote-engine` `PUBLISH`es a `QuoteUpdateEnvelope` JSON payload
   on `hqqq:channel:quote-update` for every materialized cycle.
2. Every `hqqq-gateway` replica runs a `QuoteUpdateSubscriber` hosted
   service that `SUBSCRIBE`s to the same channel.
3. Each replica receives every envelope, validates the JSON
   (malformed → dropped silently and counted via
   `hqqq.gateway.quote_updates_malformed`), and broadcasts the inner
   `QuoteUpdate` DTO to **its own** connected SignalR clients using the
   locked event name `QuoteUpdate`.

### Why no SignalR backplane

The per-replica subscribe + local broadcast pattern preserves the
multi-replica-safe invariant ("every replica receives every published
update") without paying the cost of `AddStackExchangeRedis(...)` on top
of `AddSignalR()`. Sticky sessions are NOT required for `/hubs/market` —
a SignalR client connected to either replica receives the full live
stream. Reconnect / bootstrap state still comes from REST
`GET /api/quote`; D2 deliberately does not introduce a replay buffer or
sequence protocol.

### When the SignalR Redis backplane would become relevant

Only if a future scale-out posture breaks the "every replica receives
every published update" invariant — for example, partitioning gateways
into shards that each subscribe to only a subset of channels. In that
case, adding `AddStackExchangeRedis(...)` on top of `AddSignalR()` would
re-establish hub-message fan-out at the SignalR layer. SignalR's own
key prefix (`SignalR:...`) does not conflict with the `hqqq:` keyspace
documented above, so no key migration would be required.
