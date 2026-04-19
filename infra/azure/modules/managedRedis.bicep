// ─────────────────────────────────────────────────────────────────
// Azure Managed Redis (Enterprise resource provider) — single-cluster,
// single-database for the HQQQ data tier.
//
// Used by:
//   - hqqq-gateway       (read latest quotes + pub/sub `hqqq:channel:quote-update`)
//   - hqqq-quote-engine  (write latest quotes + publish on the channel)
//
// The connection-string output is shaped to match the
// StackExchange.Redis configuration the apps already bind via
// `Redis__Configuration` (kebab-case CA secret `redis-configuration`).
//
// Hardening posture:
//   - TLS-only (clientProtocol=Encrypted, port 10000).
//   - `EnterpriseCluster` policy (single logical Redis endpoint
//     fronting the cluster, which is what StackExchange.Redis can
//     speak without OSS-cluster awareness).
//   - Access key surfaced via listKeys() inside the module so the
//     secret never appears as a non-secure module input.
//
// Dev-friendly defaults:
//   - SKU `Balanced_B0` (cheapest GA Managed Redis SKU). Operators
//     can override per environment via the `sku` param.
//   - HA disabled at this SKU (Balanced_B0 is single-AZ by design).
// ─────────────────────────────────────────────────────────────────

@description('Cluster name (1-60 chars, lowercase alphanumerics + hyphen).')
@minLength(1)
@maxLength(60)
param name string

@description('Azure region.')
param location string

@description('Managed Redis SKU. Balanced_B0 is the smallest GA tier and is the dev-friendly default. Override per environment for prod sizing.')
param sku string = 'Balanced_B0'

@description('Minimum TLS version accepted by the cluster.')
@allowed([ '1.0', '1.1', '1.2' ])
param minimumTlsVersion string = '1.2'

@description('Database eviction policy when the dataset exceeds memory. NoEviction is the safest default for a quote cache where stale entries are preferable to silent loss.')
@allowed([
  'AllKeysLFU'
  'AllKeysLRU'
  'AllKeysRandom'
  'VolatileLRU'
  'VolatileLFU'
  'VolatileTTL'
  'VolatileRandom'
  'NoEviction'
])
param evictionPolicy string = 'NoEviction'

@description('Resource tags.')
param tags object = {}

resource cluster 'Microsoft.Cache/redisEnterprise@2024-10-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    minimumTlsVersion: minimumTlsVersion
  }
}

resource database 'Microsoft.Cache/redisEnterprise/databases@2024-10-01' = {
  parent: cluster
  // Managed Redis Enterprise enforces a single database per cluster
  // and the name must be the literal 'default'.
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'EnterpriseCluster'
    evictionPolicy: evictionPolicy
  }
}

output id string = cluster.id
output name string = cluster.name
output host string = cluster.properties.hostName
output sslPort int = database.properties.port

@description('StackExchange.Redis-format connection string consumed by the gateway / quote-engine via `Redis__Configuration`.')
#disable-next-line outputs-should-not-contain-secrets
@secure()
output connectionString string = '${cluster.properties.hostName}:${database.properties.port},password=${database.listKeys().primaryKey},ssl=True,abortConnect=False'
