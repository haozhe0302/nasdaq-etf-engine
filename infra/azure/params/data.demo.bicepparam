// ─────────────────────────────────────────────────────────────────
// Concrete parameter file for the Phase 2 "demo" data tier.
//
// Resource group (assumed pre-created, shared with the app tier):
//   rg-hqqq-p2-demo-eus-01
// Region:
//   eastus
//
// `postgresAdministratorPassword` is intentionally NOT set here. It
// is injected at deploy time by phase2-deploy.yml from the
// `phase2-demo` GitHub environment secret POSTGRES_ADMIN_PASSWORD.
// Never commit a real password to this repo.
//
// All other dev-friendly defaults (SKUs, capacities, topic
// partitions matching docs/phase2/topics.md) are inherited from
// data.bicep itself — this file only sets the per-environment
// names + the costCenter tag.
// ─────────────────────────────────────────────────────────────────

using '../data.bicep'

param location = 'eastus'

param tags = {
  project: 'hqqq'
  phase: 'phase-2'
  managedBy: 'bicep'
  environment: 'demo'
  costCenter: 'portfolio'
  tier: 'data'
}

// ── Azure Managed Redis ─────────────────────────────────────────
param redisName = 'redis-hqqq-p2-demo-eus-01'
param redisSku = 'Balanced_B0'

// ── PostgreSQL Flexible Server ──────────────────────────────────
param postgresServerName = 'psql-hqqq-p2-demo-eus-01'
param postgresVersion = '16'
param postgresAdministratorLogin = 'hqqqadmin'
// Placeholder so `bicep build` succeeds locally; the deploy workflow
// supplies the real password via --parameters. NEVER put real values
// here.
param postgresAdministratorPassword = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param postgresSkuName = 'Standard_B1ms'
param postgresSkuTier = 'Burstable'
param postgresStorageSizeGB = 32
param postgresBackupRetentionDays = 7
param postgresDatabaseName = 'hqqq'

// AllowAllAzureServices is required because the Phase 2 Container
// Apps environment has no VNet integration. Append the operator's
// home/office IP here when running `psql` from a workstation, e.g.:
//   { name: 'OperatorHome', startIpAddress: '203.0.113.10', endIpAddress: '203.0.113.10' }
param postgresFirewallAllowedIpRanges = [
  {
    name: 'AllowAllAzureServices'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
]

param postgresAllowedExtensions = 'TIMESCALEDB,PG_STAT_STATEMENTS'

// ── Event Hubs namespace ────────────────────────────────────────
param eventHubsNamespaceName = 'evhns-hqqq-p2-demo-eus-01'
param eventHubsSku = 'Standard'
param eventHubsCapacity = 1
// Topics inherit the data.bicep default (current HQQQ topology).
param eventHubsSharedAccessRuleName = 'hqqq-services'
