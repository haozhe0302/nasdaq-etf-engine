// ─────────────────────────────────────────────────────────────────
// Phase 2 app-tier — Azure deployment orchestrator.
//
// Scope: resource group. The resource group itself is assumed to be
// pre-created by the operator (one-time `az group create`). See
// infra/azure/README.md for the bootstrap commands.
//
// What this template provisions:
//   - Azure Container Registry (Phase 2-isolated; legacy Phase 1
//     ACR is untouched)
//   - Log Analytics workspace
//   - User-assigned managed identity + AcrPull on the new ACR
//   - Container Apps environment bound to the Log Analytics workspace
//   - 5 long-running Container Apps:
//       gateway (external), reference-data (internal),
//       ingress / quote-engine / persistence (internal :8081)
//   - 1 Container Apps Job: hqqq-analytics (Manual trigger)
//
// What this template does NOT provision (assumed external, passed
// in via @secure() params):
//   - Kafka cluster (managed Kafka surface or self-hosted)
//   - Redis (Azure Cache for Redis or equivalent)
//   - PostgreSQL/TimescaleDB
//   See "Explicitly deferred" in the D4 plan for the rationale.
//
// Naming: every resource name is a parameter. Concrete names live
// only in the .bicepparam files under params/. To work around an
// ACR global-uniqueness collision (or to fork to a second
// environment) only the param file changes.
// ─────────────────────────────────────────────────────────────────

targetScope = 'resourceGroup'

// ── Region + tags ────────────────────────────────────────────────

@description('Azure region for every resource provisioned by this template.')
param location string = resourceGroup().location

@description('Common tags applied to every resource.')
param tags object = {
  project: 'hqqq'
  phase: 'phase-2'
  managedBy: 'bicep'
  environment: 'demo'
}

// ── Resource names (no defaults — must be set via .bicepparam) ──

@description('Globally-unique ACR name (5-50 chars, lowercase alphanumerics).')
param acrName string

@description('SKU for the new ACR.')
@allowed([ 'Basic', 'Standard', 'Premium' ])
param acrSku string = 'Basic'

@description('Log Analytics workspace name.')
param logAnalyticsName string

@description('Log Analytics retention in days.')
@minValue(30)
@maxValue(730)
param logAnalyticsRetentionInDays int = 30

@description('User-assigned managed identity name (used for ACR pull by every app + the job).')
param managedIdentityName string

@description('Container Apps environment name.')
param containerAppsEnvName string

@description('Container App name for the gateway (external ingress).')
param gatewayAppName string

@description('Container App name for reference-data (internal ingress :8080).')
param referenceDataAppName string

@description('Container App name for ingress (internal ingress :8081).')
param ingressAppName string

@description('Container App name for quote-engine (internal ingress :8081).')
param quoteEngineAppName string

@description('Container App name for persistence (internal ingress :8081).')
param persistenceAppName string

@description('Container Apps Job name for analytics.')
param analyticsJobName string

// ── Image tag ────────────────────────────────────────────────────

@description('Image tag applied to every Phase 2 service image (e.g. vsha-abcdef0 or latest). All six services share one tag in D4 to keep deploys atomic.')
param imageTag string = 'latest'

// ── Per-app resource sizing (overridable) ────────────────────────

@description('CPU cores per replica for the gateway.')
param gatewayCpu string = '0.5'
@description('Memory per replica for the gateway.')
param gatewayMemory string = '1.0Gi'
@description('Min replicas for the gateway.')
param gatewayMinReplicas int = 1
@description('Max replicas for the gateway.')
param gatewayMaxReplicas int = 2

@description('CPU cores per replica for reference-data.')
param refDataCpu string = '0.25'
@description('Memory per replica for reference-data.')
param refDataMemory string = '0.5Gi'
@description('Min replicas for reference-data.')
param refDataMinReplicas int = 1
@description('Max replicas for reference-data.')
param refDataMaxReplicas int = 2

@description('CPU cores per replica for ingress worker.')
param ingressCpu string = '0.25'
@description('Memory per replica for ingress worker.')
param ingressMemory string = '0.5Gi'
@description('Min replicas for ingress worker.')
param ingressMinReplicas int = 1
@description('Max replicas for ingress worker. Should typically be 1 for a singleton ingester.')
param ingressMaxReplicas int = 1

