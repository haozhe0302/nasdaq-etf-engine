# Hqqq.Infrastructure

Shared infrastructure concerns used by multiple services.

## Contents

- `Serialization/`
  - `HqqqJsonDefaults` — canonical `JsonSerializerOptions` (camelCase, enum-as-string, null-ignoring) shared across all services for Kafka events and REST responses
- `Kafka/`
  - `KafkaTopics` — central `const string` registry of all Kafka topic names
  - `KafkaOptions` — connection settings (BootstrapServers, ClientId, ConsumerGroupPrefix, SchemaRegistryUrl)
  - `KafkaTopicMetadata` / `KafkaTopicRegistry` — topic metadata (partitions, compaction) for deterministic bootstrap
  - `KafkaBootstrap` — idempotent topic creation using the Confluent admin client
  - `KafkaConfigBuilder` — minimal producer/consumer config builders from shared options
- `Redis/`
  - `RedisKeys` — key pattern constants and builder methods for all Redis keys and channels
  - `RedisOptions` — connection settings (Configuration)
  - `RedisConnectionFactory` — async `IConnectionMultiplexer` factory
- `Timescale/`
  - `TimescaleOptions` — connection settings (ConnectionString)
  - `TimescaleConnectionFactory` — `NpgsqlDataSource` factory with masked logging
- `Hosting/`
  - `ServiceRegistrationExtensions` — shared `AddHqqqKafka`, `AddHqqqRedis`, `AddHqqqTimescale` DI helpers and startup posture logging
  - `LegacyConfigShim` — bridges legacy flat env vars to hierarchical config keys with deprecation warnings
- `Health/`
  - `KafkaHealthCheck`, `RedisHealthCheck`, `TimescaleHealthCheck` — degraded-not-crashed dependency health checks
