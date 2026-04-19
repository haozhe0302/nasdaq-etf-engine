// ─────────────────────────────────────────────────────────────────
// Event Hubs namespace + the six HQQQ Kafka topics + a single
// namespace-scoped Send,Listen SAS rule.
//
// Event Hubs exposes a Kafka-compatible surface on port 9093, which
// is exactly what the HQQQ services already speak via
// Confluent.Kafka. The connection string this module produces is the
// `Endpoint=sb://<ns>.servicebus.windows.net/;SharedAccessKeyName=...`
// shape that Event Hubs requires when used as a Kafka SASL password
// (with username literal `$ConnectionString`).
//
// Topics + partition counts default to the live HQQQ topology
// documented in `docs/phase2/topics.md`:
//
//   market.raw_ticks.v1         partitions=3   (symbol-keyed scale-out)
//   market.latest_by_symbol.v1  partitions=3   (compacted in OSS Kafka)
//   refdata.basket.active.v1    partitions=1   (compacted in OSS Kafka)
//   refdata.basket.events.v1    partitions=1
//   pricing.snapshots.v1        partitions=1
//   ops.incidents.v1            partitions=1
//
// IMPORTANT — log compaction caveat: Event Hubs **Standard** does
// NOT support Kafka log compaction. The two compacted HQQQ topics
// (`market.latest_by_symbol.v1`, `refdata.basket.active.v1`) will
// run with time-based retention only on this namespace. Operators
// who need true compaction parity should override the `sku` param
// to `Premium` (or `Dedicated`) per environment. Time-based
// retention is enough for the demo because the bootstrap path can
// rebuild compacted state from the source-of-truth topics.
//
// Hardening posture:
//   - SAS auth only (no AAD on the Kafka surface; Confluent.Kafka
//     binds SASL/PLAIN with the connection string).
//   - Local auth enabled (required for the Kafka surface).
//   - Auto-inflate disabled by default — operators opt in per env.
//
// Dev-friendly defaults:
//   - SKU `Standard`, capacity 1 throughput unit, zone redundancy off.
//   - Retention 1 day on every hub (matches OSS Kafka local default).
// ─────────────────────────────────────────────────────────────────

@description('Event Hubs namespace name (6-50 chars, alphanumerics + hyphen, must start with a letter). Becomes the `<name>.servicebus.windows.net` host.')
@minLength(6)
@maxLength(50)
param name string

@description('Azure region.')
param location string

@description('Event Hubs SKU. Standard supports the Kafka surface and per-hub consumer groups. Use Premium for log compaction parity with OSS Kafka.')
@allowed([ 'Basic', 'Standard', 'Premium' ])
param sku string = 'Standard'

@description('Throughput Units (Standard) or Processing Units (Premium). Capacity is fixed at 1 by default — auto-inflate stays off so cost is predictable.')
@minValue(1)
@maxValue(20)
param capacity int = 1

@description('Hubs (Kafka topics) to create. Defaults match the HQQQ topology in docs/phase2/topics.md. Each item: { name: string, partitionCount: int, messageRetentionInDays: int }.')
param topics array = [
  { name: 'market.raw_ticks.v1',        partitionCount: 3, messageRetentionInDays: 1 }
  { name: 'market.latest_by_symbol.v1', partitionCount: 3, messageRetentionInDays: 1 }
  { name: 'refdata.basket.active.v1',   partitionCount: 1, messageRetentionInDays: 1 }
  { name: 'refdata.basket.events.v1',   partitionCount: 1, messageRetentionInDays: 1 }
  { name: 'pricing.snapshots.v1',       partitionCount: 1, messageRetentionInDays: 1 }
  { name: 'ops.incidents.v1',           partitionCount: 1, messageRetentionInDays: 1 }
]

@description('Name of the namespace-scoped SAS authorization rule used by every HQQQ producer/consumer. Send + Listen rights only — Manage is NOT granted.')
param sharedAccessRuleName string = 'hqqq-services'

@description('Resource tags.')
param tags object = {}

resource namespace 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
    capacity: capacity
  }
  properties: {
    isAutoInflateEnabled: false
    kafkaEnabled: true
    zoneRedundant: false
    disableLocalAuth: false
    publicNetworkAccess: 'Enabled'
  }
}

resource hubs 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = [for topic in topics: {
  parent: namespace
  name: topic.name
  properties: {
    partitionCount: topic.partitionCount
    messageRetentionInDays: topic.messageRetentionInDays
  }
}]

resource sasRule 'Microsoft.EventHub/namespaces/authorizationRules@2024-01-01' = {
  parent: namespace
  name: sharedAccessRuleName
  properties: {
    rights: [
      'Send'
      'Listen'
    ]
  }
}

output id string = namespace.id
output namespaceName string = namespace.name

@description('Kafka bootstrap servers value the HQQQ services bind via `Kafka__BootstrapServers`. Event Hubs always exposes the Kafka surface on port 9093.')
output bootstrapServers string = '${namespace.name}.servicebus.windows.net:9093'

output topicNames array = [for (topic, i) in topics: hubs[i].name]

@description('Event Hubs namespace connection string used as the SASL password (with username literal `$ConnectionString`) by every HQQQ Kafka client.')
#disable-next-line outputs-should-not-contain-secrets
@secure()
output kafkaSaslPassword string = sasRule.listKeys().primaryConnectionString
