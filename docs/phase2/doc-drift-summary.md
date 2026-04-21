# Phase 2 — Documentation Drift Summary

A short, single-page record of the doc-drift cleanup pass against the
current codebase. Use this file as the next reader's "what was wrong,
what was fixed, what is intentionally still in here, and what is
honestly still a limitation" map. This is a documentation artifact —
no code changed.

## Major drift corrected (per file)

| File | Drift item | Fix |
|------|------------|-----|
| [`README.md`](../../README.md) | API contract table tagged `/api/basket/current` and `/api/basket/refresh` as `(Phase 1 today)` | Relabeled to "Phase 2: served by `hqqq-reference-data` internally (port 5020); not exposed by `hqqq-gateway` today". Phase 1 monolith still exposes them on the public-demo backend (noted). |
| [`README.md`](../../README.md) | "Known limitations" item 1 used the word "Hybrid" (collision with the deprecated "hybrid runtime mode") | Renamed to "Composite basket source" and explicitly clarified that "composite" is the basket-source qualifier (live drop + fallback seed), not a runtime mode. |
| [`docs/architecture.md`](../architecture.md) | §2 lead-in claimed the monolith "still owns" Tiingo ingestion, basket refresh, corp-action adjustment, and `/api/system/health` aggregation | Rewritten: those responsibilities are owned by Phase 2 services in the current runtime; the monolith retains its pre-Phase-2 implementations only as reference and still backs the public live demo Web App. |
| [`docs/runbook.md`](../runbook.md) | §§1–10 read as the default path with no scoping | Labeled §1 explicitly as "Phase 1 (legacy / public-demo) local run path" with a callout pointing readers wanting Phase 2 to skip to §11. |
| [`docs/runbook.md`](../runbook.md) | §11 intro stated "The legacy monolith is still the source of Tiingo ingestion, basket refresh, corp-action adjustment, and `/api/system/health` aggregation today." | Replaced with the truthful Phase 2 ownership statement; the monolith is not in the Phase 2 runtime path. |
| [`docs/runbook.md`](../runbook.md) | §11.3 listed `hqqq-ingress` with a `# still stub` comment | Replaced with "requires `Tiingo__ApiKey` (real Tiingo IEX websocket; fail-fast on missing/placeholder key)". |
| [`docs/runbook.md`](../runbook.md) | §11.6 "Intentionally deferred" carried "Real Tiingo ingestion in `hqqq-ingress`; issuer-feed + corporate-action pipeline in `hqqq-reference-data` (still live inside the legacy monolith)" | Deleted (shipped). Replaced with the genuinely-deferred items: holdings scrape adapters, wider corp-action coverage, replay/anomaly/backfill, HA infra, Phase 3. |
| [`docs/runbook.md`](../runbook.md) | §16 corp-action degraded-behavior row was tagged "(legacy)" | Relabeled to the Phase-2-native `hqqq-reference-data` composite provider, with the actual probe (`corporate-actions-fetch`), metric (`hqqq_refdata_corp_action_fetch_errors_total`), and `file+tiingo-degraded` lineage tag. |
| [`docs/phase2/restructure-notes.md`](restructure-notes.md) | Service skeleton table marked `hqqq-reference-data` as "Partial" and `hqqq-ingress` as "Stub with Tiingo + Kafka options" | Rewritten to current-state ("live" with the actual subsystems and topics each service owns). |
| [`docs/phase2/restructure-notes.md`](restructure-notes.md) | "What stayed as legacy" closed with "the legacy API is still the only source of real Tiingo ingestion, basket refresh, corporate-action adjustment, and `/api/system/health` aggregation today" | Replaced with the truthful framing: monolith is repo-only reference code, still backs the public demo Web App, not in the Phase 2 runtime path. |
| [`docs/phase2/restructure-notes.md`](restructure-notes.md) | "Current Phase 2 state (through C4)" gateway bullet said `/api/system/health` "still follows only the global `Gateway:DataSource` (stub or legacy forwarding)" | Replaced with the D1 native aggregator default; `legacy`/`stub` reframed as opt-in fallbacks. Added concrete `hqqq-reference-data` and `hqqq-ingress` blocks alongside the existing quote-engine/persistence/analytics blocks. Section retitled "through D6.6". |
| [`docs/phase2/restructure-notes.md`](restructure-notes.md) | "Still stubbed / transitional in the new services" subsection still listed ingress + reference-data as stubs | Subsection deleted. |
| [`docs/phase2/restructure-notes.md`](restructure-notes.md) | "Intentionally deferred" table listed real Tiingo ingestion + corp-action pipeline as future work | Removed the shipped items; trimmed the surviving items to honest deferreds (scrape adapters, wider corp-action coverage, replay/anomaly/backfill, HA, Phase 3). |
| [`docs/phase2/rollback.md`](rollback.md) | §5 degraded-behavior row "Corp-action provider failure (legacy monolith)" | Relabeled to "Phase 2 `hqqq-reference-data`" with the real fallback behavior (`file+tiingo-degraded` lineage on `adjustmentSummary.providerSource`, `corporate-actions-fetch` health probe, metric counter). |

