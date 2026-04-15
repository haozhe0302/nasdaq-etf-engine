# hqqq-ingress

Tiingo WebSocket/REST adapter. Normalizes raw market ticks and publishes them
to Kafka.

**Future home of current `MarketData` module.**

## Current status (stub skeleton)

The service compiles and runs with stub clients and a logging publisher.
No real WebSocket, REST, or Kafka wiring yet.

### Behavior on startup

- `TiingoIngressWorker` starts and checks for `Tiingo:ApiKey` in configuration
- If no API key is configured, logs a warning and exits gracefully
- Otherwise enters an idle loop awaiting symbol subscription (Phase 2B)

## Folder structure

```
hqqq-ingress/
├── Configuration/      # Options/config classes
│   └── TiingoOptions.cs
├── Clients/            # Provider client interfaces + stubs
│   ├── ITiingoStreamClient.cs
│   ├── StubTiingoStreamClient.cs
│   ├── ITiingoSnapshotClient.cs
│   └── StubTiingoSnapshotClient.cs
├── Normalization/      # Raw payload -> canonical event mapping
│   └── TiingoQuoteNormalizer.cs
├── Publishing/         # Event bus abstraction (future: Kafka)
│   ├── ITickPublisher.cs
│   └── LoggingTickPublisher.cs
├── State/              # Ingestion runtime state tracking
│   └── IngestionState.cs
├── Workers/            # Hosted background workers
│   └── TiingoIngressWorker.cs
└── Program.cs
```

## Responsibilities (Phase 2 -- planned)

- Maintain Tiingo WebSocket connection and REST fallback
- Normalize provider payloads into internal event format
- Enrich with `providerTs`, `ingressTs`, `seq`, `symbol`, `provider`
- Publish to Kafka: `market.raw_ticks.v1`, `market.latest_by_symbol.v1` (compacted)
