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
// What this template does NOT provision directly:
//   - Kafka cluster, Redis, PostgreSQL/TimescaleDB.
// These now live in the sibling data-tier template
// `infra/azure/data.bicep`, which provisions Azure Managed Redis,
// PostgreSQL Flexible Server (with TIMESCALEDB allow-listed), and
// an Event Hubs namespace + the six HQQQ topics. Two-template
// separation is intentional so app-tier-only redeploys never
// touch stateful resources. The connection strings still flow
// in here as @secure() parameters; whether they come from
// pre-existing infra or from a `data.bicep` deployment is
// transparent to this template. See `phase2-deploy.yml`'s
// `provision_data_tier` input for the chained-deploy path.
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

// ── Quote-engine checkpoint persistence (optional Azure Files mount) ──
//
// When true, provisions a Storage Account + Azure Files share, attaches it
// to the Container Apps environment as a managed-environment storage
// definition, mounts it inside the quote-engine container at
// `quoteEngineMountPath`, and points `QuoteEngine__CheckpointPath` at
// `${quoteEngineMountPath}/checkpoint.json` so the checkpoint survives
// revision swaps and replica restarts.
//
// When false (default), no storage resources are provisioned and
// quote-engine keeps the historic ephemeral `/tmp/quote-engine/checkpoint.json`
// path (lost on every revision/replica restart).

@description('If true, provision an Azure Files mount and point QuoteEngine__CheckpointPath at it. If false, keep the historic ephemeral /tmp path.')
param quoteEngineCheckpointPersistence bool = false

@description('Globally-unique storage account name backing the quote-engine Azure Files share. Required when quoteEngineCheckpointPersistence=true (3-24 chars, lowercase alphanumerics).')
param quoteEngineStorageAccountName string = ''

@description('Azure Files share name for the quote-engine checkpoint.')
param quoteEngineFileShareName string = 'quote-engine-checkpoint'

@description('Container Apps environment storage definition name referenced by the quote-engine app volume.')
param quoteEngineEnvStorageName string = 'quote-engine-storage'

@description('In-container mount path for the Azure Files share. The checkpoint file is written at <mountPath>/checkpoint.json.')
param quoteEngineMountPath string = '/mnt/quote-engine'

@description('File share quota (GiB) for the quote-engine checkpoint share.')
@minValue(1)
@maxValue(102400)
param quoteEngineFileShareQuotaGiB int = 100

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

@description('Kafka security protocol. Required when broker uses SASL/SSL (e.g. Azure Event Hubs Kafka).')
@secure()
param kafkaSecurityProtocol string

@description('Kafka SASL mechanism (PLAIN for Event Hubs).')
@secure()
param kafkaSaslMechanism string

@description('Kafka SASL username ("$ConnectionString" for Event Hubs).')
@secure()
param kafkaSaslUsername string

@description('Kafka SASL password (Event Hubs namespace connection string).')
@secure()
param kafkaSaslPassword string

@description('Redis connection string. Required by gateway, quote-engine.')
@secure()
param redisConfiguration string

@description('TimescaleDB / Postgres ADO.NET connection string. Required by gateway, persistence, analytics.')
@secure()
param timescaleConnectionString string

@description('Tiingo API key. Required by ingress.')
@secure()
param tiingoApiKey string = ''

@description('Reference-data-specific Tiingo API key for corporate-actions. When empty and `refdataTiingoCorpActionsEnabled=false`, reference-data runs the offline file-only corp-actions provider (also requires `refdataAllowOfflineOnlyInProduction=true`). In the standard `with-ingress` deploy posture the workflow passes the same value as `tiingoApiKey`.')
@secure()
param refdataTiingoApiKey string = ''

@description('AlphaVantage API key for the reference-data basket tail adapter. Required by the standard Azure Production posture (four-source anchored pipeline: StockAnalysis/Schwab anchor + AlphaVantage tail + Nasdaq guardrail). Empty string disables the AlphaVantage source entirely; when combined with `refdataAllowNasdaqTailOnlyInProduction=true` this is the explicit degraded Nasdaq-tail-only posture. When combined with `refdataAllowNasdaqTailOnlyInProduction=false` the reference-data startup guard fails fast so a Production deploy never silently narrows to a 3-source posture.')
@secure()
param alphaVantageApiKey string = ''

