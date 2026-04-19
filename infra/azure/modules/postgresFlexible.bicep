// ─────────────────────────────────────────────────────────────────
// Azure Database for PostgreSQL — Flexible Server, prepared to host
// the HQQQ TimescaleDB schema (`quote_snapshots`, etc.) consumed by
// `hqqq-gateway` (history reads), `hqqq-persistence` (writes), and
// `hqqq-analytics` (batch reads).
//
// The connection-string output uses the Npgsql ADO.NET shape the
// services already bind via `Timescale__ConnectionString`
// (kebab-case CA secret `timescale-connection-string`), so the
// runtime has no idea whether it is talking to a local container,
// a self-hosted server, or this Flexible Server.
//
// Hardening posture:
//   - SSL required (Npgsql connection string sets `SSL Mode=Require`).
//   - Password auth enabled, AAD auth disabled (matches existing
//     deploy-time-secret pattern; AAD auth + UAMI is a follow-up).
//   - `azure.extensions` server parameter ALLOW-LISTS TIMESCALEDB so
//     an operator can later run `CREATE EXTENSION` against the
//     target database. The module DOES NOT run `CREATE EXTENSION`
//     itself — that remains an explicit operator step (see
//     docs/phase2/azure-deploy.md §11). This satisfies the IaC
//     constraint of not auto-running destructive DB initialization.
//
// Dev-friendly defaults:
//   - SKU `Standard_B1ms` (Burstable, ~$13/mo) with 32 GiB storage.
//   - High availability + geo-redundant backup disabled.
//   - Public network access enabled with a single firewall entry that
//     allows traffic from any Azure service (required because the
//     Container Apps environment has no VNet integration in Phase 2).
// ─────────────────────────────────────────────────────────────────

@description('Server name (3-63 chars, lowercase alphanumerics + hyphen). Becomes the `<name>.postgres.database.azure.com` FQDN.')
@minLength(3)
@maxLength(63)
param name string

@description('Azure region.')
param location string

@description('Postgres major version. 16 is the latest GA on Flexible Server as of writing.')
@allowed([ '13', '14', '15', '16' ])
param version string = '16'

@description('Administrator login (cannot be `azure_superuser`, `admin`, etc.).')
param administratorLogin string = 'hqqqadmin'

@description('Administrator password. Must satisfy Azure complexity rules: 8-128 chars, 3 of {upper, lower, digits, non-alphanumerics}.')
@secure()
@minLength(8)
@maxLength(128)
param administratorLoginPassword string

@description('Server SKU name (e.g. Standard_B1ms, Standard_D2ds_v5). Pair with `skuTier`.')
param skuName string = 'Standard_B1ms'

@description('Server SKU tier.')
@allowed([ 'Burstable', 'GeneralPurpose', 'MemoryOptimized' ])
param skuTier string = 'Burstable'

@description('Storage size in GiB (Flexible Server minimum is 32).')
@minValue(32)
@maxValue(16384)
param storageSizeGB int = 32

@description('Backup retention in days.')
@minValue(7)
@maxValue(35)
param backupRetentionDays int = 7

@description('Database name created on the server. The TimescaleDB extension will live in this database after the operator runs CREATE EXTENSION.')
param databaseName string = 'hqqq'

@description('Firewall rules to apply to the server. Each item: { name: string, startIpAddress: string, endIpAddress: string }. The default opens the server to all Azure services (start=end=0.0.0.0), which is required because the Phase 2 Container Apps environment has no VNet integration.')
param firewallAllowedIpRanges array = [
  {
    name: 'AllowAllAzureServices'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
]

@description('Comma-separated value to write to the `azure.extensions` server parameter. Allow-listing only — the operator must run `CREATE EXTENSION IF NOT EXISTS timescaledb;` against the target database before any HQQQ schema migration can proceed.')
param allowedExtensions string = 'TIMESCALEDB,PG_STAT_STATEMENTS'

@description('Resource tags.')
param tags object = {}

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    version: version
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: {
      storageSizeGB: storageSizeGB
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
  }
}

resource db 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// `azure.extensions` is a static server parameter, so changing it
// triggers a server restart. The first deploy provisions it cleanly;
// follow-up deploys are no-ops if the value is unchanged.
resource azureExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'azure.extensions'
  properties: {
    source: 'user-override'
    value: allowedExtensions
  }
}

resource firewall 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = [for rule in firewallAllowedIpRanges: {
  parent: server
  name: rule.name
  properties: {
    startIpAddress: rule.startIpAddress
    endIpAddress: rule.endIpAddress
  }
}]

output id string = server.id
output serverName string = server.name
output fqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = db.name

@description('Npgsql ADO.NET connection string consumed by the gateway / persistence / analytics services via `Timescale__ConnectionString`. SSL is required by the server.')
#disable-next-line outputs-should-not-contain-secrets
@secure()
output connectionString string = 'Host=${server.properties.fullyQualifiedDomainName};Port=5432;Database=${db.name};Username=${administratorLogin};Password=${administratorLoginPassword};SSL Mode=Require;Trust Server Certificate=true'