@description('CPU cores per replica for quote-engine worker.')
param quoteEngineCpu string = '0.5'
@description('Memory per replica for quote-engine worker.')
param quoteEngineMemory string = '1.0Gi'
@description('Min replicas for quote-engine worker.')
param quoteEngineMinReplicas int = 1
@description('Max replicas for quote-engine worker. Should typically be 1 to keep checkpoint state coherent.')
param quoteEngineMaxReplicas int = 1

@description('CPU cores per replica for persistence worker.')
param persistenceCpu string = '0.25'
@description('Memory per replica for persistence worker.')
param persistenceMemory string = '0.5Gi'
@description('Min replicas for persistence worker.')
param persistenceMinReplicas int = 1
@description('Max replicas for persistence worker.')
param persistenceMaxReplicas int = 2

@description('CPU cores per replica for the analytics job.')
param analyticsCpu string = '0.5'
@description('Memory per replica for the analytics job.')
param analyticsMemory string = '1.0Gi'
@description('Analytics job replica timeout in seconds.')
param analyticsReplicaTimeoutSeconds int = 1800
@description('Analytics job replica retry limit.')
param analyticsReplicaRetryLimit int = 1

// ── External dependency endpoints (secrets) ──────────────────────

@description('Kafka bootstrap servers (host:port[,host:port]). Required by ingress, quote-engine, persistence, reference-data.')
@secure()
param kafkaBootstrapServers string

@description('Redis connection string. Required by gateway, quote-engine.')
@secure()
param redisConfiguration string

@description('TimescaleDB / Postgres ADO.NET connection string. Required by gateway, persistence, analytics.')
@secure()
param timescaleConnectionString string

@description('Tiingo API key. Required by ingress.')
@secure()
param tiingoApiKey string = ''

// ── Generic non-secret app config ────────────────────────────────

@description('Kafka client identifier prefix.')
param kafkaClientId string = 'hqqq-azure'

@description('Kafka consumer group prefix.')
param kafkaConsumerGroupPrefix string = 'hqqq'

@description('Default basket ID surfaced by the gateway.')
param gatewayBasketId string = 'HQQQ'

// ── Provision platform resources ─────────────────────────────────

module acrModule 'modules/acr.bicep' = {
  name: 'phase2-acr'
  params: {
    name: acrName
    location: location
    sku: acrSku
    tags: tags
  }
}

module lawModule 'modules/logAnalytics.bicep' = {
  name: 'phase2-law'
  params: {
    name: logAnalyticsName
    location: location
    retentionInDays: logAnalyticsRetentionInDays
    tags: tags
  }
}

module miModule 'modules/managedIdentity.bicep' = {
  name: 'phase2-mi'
  params: {
    name: managedIdentityName
    location: location
    tags: tags
  }
}

module acrPullModule 'modules/acrPullRole.bicep' = {
  name: 'phase2-acr-pull'
  params: {
    acrName: acrModule.outputs.name
    principalId: miModule.outputs.principalId
  }
}

module caeModule 'modules/containerAppsEnvironment.bicep' = {
  name: 'phase2-cae'
  params: {
    name: containerAppsEnvName
    location: location
    logAnalyticsCustomerId: lawModule.outputs.customerId
    logAnalyticsSharedKey: lawModule.outputs.primarySharedKey
    tags: tags
  }
}

// ── Pre-compute image references and shared env ──────────────────

var acrLoginServer = acrModule.outputs.loginServer
var imageGateway = '${acrLoginServer}/hqqq-gateway:${imageTag}'
var imageRefData = '${acrLoginServer}/hqqq-reference-data:${imageTag}'
var imageIngress = '${acrLoginServer}/hqqq-ingress:${imageTag}'
var imageQuoteEngine = '${acrLoginServer}/hqqq-quote-engine:${imageTag}'
var imagePersistence = '${acrLoginServer}/hqqq-persistence:${imageTag}'
var imageAnalytics = '${acrLoginServer}/hqqq-analytics:${imageTag}'

var commonKafkaEnv = [
  { name: 'Kafka__ClientId', value: kafkaClientId }
  { name: 'Kafka__ConsumerGroupPrefix', value: kafkaConsumerGroupPrefix }
]

var commonAspNetEnv = [
  { name: 'DOTNET_RUNNING_IN_CONTAINER', value: 'true' }
  { name: 'DOTNET_NOLOGO', value: 'true' }
]

