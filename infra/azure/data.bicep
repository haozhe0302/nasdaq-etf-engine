// ─────────────────────────────────────────────────────────────────
// Phase 2 data + messaging tier — Azure deployment orchestrator.
//
// Scope: resource group. The resource group itself is assumed to be
// pre-created by the operator (one-time `az group create`). See
// infra/azure/README.md for the bootstrap commands.
//
// What this template provisions:
//   - Azure Managed Redis (Microsoft.Cache/redisEnterprise) for the
//     gateway / quote-engine quote cache + pub/sub channel.
//   - Azure Database for PostgreSQL Flexible Server, with the
//     `azure.extensions` server parameter allow-listing TIMESCALEDB
//     so the operator can `CREATE EXTENSION` against the `hqqq`
//     database that this template creates. The CREATE EXTENSION
//     itself is intentionally left as an operator step — see
//     docs/phase2/azure-deploy.md §11.
//   - Azure Event Hubs namespace (Kafka surface on :9093) plus the
//     six HQQQ topics defined in docs/phase2/topics.md, plus a
//     namespace-scoped Send,Listen SAS rule.
//
// What this template does NOT provision:
//   - Anything in the app tier — that lives in main.bicep and is
//     deployed separately. Two-template separation is intentional
//     so app-tier-only redeploys (the common case for code
//     iteration) never touch stateful resources.
//
// Outputs: every connection string the app tier consumes
// (`redisConfiguration`, `timescaleConnectionString`,
// `kafkaBootstrapServers`, `kafkaSecurityProtocol`,
// `kafkaSaslMechanism`, `kafkaSaslUsername`, `kafkaSaslPassword`)
// is surfaced here so the deploy workflow can pipe them straight
// into the main.bicep deployment as `--parameters`.
// ─────────────────────────────────────────────────────────────────

targetScope = 'resourceGroup'

// ── Region + tags ────────────────────────────────────────────────

@description('Azure region for every resource provisioned by this template.')
param location string = resourceGroup().location

@description('Common tags applied to every resource.')
param tags object = {
  project: 'hqqq'
  phase: 'phase-2'
  managedBy: 'bicep'
  environment: 'demo'
  tier: 'data'
}

// ── Resource names (no defaults — must be set via .bicepparam) ──

@description('Azure Managed Redis cluster name.')
param redisName string

@description('Managed Redis SKU. Balanced_B0 is the cheapest GA tier.')
param redisSku string = 'Balanced_B0'

@description('PostgreSQL Flexible Server name.')
param postgresServerName string

@description('PostgreSQL major version.')
@allowed([ '13', '14', '15', '16' ])
param postgresVersion string = '16'

@description('PostgreSQL administrator login.')
param postgresAdministratorLogin string = 'hqqqadmin'

@description('PostgreSQL administrator password (8-128 chars, must satisfy Azure complexity rules).')
@secure()
@minLength(8)
@maxLength(128)
param postgresAdministratorPassword string

@description('PostgreSQL SKU name.')
param postgresSkuName string = 'Standard_B1ms'

@description('PostgreSQL SKU tier.')
@allowed([ 'Burstable', 'GeneralPurpose', 'MemoryOptimized' ])
param postgresSkuTier string = 'Burstable'

@description('PostgreSQL storage size in GiB.')
@minValue(32)
@maxValue(16384)
param postgresStorageSizeGB int = 32

@description('PostgreSQL backup retention in days.')
@minValue(7)
@maxValue(35)
param postgresBackupRetentionDays int = 7

@description('Database created on the Postgres server. The TimescaleDB extension lives in this database after CREATE EXTENSION.')
param postgresDatabaseName string = 'hqqq'

