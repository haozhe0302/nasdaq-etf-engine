# Hqqq.Infrastructure

Shared infrastructure concerns used by multiple services.

## Contents

- `Serialization/`
  - `HqqqJsonDefaults` — canonical `JsonSerializerOptions` (camelCase, enum-as-string, null-ignoring) shared across all services for Kafka events and REST responses
- `Kafka/`
  - `KafkaTopics` — central `const string` registry of all Kafka topic names
  - `KafkaOptions` — connection settings (BootstrapServers, ConsumerGroupPrefix, SchemaRegistryUrl)
- `Redis/`
  - `RedisKeys` — key pattern constants and builder methods for all Redis keys and channels
  - `RedisOptions` — connection settings (Configuration)
- `Timescale/`
  - `TimescaleOptions` — connection settings (ConnectionString)

## Planned (later phases)

- `Kafka/` — `KafkaProducerFactory`, `KafkaConsumerFactory`
- `Redis/` — `RedisConnectionFactory`
- `Timescale/` — `TimescaleConnectionFactory`
- `Hosting/` — `WorkerExtensions`
- `Health/` — `DependencyHealthChecks`