var workerManagementEnv = [
  { name: 'Management__Enabled', value: 'true' }
  { name: 'Management__Port', value: '8081' }
  { name: 'Management__BindAddress', value: '0.0.0.0' }
]

// ── Workers: deploy first so the gateway can reference their FQDNs ──

module refDataApp 'modules/containerApp.bicep' = {
  name: 'phase2-app-refdata'
  params: {
    name: referenceDataAppName
    location: location
    environmentId: caeModule.outputs.id
    userAssignedIdentityId: miModule.outputs.id
    acrLoginServer: acrLoginServer
    image: imageRefData
    ingressMode: 'internal'
    targetPort: 8080
    envVars: union(commonAspNetEnv, commonKafkaEnv, [
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    redisConfiguration: redisConfiguration
    timescaleConnectionString: timescaleConnectionString
    cpu: refDataCpu
    memory: refDataMemory
    minReplicas: refDataMinReplicas
    maxReplicas: refDataMaxReplicas
    tags: tags
  }
  dependsOn: [
    acrPullModule
  ]
}

module ingressApp 'modules/containerApp.bicep' = {
  name: 'phase2-app-ingress'
  params: {
    name: ingressAppName
    location: location
    environmentId: caeModule.outputs.id
    userAssignedIdentityId: miModule.outputs.id
    acrLoginServer: acrLoginServer
    image: imageIngress
    ingressMode: 'internal'
    targetPort: 8081
    envVars: union(commonAspNetEnv, commonKafkaEnv, workerManagementEnv, [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'Tiingo__WsUrl', value: 'wss://api.tiingo.com/iex' }
      { name: 'Tiingo__RestBaseUrl', value: 'https://api.tiingo.com/iex' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    tiingoApiKey: tiingoApiKey
    cpu: ingressCpu
    memory: ingressMemory
    minReplicas: ingressMinReplicas
    maxReplicas: ingressMaxReplicas
    tags: tags
  }
  dependsOn: [
    acrPullModule
  ]
}

module quoteEngineApp 'modules/containerApp.bicep' = {
  name: 'phase2-app-quote-engine'
  params: {
    name: quoteEngineAppName
    location: location
    environmentId: caeModule.outputs.id
    userAssignedIdentityId: miModule.outputs.id
    acrLoginServer: acrLoginServer
    image: imageQuoteEngine
    ingressMode: 'internal'
    targetPort: 8081
    envVars: union(commonAspNetEnv, commonKafkaEnv, workerManagementEnv, [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      // Checkpoint path: Container Apps file system is ephemeral per
      // replica. D4 keeps min=max=1 and accepts checkpoint loss on
      // revision swap. Persistent state via Azure Files mount is a
      // documented D5/D6 follow-up.
      { name: 'QuoteEngine__CheckpointPath', value: '/tmp/quote-engine/checkpoint.json' }
      { name: 'QuoteEngine__CheckpointInterval', value: '00:00:10' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    redisConfiguration: redisConfiguration
    cpu: quoteEngineCpu
    memory: quoteEngineMemory
    minReplicas: quoteEngineMinReplicas
    maxReplicas: quoteEngineMaxReplicas
    tags: tags
  }
  dependsOn: [
    acrPullModule
  ]
}

module persistenceApp 'modules/containerApp.bicep' = {
  name: 'phase2-app-persistence'
  params: {
    name: persistenceAppName
    location: location
    environmentId: caeModule.outputs.id
    userAssignedIdentityId: miModule.outputs.id
    acrLoginServer: acrLoginServer
    image: imagePersistence
    ingressMode: 'internal'
    targetPort: 8081
    envVars: union(commonAspNetEnv, commonKafkaEnv, workerManagementEnv, [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'Persistence__SchemaBootstrapOnStart', value: 'true' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    timescaleConnectionString: timescaleConnectionString
    cpu: persistenceCpu
    memory: persistenceMemory
    minReplicas: persistenceMinReplicas
    maxReplicas: persistenceMaxReplicas
    tags: tags
  }
  dependsOn: [
    acrPullModule
  ]
}

// ── Gateway: external, wires worker FQDNs into health aggregator ──
// Container Apps internal ingress terminates TLS on the env's
// internal domain, so URLs use https:// against the FQDN with no
// explicit port. The worker target port (8080 for refdata, 8081
// for the management hosts) is preserved by the env's ingress
// proxy.

module gatewayApp 'modules/containerApp.bicep' = {
  name: 'phase2-app-gateway'
  params: {
    name: gatewayAppName
    location: location
    environmentId: caeModule.outputs.id
    userAssignedIdentityId: miModule.outputs.id
    acrLoginServer: acrLoginServer
    image: imageGateway
    ingressMode: 'external'
    targetPort: 8080
    envVars: union(commonAspNetEnv, [
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
      { name: 'Gateway__BasketId', value: gatewayBasketId }
      { name: 'Gateway__DataSource', value: 'stub' }
      { name: 'Gateway__Sources__Quote', value: 'redis' }
      { name: 'Gateway__Sources__Constituents', value: 'redis' }
      { name: 'Gateway__Sources__History', value: 'timescale' }
      { name: 'Gateway__Sources__SystemHealth', value: 'aggregated' }
      { name: 'Gateway__Health__RequestTimeoutSeconds', value: '1.5' }
      { name: 'Gateway__Health__IncludeRedis', value: 'true' }
      { name: 'Gateway__Health__IncludeTimescale', value: 'true' }
      { name: 'Gateway__Health__Services__ReferenceData__BaseUrl', value: 'https://${refDataApp.outputs.fqdn}' }
      { name: 'Gateway__Health__Services__Ingress__BaseUrl', value: 'https://${ingressApp.outputs.fqdn}' }
      { name: 'Gateway__Health__Services__QuoteEngine__BaseUrl', value: 'https://${quoteEngineApp.outputs.fqdn}' }
      { name: 'Gateway__Health__Services__Persistence__BaseUrl', value: 'https://${persistenceApp.outputs.fqdn}' }
    ])
    redisConfiguration: redisConfiguration
    timescaleConnectionString: timescaleConnectionString
    cpu: gatewayCpu
    memory: gatewayMemory
    minReplicas: gatewayMinReplicas
    maxReplicas: gatewayMaxReplicas
    tags: tags
  }
  dependsOn: [
    acrPullModule
  ]
}

// ── Analytics: Manual-trigger one-shot job ───────────────────────

module analyticsJob 'modules/containerAppJob.bicep' = {
  name: 'phase2-job-analytics'
  params: {
    name: analyticsJobName
    location: location
    environmentId: caeModule.outputs.id
    userAssignedIdentityId: miModule.outputs.id
    acrLoginServer: acrLoginServer
    image: imageAnalytics
    envVars: union(commonAspNetEnv, [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'Analytics__Mode', value: 'report' }
      { name: 'Analytics__BasketId', value: gatewayBasketId }
      // StartUtc / EndUtc / EmitJsonPath are intentionally not set
      // here. They must be supplied at execution time via:
      //   az containerapp job start ... --env-vars Analytics__StartUtc=... Analytics__EndUtc=...
      // The AnalyticsOptionsValidator fails fast if the window is
      // missing, which is the desired behaviour.
    ])
    timescaleConnectionString: timescaleConnectionString
    cpu: analyticsCpu
    memory: analyticsMemory
    replicaTimeoutSeconds: analyticsReplicaTimeoutSeconds
    replicaRetryLimit: analyticsReplicaRetryLimit
    tags: tags
  }
  dependsOn: [
    acrPullModule
  ]
}

// ── Outputs (consumed by the deploy workflow for smoke output) ──

output acrLoginServer string = acrModule.outputs.loginServer
output acrName string = acrModule.outputs.name
output containerAppsEnvironmentName string = caeModule.outputs.name
output managedIdentityClientId string = miModule.outputs.clientId

output gatewayFqdn string = gatewayApp.outputs.fqdn
output gatewayUrl string = 'https://${gatewayApp.outputs.fqdn}'
// Surfaced so the deploy workflow's gateway rollback-assist job can
// distinguish "the revision this run created" from "the revision to
// fall back to" without re-parsing revision lists.
output gatewayLatestRevisionName string = gatewayApp.outputs.latestRevisionName
output referenceDataInternalFqdn string = refDataApp.outputs.fqdn
output ingressInternalFqdn string = ingressApp.outputs.fqdn
output quoteEngineInternalFqdn string = quoteEngineApp.outputs.fqdn
output persistenceInternalFqdn string = persistenceApp.outputs.fqdn
output analyticsJobName string = analyticsJob.outputs.name
