// ─────────────────────────────────────────────────────────────────
// Concrete parameter file for the Phase 2 "demo" environment.
//
// Resource group (assumed pre-created):  rg-hqqq-p2-demo-eus-01
// Region:                                 eastus
//
// All resource names below are isolated from the Phase 1 monolith
// (which lives in a separate ACR + App Service). Renaming requires
// only changing this file — main.bicep takes every name as a param.
//
// Secrets (kafkaBootstrapServers / redisConfiguration /
// timescaleConnectionString / tiingoApiKey) are intentionally NOT
// set here. They are injected at deploy time by phase2-deploy.yml
// from environment-scoped GitHub secrets, e.g.
//   az deployment group create ... \
//     --parameters kafkaBootstrapServers='...' \
//                  redisConfiguration='...' \
//                  timescaleConnectionString='...' \
//                  tiingoApiKey='...'
// Never commit real connection strings to this repo.
// ─────────────────────────────────────────────────────────────────

using '../main.bicep'

param location = 'eastus'

param tags = {
  project: 'hqqq'
  phase: 'phase-2'
  managedBy: 'bicep'
  environment: 'demo'
  costCenter: 'portfolio'
}

// ── ACR ──────────────────────────────────────────────────────────
param acrName = 'acrhqqqp2demo01'
param acrSku = 'Basic'

// ── Log Analytics ────────────────────────────────────────────────
param logAnalyticsName = 'law-hqqq-p2-demo-eus-01'
param logAnalyticsRetentionInDays = 30

// ── Managed Identity ─────────────────────────────────────────────
param managedIdentityName = 'id-hqqq-p2-apps-demo-01'

// ── Container Apps Environment ───────────────────────────────────
param containerAppsEnvName = 'cae-hqqq-p2-demo-eus-01'

// ── Apps + Job ───────────────────────────────────────────────────
param gatewayAppName = 'ca-hqqq-p2-gateway-demo-01'
param referenceDataAppName = 'ca-hqqq-p2-refdata-demo-01'
param ingressAppName = 'ca-hqqq-p2-ingress-demo-01'
param quoteEngineAppName = 'ca-hqqq-p2-quote-engine-demo-01'
param persistenceAppName = 'ca-hqqq-p2-persist-demo-01'
param analyticsJobName = 'caj-hqqq-p2-analytics-demo-01'

// ── Image tag ────────────────────────────────────────────────────
// Overridden by the deploy workflow's `image_tag` input. `latest`
// is the safe default for first-time stand-up; pin to vsha-XXXXX
// for reproducible deploys.
param imageTag = 'latest'

// ── Sizing (demo defaults — kept small) ──────────────────────────
param gatewayCpu = '0.5'
param gatewayMemory = '1.0Gi'
param gatewayMinReplicas = 1
param gatewayMaxReplicas = 2

param refDataCpu = '0.25'
param refDataMemory = '0.5Gi'
param refDataMinReplicas = 1
param refDataMaxReplicas = 2

param ingressCpu = '0.25'
param ingressMemory = '0.5Gi'
param ingressMinReplicas = 1
param ingressMaxReplicas = 1

param quoteEngineCpu = '0.5'
param quoteEngineMemory = '1.0Gi'
param quoteEngineMinReplicas = 1
param quoteEngineMaxReplicas = 1

param persistenceCpu = '0.25'
param persistenceMemory = '0.5Gi'
param persistenceMinReplicas = 1
param persistenceMaxReplicas = 2

param analyticsCpu = '0.5'
param analyticsMemory = '1.0Gi'
param analyticsReplicaTimeoutSeconds = 1800
param analyticsReplicaRetryLimit = 1

// ── Generic non-secret app config ────────────────────────────────
param kafkaClientId = 'hqqq-p2-demo'
param kafkaConsumerGroupPrefix = 'hqqq'
param gatewayBasketId = 'HQQQ'

// ── Secrets — placeholders so `bicep build` succeeds locally ────
// The deploy workflow supplies real values via --parameters.
// Leaving empty strings here would fail the @secure() requirement
// in main.bicep (no default), so use clearly-bogus placeholders
// that the workflow always overrides. NEVER put real values here.
param kafkaBootstrapServers = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param redisConfiguration = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param timescaleConnectionString = 'OVERRIDE_ME_FROM_WORKFLOW_SECRET'
param tiingoApiKey = ''
