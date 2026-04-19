// ─────────────────────────────────────────────────────────────────
// Storage account + Azure Files share for the quote-engine
// checkpoint persistence path.
//
// Provisioned only when main.bicep is invoked with
// `quoteEngineCheckpointPersistence=true`. The resulting share is
// attached to the Container Apps environment by
// managedEnvironmentStorage.bicep and mounted into the quote-engine
// container so its checkpoint survives revision swaps and restarts.
//
// Hardening posture:
//   - StorageV2 + Standard_LRS (single-AZ is fine for a checkpoint
//     that the engine itself rebuilds on demand from Kafka).
//   - Public blob access disabled, TLS 1.2 minimum, OAuth-by-default
//     for any non-key clients.
//   - Shared-key access stays enabled because Container Apps
//     environment storage definitions authenticate to Azure Files
//     using the account key. Rotating the key requires re-running
//     the deploy (Bicep re-reads it via listKeys()).
// ─────────────────────────────────────────────────────────────────

@description('Globally-unique storage account name (3-24 chars, lowercase alphanumerics).')
@minLength(3)
@maxLength(24)
param name string

@description('Azure region.')
param location string

@description('Azure Files share name (3-63 chars, lowercase alphanumerics + hyphen).')
param fileShareName string

@description('File share quota in GiB. 100 is plenty for a JSON checkpoint; the floor for SMB shares is 1.')
@minValue(1)
@maxValue(102400)
param fileShareQuotaGiB int = 100

@description('Resource tags applied to the storage account.')
param tags object = {}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Enabled'
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {}
}

resource share 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: fileShareName
  properties: {
    accessTier: 'TransactionOptimized'
    shareQuota: fileShareQuotaGiB
    enabledProtocols: 'SMB'
  }
}

output id string = storage.id
output name string = storage.name
output fileShareName string = share.name

@description('Primary key for the storage account. Surface only into Container Apps env storage definitions and Container App secrets.')
@secure()
output primaryKey string = storage.listKeys().keys[0].value
