# Hqqq.Infrastructure

Shared infrastructure concerns used by multiple services.

## Contents

- `Serialization/`
  - `HqqqJsonDefaults` — canonical `JsonSerializerOptions` (camelCase, enum-as-string, null-ignoring) shared across all services for Kafka events and REST responses

## Planned (later phases)

- `Kafka/` — `KafkaProducerFactory`, `KafkaConsumerFactory`
- `Redis/` — `RedisConnectionFactory`
- `Timescale/` — `TimescaleConnectionFactory`
- `Hosting/` — `WorkerExtensions`
- `Health/` — `DependencyHealthChecks`