// ── Reference-data Production posture (deploy_posture-driven) ────

@description('When true, reference-data overlays Tiingo EOD splits on top of the file corp-action provider. Driven by deploy_posture: true for with-ingress, false for no-ingress-offline.')
param refdataTiingoCorpActionsEnabled bool = true

@description('When true, reference-data accepts running Production with the file-only corp-action provider (no Tiingo overlay). Driven by deploy_posture: false for with-ingress, true for no-ingress-offline.')
param refdataAllowOfflineOnlyInProduction bool = false

@description('When true, reference-data enables the StockAnalysis HTML scraper as a primary anchor source. The standard Azure Production path turns this on so the merged basket carries authoritative SharesHeld.')
param refdataStockAnalysisEnabled bool = true

@description('When true, reference-data enables the Schwab HTML scraper as a secondary anchor source. The standard Azure Production path turns this on so anchor selection can pick the freshest of StockAnalysis vs Schwab.')
param refdataSchwabEnabled bool = true

@description('Explicit per-run operator override: accept a narrower Production posture (anchor + Nasdaq tail only, no AlphaVantage) for the reference-data service. Default false — the standard with-ingress Production contract is the full four-source anchored pipeline and the reference-data startup guard fails fast when AlphaVantage is not configured. Driven by the deploy workflow `allow_nasdaq_tail_only` input and mirrored by the `REFDATA_ALLOW_NASDAQ_TAIL_ONLY` env that `phase2-azure-smoke.sh` reads.')
param refdataAllowNasdaqTailOnlyInProduction bool = false

// ── Generic non-secret app config ────────────────────────────────

@description('Kafka client identifier prefix.')
param kafkaClientId string = 'hqqq-azure'

@description('Kafka consumer group prefix.')
param kafkaConsumerGroupPrefix string = 'hqqq'

@description('Default basket ID surfaced by the gateway.')
param gatewayBasketId string = 'HQQQ'

@description('Operating-mode logging-posture tag for Phase 2 services. `standalone` (default) is the Phase 2 runtime — ingress runs natively against Tiingo, reference-data runs the real-source basket pipeline, gateway reads Redis/Timescale directly. `hybrid` is retained only as a logging label for legacy rollbacks during cutover; it does NOT change runtime behaviour in Phase 2. The hybrid/standalone runtime split has been removed from every Phase 2 service and this parameter only controls the cross-service posture log line.')
@allowed([ 'hybrid', 'standalone' ])
param operatingMode string = 'standalone'

@description('If true (default, matching deploy_posture=with-ingress in the deploy workflow), deploys the hqqq-ingress Container App. If false (deploy_posture=no-ingress-offline), the ingress app is NOT deployed and tiingoApiKey can be empty; reference-data / gateway / quote-engine still come up and serve cached baskets and Redis/Timescale state.')
param deployIngress bool = true

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

// ── Optional quote-engine checkpoint persistence ─────────────────
//
// Storage account + Azure Files share, then a managed-environment
// storage definition that the quote-engine app references as a
// volume. Both modules are conditional on
// `quoteEngineCheckpointPersistence` so a `false` deploy is
// byte-identical to the historic ephemeral posture.

module quoteEngineStorageModule 'modules/storageAccount.bicep' = if (quoteEngineCheckpointPersistence) {
  name: 'phase2-quote-engine-storage'
  params: {
    name: quoteEngineStorageAccountName
    location: location
    fileShareName: quoteEngineFileShareName
    fileShareQuotaGiB: quoteEngineFileShareQuotaGiB
    tags: tags
  }
}

