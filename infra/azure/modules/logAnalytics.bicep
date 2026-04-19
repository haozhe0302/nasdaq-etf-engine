// ─────────────────────────────────────────────────────────────────
// Log Analytics workspace backing the Container Apps environment.
//
// Container Apps requires a workspace for stdout/stderr collection
// and platform diagnostics. We keep retention low and SKU
// PerGB2018 so the demo footprint stays cheap.
// ─────────────────────────────────────────────────────────────────

@description('Log Analytics workspace name.')
param name string

@description('Azure region.')
param location string

@description('Retention period in days. 30 is the minimum allowed.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Resource tags.')
param tags object = {}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output id string = workspace.id
output name string = workspace.name
output customerId string = workspace.properties.customerId
#disable-next-line outputs-should-not-contain-secrets
output primarySharedKey string = workspace.listKeys().primarySharedKey
