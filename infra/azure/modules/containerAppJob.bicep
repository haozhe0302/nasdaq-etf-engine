// ─────────────────────────────────────────────────────────────────
// Container Apps Job for the hqqq-analytics one-shot report.
//
// triggerType=Manual: the job is started on demand by an operator
//   (or by a higher-level scheduler later) using
//   `az containerapp job start ... --env-vars Analytics__StartUtc=...`
//   so accidental auto-runs are not possible.
//
// replicaTimeout / replicaRetryLimit / parallelism are explicit so
// the job's failure posture is documented in the IaC, not implicit.
//
// Per-secret @secure() params follow the same convention as the
// long-running app module: empty string = "this job does not
// consume that secret".
// ─────────────────────────────────────────────────────────────────

@description('Container Apps Job name.')
param name string

@description('Azure region.')
param location string

@description('Container Apps environment resource ID.')
param environmentId string

@description('Resource ID of the user-assigned MI used for ACR pull.')
param userAssignedIdentityId string

@description('ACR login server.')
param acrLoginServer string

@description('Full container image reference for the analytics job.')
param image string

@description('Plain (non-secret) env vars.')
param envVars array = []

@description('TimescaleDB connection string. Empty string = not used by this job.')
@secure()
param timescaleConnectionString string = ''

@description('CPU cores per replica.')
param cpu string = '0.5'

@description('Memory per replica.')
param memory string = '1.0Gi'

@description('Maximum seconds a single replica may run before being terminated.')
@minValue(60)
@maxValue(86400)
param replicaTimeoutSeconds int = 1800

@description('Number of times a failed replica will be retried before the execution is marked failed.')
@minValue(0)
@maxValue(10)
param replicaRetryLimit int = 1

@description('Number of replicas to run in parallel for one execution.')
@minValue(1)
@maxValue(10)
param parallelism int = 1

@description('Number of replicas that must complete successfully for an execution to be considered successful.')
@minValue(1)
@maxValue(10)
param replicaCompletionCount int = 1

@description('Resource tags.')
param tags object = {}

var platformSecrets = empty(timescaleConnectionString) ? [] : [
  { name: 'timescale-connection-string', value: timescaleConnectionString }
]

var secretEnvVars = empty(timescaleConnectionString) ? [] : [
  { name: 'Timescale__ConnectionString', secretRef: 'timescale-connection-string' }
]

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: replicaTimeoutSeconds
      replicaRetryLimit: replicaRetryLimit
      manualTriggerConfig: {
        parallelism: parallelism
        replicaCompletionCount: replicaCompletionCount
      }
      registries: [
        {
          server: acrLoginServer
          identity: userAssignedIdentityId
        }
      ]
      secrets: platformSecrets
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: concat(envVars, secretEnvVars)
        }
      ]
    }
  }
}

output id string = job.id
output name string = job.name
