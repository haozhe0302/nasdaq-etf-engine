// ─────────────────────────────────────────────────────────────────
// Azure Container Registry for the Phase 2 app tier.
//
// Hardening posture:
//   - adminUserEnabled = false. Image pull is wired through the
//     user-assigned managed identity + AcrPull role assignment in
//     acrPullRole.bicep. Image push is wired through GitHub OIDC
//     federated credentials with AcrPush at the registry scope.
//   - Public network access stays enabled (Container Apps in this
//     phase reach ACR over the public endpoint). Tightening to a
//     private endpoint is deferred to a later phase.
// ─────────────────────────────────────────────────────────────────

@description('Globally-unique ACR name (5-50 chars, lowercase alphanumerics).')
param name string

@description('Azure region for the registry.')
param location string

@description('SKU. Basic is fine for a portfolio demo; Premium unlocks geo-replication / private endpoints later.')
@allowed([ 'Basic', 'Standard', 'Premium' ])
param sku string = 'Basic'

@description('Resource tags applied to the registry.')
param tags object = {}

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    anonymousPullEnabled: false
    zoneRedundancy: 'Disabled'
  }
}

output id string = registry.id
output name string = registry.name
output loginServer string = registry.properties.loginServer
