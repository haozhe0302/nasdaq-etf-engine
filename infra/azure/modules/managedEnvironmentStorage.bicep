// ─────────────────────────────────────────────────────────────────
// Attaches an Azure Files share as a managed-environment storage
// definition on an existing Container Apps environment, so any
// Container App in that environment can reference it as an
// `AzureFile` volume.
//
// This module is intentionally narrow: one share -> one storage
// definition. The quote-engine app references the resulting
// `storageName` from its `properties.template.volumes` block.
// ─────────────────────────────────────────────────────────────────

@description('Name of the existing Container Apps environment to attach storage to.')
param containerAppsEnvName string

@description('Storage definition name as it will appear inside the env. Used by container apps to reference the volume.')
param storageName string

@description('Storage account name backing the file share.')
param storageAccountName string

@description('Azure Files share name on that storage account.')
param fileShareName string

@description('Storage account key. Bound at deploy time via listKeys() in the parent template.')
@secure()
param storageAccountKey string

@description('Mount access mode. ReadWrite is required for the quote-engine to flush its checkpoint.')
@allowed([ 'ReadOnly', 'ReadWrite' ])
param accessMode string = 'ReadWrite'

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvName
}

resource envStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: cae
  name: storageName
  properties: {
    azureFile: {
      accountName: storageAccountName
      accountKey: storageAccountKey
      shareName: fileShareName
      accessMode: accessMode
    }
  }
}

output id string = envStorage.id
output name string = envStorage.name