@description('Firewall rules applied to the Postgres server. Default opens it to all Azure services (required for Container Apps without VNet integration). Append operator IP ranges per environment.')
param postgresFirewallAllowedIpRanges array = [
  {
    name: 'AllowAllAzureServices'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
]

@description('Comma-separated allow-list for the `azure.extensions` server parameter. TIMESCALEDB must be present for the operator CREATE EXTENSION step to succeed.')
param postgresAllowedExtensions string = 'TIMESCALEDB,PG_STAT_STATEMENTS'

@description('Event Hubs namespace name.')
param eventHubsNamespaceName string

@description('Event Hubs SKU. Standard supports the Kafka surface; use Premium for log compaction parity with OSS Kafka.')
@allowed([ 'Basic', 'Standard', 'Premium' ])
param eventHubsSku string = 'Standard'

@description('Event Hubs throughput / processing units.')
@minValue(1)
@maxValue(20)
param eventHubsCapacity int = 1

@description('Hubs (Kafka topics) to create. Defaults match docs/phase2/topics.md exactly.')
param eventHubsTopics array = [
  { name: 'market.raw_ticks.v1',        partitionCount: 3, messageRetentionInDays: 1 }
  { name: 'market.latest_by_symbol.v1', partitionCount: 3, messageRetentionInDays: 1 }
  { name: 'refdata.basket.active.v1',   partitionCount: 1, messageRetentionInDays: 1 }
  { name: 'refdata.basket.events.v1',   partitionCount: 1, messageRetentionInDays: 1 }
  { name: 'pricing.snapshots.v1',       partitionCount: 1, messageRetentionInDays: 1 }
  { name: 'ops.incidents.v1',           partitionCount: 1, messageRetentionInDays: 1 }
]

@description('Name of the namespace-scoped Send,Listen SAS rule. The connection string of this rule is what every HQQQ Kafka client uses as the SASL password.')
param eventHubsSharedAccessRuleName string = 'hqqq-services'

// ── Modules ──────────────────────────────────────────────────────

module redisModule 'modules/managedRedis.bicep' = {
  name: 'data-redis'
  params: {
    name: redisName
    location: location
    sku: redisSku
    tags: tags
  }
}

module postgresModule 'modules/postgresFlexible.bicep' = {
  name: 'data-postgres'
  params: {
    name: postgresServerName
    location: location
    version: postgresVersion
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
    skuName: postgresSkuName
    skuTier: postgresSkuTier
    storageSizeGB: postgresStorageSizeGB
    backupRetentionDays: postgresBackupRetentionDays
    databaseName: postgresDatabaseName
    firewallAllowedIpRanges: postgresFirewallAllowedIpRanges
    allowedExtensions: postgresAllowedExtensions
    tags: tags
  }
}

module eventHubsModule 'modules/eventHubsNamespace.bicep' = {
  name: 'data-eventhubs'
  params: {
    name: eventHubsNamespaceName
    location: location
    sku: eventHubsSku
    capacity: eventHubsCapacity
    topics: eventHubsTopics
    sharedAccessRuleName: eventHubsSharedAccessRuleName
    tags: tags
  }
}

// ── Outputs ─────────────────────────────────────────────────────
//
// Plain (non-secret) structural outputs — surfaced so the deploy
// workflow can render a readable summary table without re-querying
// the resources.

output redisHost string = redisModule.outputs.host
output redisSslPort int = redisModule.outputs.sslPort
output postgresServerName string = postgresModule.outputs.serverName
output postgresFqdn string = postgresModule.outputs.fqdn
output postgresDatabaseName string = postgresModule.outputs.databaseName
output postgresAdministratorLogin string = postgresAdministratorLogin
output eventHubsNamespaceName string = eventHubsModule.outputs.namespaceName
output eventHubsTopicNames array = eventHubsModule.outputs.topicNames

// Static literals the apps need on the Kafka side. Surfaced as
// outputs (not just hardcoded in the workflow) so the data tier
// is the single source of truth: if Event Hubs ever changes its
// supported SASL mechanism, only this template moves.

output kafkaSecurityProtocol string = 'SaslSsl'
output kafkaSaslMechanism string = 'Plain'
output kafkaSaslUsername string = '$ConnectionString'

// Secret outputs — consumed by phase2-deploy.yml's provision-data
// job and forwarded into main.bicep's @secure() params via
// `az deployment group create --parameters`.

@description('StackExchange.Redis connection string for Redis__Configuration.')
#disable-next-line outputs-should-not-contain-secrets
@secure()
output redisConfiguration string = redisModule.outputs.connectionString

@description('Npgsql ADO.NET connection string for Timescale__ConnectionString.')
#disable-next-line outputs-should-not-contain-secrets
@secure()
output timescaleConnectionString string = postgresModule.outputs.connectionString

@description('Event Hubs Kafka bootstrap host (`<ns>.servicebus.windows.net:9093`).')
#disable-next-line outputs-should-not-contain-secrets
@secure()
output kafkaBootstrapServers string = eventHubsModule.outputs.bootstrapServers

@description('Event Hubs namespace connection string used as the Kafka SASL password.')
#disable-next-line outputs-should-not-contain-secrets
@secure()
output kafkaSaslPassword string = eventHubsModule.outputs.kafkaSaslPassword