## Stale phrases removed

- "still stub" / "Stub with Tiingo + Kafka options" / "Partial — config binding + in-memory basket repository"
- "issuer-feed + corporate-action pipeline … still live inside the legacy monolith"
- "Real Tiingo ingestion in `hqqq-ingress` … (still live inside the legacy monolith)"
- "/api/system/health still follows only the global `Gateway:DataSource` (stub or legacy forwarding)"
- "the legacy API is still the only source of real Tiingo ingestion, basket refresh, corporate-action adjustment, and `/api/system/health` aggregation today"
- "Corp-action provider failure (legacy monolith) … pricing falls back to unadjusted shares"

## Phrases intentionally retained (and why)

- **`legacy` / `stub` gateway source modes** — still real opt-in code paths for offline UI smoke and side-by-side parity testing against a separately-running monolith. Kept in [`src/services/hqqq-gateway/README.md`](../../src/services/hqqq-gateway/README.md), [`docs/architecture.md`](../architecture.md) §4.5, [`docs/phase2/local-dev.md`](local-dev.md), and [`docs/phase2/rollback.md`](rollback.md). Defaults are `redis` / `redis` / `timescale` / `aggregated`.
- **"Composite basket source" / "Hybrid basket" wording in `README.md` Known Limitations** — kept as the basket-source qualifier (composite live drop + fallback seed in Phase 2; scraped sources in the Phase 1 monolith). Explicitly clarified that this is the basket source, not the deprecated runtime mode.
- **Phase 1 monolith narrative across `README.md`, `docs/architecture.md` §2, `docs/runbook.md` §§1–10** — kept because the monolith still backs the public live demo links (Azure App Service Web App). It is repo-only reference for the Phase 2 runtime; it is not deleted because the demo URL still routes there.
- **`OperatingMode` / `HQQQ_OPERATING_MODE` env** — retained as a logging-posture tag for cross-service log consistency. Documented in [`README.md`](../../README.md), [`docs/architecture.md`](../architecture.md), [`docs/phase2/config-matrix.md`](config-matrix.md), and [`docs/phase2/azure-deploy.md`](azure-deploy.md) as such; explicitly noted that it no longer branches runtime behaviour.

## Honest remaining limitations (unchanged by this pass)

These are real and still in the docs deliberately:

- **Single-instance Phase 2 workers.** D5 only duplicates the gateway. `hqqq-ingress`, `hqqq-reference-data`, `hqqq-quote-engine`, `hqqq-persistence` are singletons by design today.
- **Stateful infra is single-instance** in the demo environment. No HA Kafka / Redis / Timescale topology.
- **Corporate-action scope is narrow** by design: forward / reverse splits, ticker renames (chained), constituent transition detection, scale-factor continuity. Dividends, spin-offs, mergers, cross-exchange moves, and ISIN/CUSIP-level remaps are explicitly out of scope. Splits adjust `SharesHeld` only; `ReferencePrice` is left for the next holdings refresh (known approximation).
- **Live holdings source set is narrow.** `hqqq-reference-data`'s `LiveHoldingsSource` supports `File` and `Http` drops only; the legacy provider-specific scrapers (Schwab / StockAnalysis / AlphaVantage / Nasdaq) stay in the monolith as reference until ported behind `IHoldingsSource`.
- **Fallback seed `asOfDate`** is pinned at build time in `Resources/basket-seed.json` — credible stand-in, not a live market snapshot.
- **`marketPrice` is a QQQ proxy.** HQQQ does not trade; premium/discount is computed against live QQQ.
- **`hqqq-analytics`** is one-shot report mode only. Replay / backfill / anomaly detection are deferred seams.
- **Azure quote-engine checkpoint persistence** is opt-in via the `quoteEngineCheckpointPersistence` bicepparam toggle. With it off, the checkpoint sits on ephemeral `/tmp` and is lost on revision swap.
- **No scheduled trigger for the analytics Container Apps Job** today — `triggerType=Manual`, dispatched per execution.
- **Phase 3 (Kubernetes app-tier operationalization)** is deferred. Phase 2's cloud target is Azure Container Apps.
