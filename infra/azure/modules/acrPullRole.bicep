// ─────────────────────────────────────────────────────────────────
// AcrPull role assignment so the user-assigned MI used by every
// Container App can pull images from the Phase 2 ACR without
// admin credentials.
//
// Scope: the registry resource itself (narrowest viable scope).
// ─────────────────────────────────────────────────────────────────

@description('Name of the ACR to grant pull on. Must already exist in this resource group.')
param acrName string

@description('Object/principal ID of the user-assigned managed identity that needs AcrPull.')
param principalId string

// 7f951dda-4ed3-4680-a7ca-43fe172d538d is the built-in AcrPull role definition ID.
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: acrName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, principalId, acrPullRoleDefinitionId)
  scope: registry
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

output roleAssignmentId string = assignment.id
