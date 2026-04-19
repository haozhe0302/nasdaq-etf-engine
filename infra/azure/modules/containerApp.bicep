// ─────────────────────────────────────────────────────────────────
// Generic Container App module used by every Phase 2 long-running
// service (gateway, reference-data, ingress, quote-engine,
// persistence). The analytics one-shot job uses containerAppJob.bicep.
//
// Hardening posture wired in:
//   - explicit image tag (caller passes the full repo:tag)
//   - liveness + readiness probes against /healthz/live and
//     /healthz/ready on the targetPort when ingress is enabled
//   - explicit cpu/memory resources
//   - secrets passed via per-secret @secure() params and surfaced
//     to the container as `secretRef` env vars (never plaintext)
//   - registry pull through a user-assigned managed identity
//     (no admin user, no registry password)
//   - ingress is opt-in: caller chooses 'external' | 'internal' | 'none'
//
// Per-secret params follow the convention: empty string = "this app
// does not consume that secret". This keeps the per-app callsites
// in main.bicep readable and avoids JSON-encoding workarounds for
// secrets bundled into a single string.
// ─────────────────────────────────────────────────────────────────

@description('Container App name.')
param name string

@description('Azure region.')
param location string

@description('Container Apps environment resource ID.')
param environmentId string

@description('Resource ID of the user-assigned MI used for ACR pull.')
param userAssignedIdentityId string

@description('ACR login server (e.g. acrhqqqp2demo01.azurecr.io).')
param acrLoginServer string

@description('Full container image reference, e.g. acrhqqqp2demo01.azurecr.io/hqqq-gateway:vsha-abcdef0.')
param image string

@description('Ingress mode. "none" disables ingress entirely (background workers with no inbound).')
@allowed([ 'external', 'internal', 'none' ])
param ingressMode string = 'none'

@description('Container target port. Required when ingressMode is external/internal.')
param targetPort int = 8080

@description('Plain (non-secret) env vars. Each item: { name: string, value: string }.')
param envVars array = []

@description('Kafka bootstrap servers connection string. Empty string = not used by this app.')
@secure()
param kafkaBootstrapServers string = ''

@description('Kafka SASL/SSL security protocol (e.g. SASL_SSL). Empty string = not used by this app.')
@secure()
param kafkaSecurityProtocol string = ''

@description('Kafka SASL mechanism (e.g. PLAIN, SCRAM-SHA-256). Empty string = not used by this app.')
@secure()
param kafkaSaslMechanism string = ''

@description('Kafka SASL username (Event Hubs uses "$ConnectionString"). Empty string = not used by this app.')
@secure()
param kafkaSaslUsername string = ''

@description('Kafka SASL password (Event Hubs namespace connection string). Empty string = not used by this app.')
@secure()
param kafkaSaslPassword string = ''

@description('Redis connection string. Empty string = not used by this app.')
@secure()
param redisConfiguration string = ''

@description('TimescaleDB connection string. Empty string = not used by this app.')
@secure()
param timescaleConnectionString string = ''

@description('Tiingo API key. Empty string = not used by this app.')
@secure()
param tiingoApiKey string = ''

@description('CPU cores per replica (e.g. 0.25, 0.5, 1.0).')
param cpu string = '0.5'

@description('Memory per replica (e.g. 0.5Gi, 1.0Gi, 2.0Gi).')
param memory string = '1.0Gi'

@description('Minimum replica count.')
@minValue(0)
@maxValue(25)
param minReplicas int = 1

@description('Maximum replica count.')
@minValue(1)
@maxValue(25)
param maxReplicas int = 2

@description('Resource tags.')
param tags object = {}

// Build the platform "secrets" block from whichever per-secret params
// were actually populated. Each entry maps a Container App secret
// name (kebab-case) to the secret value.
var platformSecrets = union(
  empty(kafkaBootstrapServers) ? [] : [ { name: 'kafka-bootstrap-servers', value: kafkaBootstrapServers } ],
  empty(kafkaSecurityProtocol) ? [] : [ { name: 'kafka-security-protocol', value: kafkaSecurityProtocol } ],
  empty(kafkaSaslMechanism) ? [] : [ { name: 'kafka-sasl-mechanism', value: kafkaSaslMechanism } ],
  empty(kafkaSaslUsername) ? [] : [ { name: 'kafka-sasl-username', value: kafkaSaslUsername } ],
  empty(kafkaSaslPassword) ? [] : [ { name: 'kafka-sasl-password', value: kafkaSaslPassword } ],
  empty(redisConfiguration) ? [] : [ { name: 'redis-configuration', value: redisConfiguration } ],
  empty(timescaleConnectionString) ? [] : [ { name: 'timescale-connection-string', value: timescaleConnectionString } ],
  empty(tiingoApiKey) ? [] : [ { name: 'tiingo-api-key', value: tiingoApiKey } ]
)

// Map each populated secret to the .NET hierarchical config key the
// services already bind (Kafka__BootstrapServers, etc).
var secretEnvVars = union(
  empty(kafkaBootstrapServers) ? [] : [ { name: 'Kafka__BootstrapServers', secretRef: 'kafka-bootstrap-servers' } ],
  empty(kafkaSecurityProtocol) ? [] : [ { name: 'Kafka__SecurityProtocol', secretRef: 'kafka-security-protocol' } ],
  empty(kafkaSaslMechanism) ? [] : [ { name: 'Kafka__SaslMechanism', secretRef: 'kafka-sasl-mechanism' } ],
  empty(kafkaSaslUsername) ? [] : [ { name: 'Kafka__SaslUsername', secretRef: 'kafka-sasl-username' } ],
  empty(kafkaSaslPassword) ? [] : [ { name: 'Kafka__SaslPassword', secretRef: 'kafka-sasl-password' } ],
  empty(redisConfiguration) ? [] : [ { name: 'Redis__Configuration', secretRef: 'redis-configuration' } ],
  empty(timescaleConnectionString) ? [] : [ { name: 'Timescale__ConnectionString', secretRef: 'timescale-connection-string' } ],
  empty(tiingoApiKey) ? [] : [ { name: 'Tiingo__ApiKey', secretRef: 'tiingo-api-key' } ]
)

var ingressBlock = ingressMode == 'none' ? null : {
  external: ingressMode == 'external'
  targetPort: targetPort
  transport: 'auto'
  allowInsecure: false
  traffic: [
    {
      latestRevision: true
      weight: 100
    }
  ]
}

var probes = ingressMode == 'none' ? [] : [
  {
    type: 'Liveness'
    httpGet: {
      path: '/healthz/live'
      port: targetPort
      scheme: 'HTTP'
    }
    initialDelaySeconds: 10
    periodSeconds: 30
    timeoutSeconds: 3
    failureThreshold: 3
  }
  {
    type: 'Readiness'
    httpGet: {
      path: '/healthz/ready'
      port: targetPort
      scheme: 'HTTP'
    }
    initialDelaySeconds: 5
    periodSeconds: 10
    timeoutSeconds: 3
    failureThreshold: 3
  }
]

resource app 'Microsoft.App/containerApps@2024-03-01' = {
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
      activeRevisionsMode: 'Single'
      ingress: ingressBlock
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
          probes: probes
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output id string = app.id
output name string = app.name
output fqdn string = ingressMode == 'none' ? '' : app.properties.configuration.ingress.fqdn
output latestRevisionName string = app.properties.latestRevisionName
