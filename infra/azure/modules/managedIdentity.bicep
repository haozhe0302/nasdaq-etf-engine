// ─────────────────────────────────────────────────────────────────
// User-assigned managed identity used by every Phase 2 container
// app + the analytics job for ACR image pull (and as a placeholder
// for future RBAC against Key Vault / Postgres / Redis when those
// move into Bicep).
// ─────────────────────────────────────────────────────────────────

@description('User-assigned managed identity name.')
param name string

@description('Azure region.')
param location string

@description('Resource tags.')
param tags object = {}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: name
  location: location
  tags: tags
}

output id string = identity.id
output name string = identity.name
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
