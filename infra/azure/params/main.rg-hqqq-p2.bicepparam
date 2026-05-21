// ─────────────────────────────────────────────────────────────────
// Concrete parameter file for the live Phase 2 stack in `rg-hqqq-p2`.
//
// Resource group (pre-existing):  rg-hqqq-p2
// Region:                          eastus2
//
// Goal: reconcile the IaC to the names that already exist in
// Azure so `phase2-deploy.yml` updates (idempotent) rather than
// creating a parallel set of resources. Every name below was
// confirmed against `az resource list -g rg-hqqq-p2`.
//
// Differences vs `main.demo.bicepparam`:
//   - Region                                eastus2 (vs eastus)
//   - ACR / LAW / Apps / Job / MI           no `-demo-eus-01` suffix
//   - Managed identity                      `uami-hqqq-p2-acr`
//                                           (legacy ACR-pull identity
//                                            reused for all apps)
//   - Storage account                       `sthqqqp2qe` (vs demo's
//                                            `sthqqqp2demoeus01`)
//   - Sizing                                mirrors current live shape
//                                            (gateway/quote-engine
//                                            already scaled to 1 CPU
//                                            2 Gi, 1-3 replicas)
//   - `kafkaClientId = 'hqqq-azure'`        matches the value set on
//                                            live container envs so the
//                                            first what-if shows no
//                                            spurious env-var churn.
//
// Secrets (kafkaBootstrapServers / kafkaSecurityProtocol /
// kafkaSaslMechanism / kafkaSaslUsername / kafkaSaslPassword /
// redisConfiguration / timescaleConnectionString / tiingoApiKey /
// refdataTiingoApiKey / alphaVantageApiKey) are intentionally NOT
// set here. The deploy workflow injects them from environment-scoped
// GitHub secrets at runtime.
// ─────────────────────────────────────────────────────────────────

using '../main.bicep'

param location = 'eastus2'

param tags = {
  project: 'hqqq'
  phase: 'phase-2'
  managedBy: 'bicep'
  environment: 'demo'
  costCenter: 'portfolio'
}

// ── ACR ──────────────────────────────────────────────────────────
param acrName = 'acrhqqqp2'
param acrSku = 'Standard'

// ── Log Analytics ────────────────────────────────────────────────
param logAnalyticsName = 'law-hqqq-p2'
param logAnalyticsRetentionInDays = 30

// ── Managed Identity ─────────────────────────────────────────────
// The live stack reuses the original `uami-hqqq-p2-acr` identity
// (created for ACR pull) for every Container App. Keeping this name
// here prevents the deploy from spinning up a second identity and
// re-wiring ACR pull.
param managedIdentityName = 'uami-hqqq-p2-acr'

// ── Container Apps Environment ───────────────────────────────────
param containerAppsEnvName = 'cae-hqqq-p2'

// ── Apps + Job ───────────────────────────────────────────────────
param gatewayAppName = 'ca-hqqq-p2-gateway'
param referenceDataAppName = 'ca-hqqq-p2-refdata'
param ingressAppName = 'ca-hqqq-p2-ingress'
param quoteEngineAppName = 'ca-hqqq-p2-quote-engine'
param persistenceAppName = 'ca-hqqq-p2-persist'
param analyticsJobName = 'caj-hqqq-p2-analytics'

// ── Quote-engine checkpoint persistence (Azure Files mount) ─────
// The storage account, file share, and env-storage definition all
// already exist in `rg-hqqq-p2`; names below match the live state
// 1:1 so the deploy updates the container template (volume
// reference) without recreating the share or losing the persisted
// checkpoint.json.
param quoteEngineCheckpointPersistence = true
param quoteEngineStorageAccountName = 'sthqqqp2qe'
param quoteEngineFileShareName = 'quote-engine-checkpoint'
param quoteEngineEnvStorageName = 'quote-engine-storage'
param quoteEngineMountPath = '/mnt/quote-engine'
param quoteEngineFileShareQuotaGiB = 100

// ── Image tag ────────────────────────────────────────────────────
// Overridden by the deploy workflow's `image_tag` input. Pin to the
// vsha-<sha> tag built from the long-term-hardening PR before
// running with `what_if_only=false`.
param imageTag = 'latest'

// ── Sizing (mirrors live container-app shape) ────────────────────
// Pulled from `az containerapp list -g rg-hqqq-p2`. Holding these
// fixed avoids a what-if delta on the first reconcile.
param gatewayCpu = '1.0'
param gatewayMemory = '2.0Gi'
param gatewayMinReplicas = 1
param gatewayMaxReplicas = 3

param refDataCpu = '0.5'
param refDataMemory = '1.0Gi'
param refDataMinReplicas = 1
param refDataMaxReplicas = 3

param ingressCpu = '0.5'
param ingressMemory = '1.0Gi'
param ingressMinReplicas = 1
param ingressMaxReplicas = 3

param quoteEngineCpu = '1.0'
param quoteEngineMemory = '2.0Gi'
param quoteEngineMinReplicas = 1
param quoteEngineMaxReplicas = 3

param persistenceCpu = '0.5'
param persistenceMemory = '1.0Gi'
param persistenceMinReplicas = 1
param persistenceMaxReplicas = 2

param analyticsCpu = '1.0'
param analyticsMemory = '2.0Gi'
param analyticsReplicaTimeoutSeconds = 1800
param analyticsReplicaRetryLimit = 0

// ── Generic non-secret app config ────────────────────────────────
// `hqqq-azure` matches the current live `Kafka__ClientId` env on
// every container app — keeping it here avoids unnecessary env
// churn on what-if.
param kafkaClientId = 'hqqq-azure'
param kafkaConsumerGroupPrefix = 'hqqq'
param gatewayBasketId = 'HQQQ'

// ── Operating mode ───────────────────────────────────────────────
// Phase 2 runs `standalone` unconditionally; matches live posture.
param operatingMode = 'standalone'

// ── Reference-data Production posture (deploy_posture-driven) ────
// Default to the `with-ingress` path. The deploy workflow rewrites
// each of these from the `deploy_posture` input so workflow/bicep/
// runtime always agree at apply time.
param refdataTiingoCorpActionsEnabled = true
param refdataAllowOfflineOnlyInProduction = false
param refdataStockAnalysisEnabled = true
param refdataSchwabEnabled = true

// ── Secrets — placeholders so `bicep build` succeeds locally ────
// The deploy workflow supplies real values via --parameters from
// the `phase2-demo` GitHub environment. Leaving these empty would
// fail the @secure() requirement in main.bicep (no default).
// NEVER put real values here.
param kafkaBootstrapServers = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param kafkaSecurityProtocol = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param kafkaSaslMechanism = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param kafkaSaslUsername = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param kafkaSaslPassword = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param redisConfiguration = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param timescaleConnectionString = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param tiingoApiKey = ''
param refdataTiingoApiKey = ''
