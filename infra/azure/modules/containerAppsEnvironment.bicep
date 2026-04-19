// ─────────────────────────────────────────────────────────────────
// Azure Container Apps environment that hosts every Phase 2 app.
//
// Bound to the Log Analytics workspace from logAnalytics.bicep so
// every container app's stdout/stderr lands in the same workspace.
// Internal apps reach each other over the env's internal DNS
// suffix (`*.internal.<defaultDomain>`).
// ─────────────────────────────────────────────────────────────────

@description('Container Apps environment name.')
param name string

@description('Azure region.')
param location string

@description('Log Analytics workspace customer ID (workspaceId).')
param logAnalyticsCustomerId string

@description('Log Analytics workspace primary shared key.')
@secure()
param logAnalyticsSharedKey string

@description('Resource tags.')
param tags object = {}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    zoneRedundant: false
  }
}

output id string = env.id
output name string = env.name
output defaultDomain string = env.properties.defaultDomain
