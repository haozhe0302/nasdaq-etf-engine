// ─────────────────────────────────────────────────────────────────
// Reference parameter file for the Phase 2 data tier. Same shape
// as data.demo.bicepparam, with placeholder values. Copy this
// file to a private location (e.g. data.<your-env>.bicepparam) and
// fill in real names.
//
// All names below MUST be replaced before use:
//   - redisName must be unique within `*.<region>.redisenterprise.cache.azure.net`.
//   - postgresServerName must be globally unique in `postgres.database.azure.com`.
//   - eventHubsNamespaceName must be globally unique in `servicebus.windows.net`.
//
// Secrets are intentionally absent. The deploy workflow injects
// `postgresAdministratorPassword` via --parameters at
// `az deployment group create` time.
// ─────────────────────────────────────────────────────────────────

using '../data.bicep'

param location = '<azure-region>'

param tags = {
  project: 'hqqq'
  phase: 'phase-2'
  managedBy: 'bicep'
  environment: '<your-env>'
  tier: 'data'
}

// ── Azure Managed Redis ─────────────────────────────────────────
param redisName = '<your-redis-name>'
param redisSku = 'Balanced_B0'

// ── PostgreSQL Flexible Server ──────────────────────────────────
param postgresServerName = '<your-postgres-server-name>'
param postgresVersion = '16'
param postgresAdministratorLogin = 'hqqqadmin'
// Required @secure() param — placeholder only; supply real value
// via --parameters at deploy time.
param postgresAdministratorPassword = 'OVERRIDE_ME'
param postgresSkuName = 'Standard_B1ms'
param postgresSkuTier = 'Burstable'
param postgresStorageSizeGB = 32
param postgresBackupRetentionDays = 7
param postgresDatabaseName = 'hqqq'
param postgresFirewallAllowedIpRanges = [
  {
    name: 'AllowAllAzureServices'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
]
param postgresAllowedExtensions = 'TIMESCALEDB,PG_STAT_STATEMENTS'

// ── Event Hubs namespace ────────────────────────────────────────
param eventHubsNamespaceName = '<your-event-hubs-namespace>'
param eventHubsSku = 'Standard'
param eventHubsCapacity = 1
// Topics inherit the data.bicep default (current HQQQ topology).
param eventHubsSharedAccessRuleName = 'hqqq-services'
