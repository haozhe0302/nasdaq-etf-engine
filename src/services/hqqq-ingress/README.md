# hqqq-ingress

Tiingo IEX WebSocket + REST adapter. Normalizes raw market ticks and
publishes them to Kafka. Phase 2 ingress is **self-sufficient**: there is
no stub / hybrid / log-only path. The legacy `hqqq-api` monolith does NOT
participate in Phase 2 ingest.

## Current status (real)

- Opens a real Tiingo IEX websocket and publishes normalized
  `RawTickV1` + `LatestSymbolQuoteV1` events to Kafka.
- Subscribes to `refdata.basket.active.v1` and drives its Tiingo
  subscribe / unsubscribe calls off the basket event — the active symbol
  universe is derived from the basket published by `hqqq-reference-data`.
- Fails fast on a missing / placeholder `Tiingo:ApiKey`.

## Runtime behaviour

1. **StartAsync**: validates `Tiingo:ApiKey` (throws `InvalidOperationException`
   on missing / placeholder so the orchestrator restarts with a visible
   error).
2. **BasketActiveConsumer**: subscribes to `refdata.basket.active.v1` with
   `AutoOffsetReset=Earliest`, feeds every `BasketActiveStateV1` into
   `ActiveSymbolUniverse`.
3. **BasketSubscriptionCoordinator**: on each fingerprint change, diffs
   the symbol set against the last-applied and calls
   `ITiingoStreamClient.SubscribeAsync(added)` /
   `UnsubscribeAsync(removed)` mid-session.
4. **TiingoIngressWorker**: waits up to
   `Ingress:Basket:StartupWaitSeconds` for the first basket. If it times
   out and `Tiingo:Symbols` is configured, falls back to that override
   (exposed in `/healthz/ready` as `bootstrap:override`). Otherwise the
   worker idles with no subscription and the basket health check stays
   `Unhealthy` so operators notice.
5. **Websocket loop**: exponential backoff on disconnect; mid-session
   subscribe/unsubscribe is applied immediately, otherwise queued for
   the next connect.

## Folder structure

```
hqqq-ingress/
├── Configuration/
│   ├── TiingoOptions.cs
│   └── IngressBasketOptions.cs
├── Clients/
│   ├── ITiingoStreamClient.cs
│   ├── TiingoStreamClient.cs
│   ├── ITiingoSnapshotClient.cs
│   └── TiingoSnapshotClient.cs
├── Consumers/
│   └── BasketActiveConsumer.cs       # refdata.basket.active.v1 → ActiveSymbolUniverse
├── State/
│   ├── IngestionState.cs             # upstream connection + tick counters
│   ├── ActiveSymbolUniverse.cs       # current basket snapshot for ingress
│   └── BasketSubscriptionCoordinator.cs
├── Normalization/
│   └── TiingoQuoteNormalizer.cs
├── Publishing/
│   ├── ITickPublisher.cs
│   └── KafkaTickPublisher.cs         # real Kafka producer (fan-out to 2 topics)
├── Health/
│   ├── IngressUpstreamHealthCheck.cs # Tiingo connection + staleness
│   └── IngressBasketHealthCheck.cs   # basket fingerprint + active symbol count
├── Workers/
│   └── TiingoIngressWorker.cs
└── Program.cs
```

## Responsibilities

- Maintain a real Tiingo IEX WebSocket connection with bounded
  exponential-backoff reconnect.
- Warm up consumers with a REST snapshot batch on startup.
- Normalize provider payloads into `RawTickV1` and publish to both
  `market.raw_ticks.v1` (time-series) and `market.latest_by_symbol.v1`
  (compacted).
- Follow `refdata.basket.active.v1` for symbol subscription changes —
  no static symbol list by default.
- Surface upstream state and basket state on `/healthz/ready` and
  `/metrics`. Emitted observable gauges (wired by
  `Metrics/IngressMetrics.cs` and registered eagerly in `Program.cs`):
  - `hqqq_ingress_active_symbols` — current size of the basket-driven
    Tiingo subscription set.
  - `hqqq_ingress_basket_fingerprint_age_seconds` — wall-clock age of
    the active basket snapshot (`0` before the first basket).
  - `hqqq_ingress_published_ticks_total` — running count of ticks
    successfully produced to Kafka.
  - `hqqq_ingress_last_published_tick_timestamp` — unix seconds of the
    most recent successful publish (`0` if none yet).

  The same `publishedTickCount` and `lastPublishedTickUtc` are echoed in
  the `/healthz/ready` payload so smoke proofs can sample them twice
  across a window and assert tick growth without parsing Prometheus.

## Configuration

| Key | Purpose |
|-----|---------|
| `Tiingo:ApiKey` | **Required**. Service fails fast without a real key. |
| `Tiingo:WsUrl` | Tiingo IEX websocket URL. |
| `Tiingo:RestBaseUrl` | Tiingo IEX REST base URL (snapshot warmup). |
| `Tiingo:ReconnectBaseDelaySeconds` / `MaxReconnectDelaySeconds` | Reconnect backoff bounds. |
| `Tiingo:StaleAfterSeconds` | Health-check staleness threshold. |
| `Tiingo:Symbols` | **Optional bootstrap override** — only used when no basket arrives inside `Ingress:Basket:StartupWaitSeconds`. |
| `Ingress:Basket:Topic` | Default `refdata.basket.active.v1`. |
| `Ingress:Basket:ConsumerGroup` | Default `ingress-baskets`. |
| `Ingress:Basket:StartupWaitSeconds` | Default 60. Deadline for the first basket before falling back to `Tiingo:Symbols`. |
