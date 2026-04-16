# Hqqq.Observability

Shared observability: metrics definitions, tracing extensions,
structured logging helpers, and health payload builders.

## Contents

- `Metrics/`
  - `MetricNames` — central registry of metric name constants (OpenTelemetry-style naming)
  - `HqqqMetrics` — shared `Meter` and instrument instances (counters, histograms, gauges)
- `Logging/`
  - `LoggingExtensions` — `AddHqqqObservability` (registers `HqqqMetrics`) and `AddHqqqDefaults` (structured console logging)
- `Tracing/`
  - `TracingExtensions` — shared `ActivitySource` and helper for starting trace activities
- `Health/`
  - `HealthPayloadBuilder` — builds consistent JSON health response payloads from `HealthReport`
