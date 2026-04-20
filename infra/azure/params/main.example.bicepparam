// ─────────────────────────────────────────────────────────────────
// Reference parameter file — same shape as main.demo.bicepparam,
// with placeholder values. Copy this file to a private location
// (e.g. main.<your-env>.bicepparam) and fill in real names.
//
// All names below MUST be replaced before use:
//   - acrName must be globally unique in azurecr.io.
//   - Container app names must be unique within the resource group.
//
// Secrets are intentionally absent. The deploy workflow injects
// them via --parameters at `az deployment group create` time.
// ─────────────────────────────────────────────────────────────────

using '../main.bicep'

param location = '<azure-region>'

param tags = {
  project: 'hqqq'
  phase: 'phase-2'
  managedBy: 'bicep'
  environment: '<your-env>'
}

param acrName = '<your-acr-name>'
param acrSku = 'Basic'

param logAnalyticsName = '<your-log-analytics-name>'
param logAnalyticsRetentionInDays = 30

param managedIdentityName = '<your-managed-identity-name>'

param containerAppsEnvName = '<your-container-apps-env-name>'

param gatewayAppName = '<your-gateway-app-name>'
param referenceDataAppName = '<your-refdata-app-name>'
param ingressAppName = '<your-ingress-app-name>'
param quoteEngineAppName = '<your-quote-engine-app-name>'
param persistenceAppName = '<your-persistence-app-name>'
param analyticsJobName = '<your-analytics-job-name>'

// Optional persistent-checkpoint mount for the quote-engine. Off by
// default in this template — flip to true and set a globally-unique
// storage account name to provision a Standard_LRS storage account +
// Azure Files share, attach it to the Container Apps environment,
// and mount it inside the quote-engine container so checkpoint state
// survives revision swaps and replica restarts.
param quoteEngineCheckpointPersistence = false
param quoteEngineStorageAccountName = '<your-storage-account-name>'
param quoteEngineFileShareName = 'quote-engine-checkpoint'
param quoteEngineEnvStorageName = 'quote-engine-storage'
param quoteEngineMountPath = '/mnt/quote-engine'
param quoteEngineFileShareQuotaGiB = 100

param imageTag = 'latest'

param kafkaClientId = 'hqqq-azure'
param kafkaConsumerGroupPrefix = 'hqqq'
param gatewayBasketId = 'HQQQ'

// Operating mode for Phase 2 services.
//   hybrid     : legacy hqqq-api monolith bridges live ticks/baskets;
//                ingress + reference-data run as no-op stubs.
//   standalone : Phase 2 native ingress + deterministic basket seed
//                drive the system end-to-end. Requires tiingoApiKey.
param operatingMode = 'hybrid'

// Required @secure() params — placeholders only; supply real values
// via --parameters at deploy time:
param kafkaBootstrapServers = 'OVERRIDE_ME'
param kafkaSecurityProtocol = 'OVERRIDE_ME'
param kafkaSaslMechanism = 'OVERRIDE_ME'
param kafkaSaslUsername = 'OVERRIDE_ME'
param kafkaSaslPassword = 'OVERRIDE_ME'
param redisConfiguration = 'OVERRIDE_ME'
param timescaleConnectionString = 'OVERRIDE_ME'
param tiingoApiKey = ''