module quoteEngineEnvStorageModule 'modules/managedEnvironmentStorage.bicep' = if (quoteEngineCheckpointPersistence) {
  name: 'phase2-quote-engine-env-storage'
  params: {
    containerAppsEnvName: caeModule.outputs.name
    storageName: quoteEngineEnvStorageName
    // Both storage modules share the same `quoteEngineCheckpointPersistence`
    // condition, so when this module deploys the parent storage module
    // is guaranteed to be deployed too. The two BCP318 nullable-module
    // warnings the analyzer would otherwise raise across that conditional
    // boundary are suppressed inline; the secure key output (BCP426)
    // forces a direct module reference, which is exactly what we use.
    #disable-next-line BCP318
    storageAccountName: quoteEngineStorageModule.outputs.name
    #disable-next-line BCP318
    fileShareName: quoteEngineStorageModule.outputs.fileShareName
    #disable-next-line BCP318
    storageAccountKey: quoteEngineStorageModule.outputs.primaryKey
    accessMode: 'ReadWrite'
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

// Surfaced to every Phase 2 app so a single Bicep param flips the
// whole stack between the legacy-monolith bridge and the standalone
// Phase 2 architecture.
var operatingModeEnv = [
  { name: 'OperatingMode', value: operatingMode }
]

// ── Workers: deploy first so the gateway can reference their FQDNs ──

// Reference-data Production posture, driven by deploy_posture in the
// workflow. These env vars MUST match the runtime guards in
// `ReferenceDataStartupGuard.Validate` + `ValidateCorporateActions` —
// workflow, bicep, and runtime agree exactly so a Production deploy
// never silently runs on the wrong posture.
var refdataPostureEnv = [
  { name: 'ReferenceData__Basket__Mode', value: 'RealSource' }
  { name: 'ReferenceData__Basket__AllowDeterministicSeedInProduction', value: 'false' }
  { name: 'ReferenceData__Basket__RequireAnchorInProduction', value: 'true' }
  { name: 'ReferenceData__Basket__AllowAnchorlessProxyInProduction', value: 'false' }
  { name: 'ReferenceData__Basket__AllowNasdaqTailOnlyInProduction', value: string(refdataAllowNasdaqTailOnlyInProduction) }
  { name: 'ReferenceData__Basket__Sources__StockAnalysis__Enabled', value: string(refdataStockAnalysisEnabled) }
  { name: 'ReferenceData__Basket__Sources__Schwab__Enabled', value: string(refdataSchwabEnabled) }
  { name: 'ReferenceData__Basket__Sources__AlphaVantage__Enabled', value: string(!empty(alphaVantageApiKey)) }
  { name: 'ReferenceData__Basket__Sources__Nasdaq__Enabled', value: 'true' }
  { name: 'ReferenceData__CorporateActions__Tiingo__Enabled', value: string(refdataTiingoCorpActionsEnabled) }
  { name: 'ReferenceData__CorporateActions__AllowOfflineOnlyInProduction', value: string(refdataAllowOfflineOnlyInProduction) }
]

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
    envVars: union(commonAspNetEnv, commonKafkaEnv, operatingModeEnv, refdataPostureEnv, [
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    kafkaSecurityProtocol: kafkaSecurityProtocol
    kafkaSaslMechanism: kafkaSaslMechanism
    kafkaSaslUsername: kafkaSaslUsername
    kafkaSaslPassword: kafkaSaslPassword
    redisConfiguration: redisConfiguration
    timescaleConnectionString: timescaleConnectionString
    refdataTiingoApiKey: refdataTiingoApiKey
    alphaVantageApiKey: alphaVantageApiKey
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

module ingressApp 'modules/containerApp.bicep' = if (deployIngress) {
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
    envVars: union(commonAspNetEnv, commonKafkaEnv, workerManagementEnv, operatingModeEnv, [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'Tiingo__WsUrl', value: 'wss://api.tiingo.com/iex' }
      { name: 'Tiingo__RestBaseUrl', value: 'https://api.tiingo.com/iex' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    kafkaSecurityProtocol: kafkaSecurityProtocol
    kafkaSaslMechanism: kafkaSaslMechanism
    kafkaSaslUsername: kafkaSaslUsername
    kafkaSaslPassword: kafkaSaslPassword
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

// Checkpoint path follows the persistence toggle:
//   - quoteEngineCheckpointPersistence=true  -> mounted Azure Files share
//     at quoteEngineMountPath; checkpoint survives revision swaps.
//   - false                                  -> historic ephemeral /tmp
//     path; checkpoint is lost on every revision/replica restart.
var quoteEngineVolumeName = 'quote-engine-checkpoint'
var quoteEngineCheckpointPathValue = quoteEngineCheckpointPersistence
  ? '${quoteEngineMountPath}/checkpoint.json'
  : '/tmp/quote-engine/checkpoint.json'
var quoteEngineVolumes = quoteEngineCheckpointPersistence ? [
  {
    name: quoteEngineVolumeName
    storageType: 'AzureFile'
    storageName: quoteEngineEnvStorageName
  }
] : []
var quoteEngineVolumeMounts = quoteEngineCheckpointPersistence ? [
  {
    volumeName: quoteEngineVolumeName
    mountPath: quoteEngineMountPath
  }
] : []

// When ingress is not deployed (deploy_posture=no-ingress-offline), the
// gateway's aggregated health probe set omits the Ingress entry. The
// gateway's health aggregator tolerates missing entries and reports
// the missing service as `idle / not-configured` so an operator sees
// the posture directly on /api/system/health instead of a probe
// timeout.
#disable-next-line BCP318
var ingressHealthProbeEnv = deployIngress ? [
  { name: 'Gateway__Health__Services__Ingress__BaseUrl', value: 'https://${ingressApp.outputs.fqdn}' }
] : []

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
    envVars: union(commonAspNetEnv, commonKafkaEnv, workerManagementEnv, operatingModeEnv, [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'QuoteEngine__CheckpointPath', value: quoteEngineCheckpointPathValue }
      { name: 'QuoteEngine__CheckpointInterval', value: '00:00:10' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    kafkaSecurityProtocol: kafkaSecurityProtocol
    kafkaSaslMechanism: kafkaSaslMechanism
    kafkaSaslUsername: kafkaSaslUsername
    kafkaSaslPassword: kafkaSaslPassword
    redisConfiguration: redisConfiguration
    cpu: quoteEngineCpu
    memory: quoteEngineMemory
    minReplicas: quoteEngineMinReplicas
    maxReplicas: quoteEngineMaxReplicas
    tags: tags
    volumes: quoteEngineVolumes
    volumeMounts: quoteEngineVolumeMounts
  }
  dependsOn: [
    acrPullModule
    // The env storage definition must exist before the container app
    // references it via volumes[].storageName. dependsOn is conditional
    // by virtue of the module itself being conditional.
    quoteEngineEnvStorageModule
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
    envVars: union(commonAspNetEnv, commonKafkaEnv, workerManagementEnv, operatingModeEnv, [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'Persistence__SchemaBootstrapOnStart', value: 'true' }
    ])
    kafkaBootstrapServers: kafkaBootstrapServers
    kafkaSecurityProtocol: kafkaSecurityProtocol
    kafkaSaslMechanism: kafkaSaslMechanism
    kafkaSaslUsername: kafkaSaslUsername
    kafkaSaslPassword: kafkaSaslPassword
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
    envVars: union(commonAspNetEnv, operatingModeEnv, [
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
      { name: 'Gateway__Health__Services__QuoteEngine__BaseUrl', value: 'https://${quoteEngineApp.outputs.fqdn}' }
      { name: 'Gateway__Health__Services__Persistence__BaseUrl', value: 'https://${persistenceApp.outputs.fqdn}' }
    ], ingressHealthProbeEnv)
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
output referenceDataAppName string = refDataApp.outputs.name
output referenceDataInternalFqdn string = refDataApp.outputs.fqdn
#disable-next-line BCP318
output ingressInternalFqdn string = deployIngress ? ingressApp.outputs.fqdn : ''
output ingressDeployed bool = deployIngress
output quoteEngineInternalFqdn string = quoteEngineApp.outputs.fqdn
output persistenceInternalFqdn string = persistenceApp.outputs.fqdn
output analyticsJobName string = analyticsJob.outputs.name
