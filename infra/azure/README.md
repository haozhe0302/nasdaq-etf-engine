# Phase 2 Azure deployment assets

Bicep + GitHub Actions for deploying the Phase 2 app tier
(`hqqq-gateway`, `hqqq-reference-data`, `hqqq-ingress`,
`hqqq-quote-engine`, `hqqq-persistence`, `hqqq-analytics`) to Azure
Container Apps.

This deployment path is **isolated from the Phase 1 monolith**
(`hqqq-api` + `hqqq-ui`). Phase 1 keeps its own ACR
(`acrhqqqmvp001`), App Service, and `hqqq-api-docker.yml` workflow,
unaffected by anything in this folder.

Kubernetes is explicitly out of scope.

---

## 1) What this provisions

| Resource | Default name (demo)                  | Notes                                                       |
| -------- | ------------------------------------ | ----------------------------------------------------------- |
| ACR      | `acrhqqqp2demo01`                    | Basic SKU. `adminUserEnabled=false`. AcrPull via UAMI.      |
| LAW      | `law-hqqq-p2-demo-eus-01`            | 30-day retention. Bound to the Container Apps env.          |
| UAMI     | `id-hqqq-p2-apps-demo-01`            | One identity for all apps + the analytics job.              |
| CAE      | `cae-hqqq-p2-demo-eus-01`            | Hosts every container app/job in this stack.                |
| App      | `ca-hqqq-p2-gateway-demo-01`         | External ingress :8080. REST + SignalR.                     |
| App      | `ca-hqqq-p2-refdata-demo-01`         | Internal ingress :8080.                                     |
| App      | `ca-hqqq-p2-ingress-demo-01`         | Internal ingress :8081 (management host).                   |
| App      | `ca-hqqq-p2-quote-engine-demo-01`    | Internal ingress :8081. Single replica (checkpoint state).  |
| App      | `ca-hqqq-p2-persist-demo-01`         | Internal ingress :8081.                                     |
| Job      | `caj-hqqq-p2-analytics-demo-01`      | `triggerType=Manual`. 30-min timeout, 1 retry.              |
| Storage  | `sthqqqp2demoeus01` *(opt-in)*       | Standard_LRS StorageV2. Provisioned only when `quoteEngineCheckpointPersistence=true`. |
| Share    | `quote-engine-checkpoint` *(opt-in)* | Azure Files share mounted into the quote-engine container.  |

Every name above is a **parameter** in `main.bicep`. Concrete values
live in [`params/main.demo.bicepparam`](params/main.demo.bicepparam).
To work around an ACR global-name collision (or to fork a second
environment), copy the demo param file and change names — no edits
to the template logic.

---

## 2) What is NOT provisioned by `main.bicep`

- Kafka cluster, Redis, PostgreSQL/TimescaleDB
- Custom domain + TLS cert on the gateway
- Key Vault for secret resolution

These are passed in via `@secure()` deploy-time parameters
(`kafkaBootstrapServers`, `redisConfiguration`,
`timescaleConnectionString`, `tiingoApiKey`). The Phase 2 services
already speak generic .NET hierarchical config keys
(`Kafka__BootstrapServers`, `Redis__Configuration`,
`Timescale__ConnectionString`, `Tiingo__ApiKey`) so any provider
that emits a wire-compatible endpoint works without code changes.

The data + messaging tier (Kafka, Redis, Postgres) **does** have a
sibling Bicep template in this directory —
[`data.bicep`](data.bicep) — that provisions Azure Managed Redis,
PostgreSQL Flexible Server (with the TimescaleDB extension
allow-listed), and an Event Hubs namespace + the six HQQQ topics.
It is opt-in via the `provision_data_tier` workflow input so the
common app-tier-only redeploy path stays fast and never touches
stateful resources. See [§2b](#2b-data--messaging-tier-opt-in-via-databicep)
and the operator walkthrough in
[`docs/phase2/azure-deploy.md` §11](../../docs/phase2/azure-deploy.md).

Persistent storage for the `hqqq-quote-engine` checkpoint is also
an **opt-in** part of `main.bicep` (off in `main.example.bicepparam`,
on in `main.demo.bicepparam`). See
[§2a Persistent checkpoint storage](#2a-persistent-checkpoint-storage-opt-in)
below and the operator walkthrough in
[`docs/phase2/azure-deploy.md` §9](../../docs/phase2/azure-deploy.md).

### 2a) Persistent checkpoint storage (opt-in)

The Container Apps file system is ephemeral per replica. To make the
`hqqq-quote-engine` checkpoint survive revision swaps and restarts,
the template provisions an Azure Files mount when the toggle is on:

```bicep
// infra/azure/params/main.demo.bicepparam
param quoteEngineCheckpointPersistence = true
param quoteEngineStorageAccountName    = 'sthqqqp2demoeus01'
param quoteEngineFileShareName         = 'quote-engine-checkpoint'
param quoteEngineEnvStorageName        = 'quote-engine-storage'
param quoteEngineMountPath             = '/mnt/quote-engine'
param quoteEngineFileShareQuotaGiB     = 100
```

| Mode                                       | Storage account + share | `QuoteEngine__CheckpointPath`               |
| ------------------------------------------ | ----------------------- | ------------------------------------------- |
| `quoteEngineCheckpointPersistence = true`  | provisioned             | `/mnt/quote-engine/checkpoint.json` (durable) |
| `quoteEngineCheckpointPersistence = false` | not provisioned         | `/tmp/quote-engine/checkpoint.json` (ephemeral) |

The toggle is fully orthogonal to local dev: `dotnet run` and
`docker-compose.phase2.yml` keep their existing checkpoint paths.

### 2b) Data + messaging tier (opt-in via `data.bicep`)

[`data.bicep`](data.bicep) is a sibling RG-scope template that
provisions everything `main.bicep` deliberately leaves to deploy-time
secrets. It is invoked by `phase2-deploy.yml` only when the workflow
is dispatched with `provision_data_tier=true`; the resulting
connection strings are forwarded into `main.bicep` as masked job
outputs. Two-template separation is intentional so the common
app-tier-only redeploy path (the inner-loop case) is unchanged.

| Resource | Default name (demo) | Default SKU | Notes |
|----------|---------------------|-------------|-------|
| Azure Managed Redis | `redis-hqqq-p2-demo-eus-01` | `Balanced_B0` | Smallest GA Managed Redis tier. Single database, port 10000, TLS-only, `EnterpriseCluster` policy. |
| PostgreSQL Flexible Server | `psql-hqqq-p2-demo-eus-01` | `Standard_B1ms` Burstable, 32 GiB | PG 16, password auth, public network access on, single firewall rule allows all Azure services (required because the CAE has no VNet integration). |
| Postgres database | `hqqq` (on the server above) | — | Created empty. The TIMESCALEDB extension is allow-listed at the server level via the `azure.extensions` server parameter; `CREATE EXTENSION` itself is left as the operator's explicit step (see `azure-deploy.md` §11.4). |
| Event Hubs namespace | `evhns-hqqq-p2-demo-eus-01` | `Standard`, 1 TU | Kafka surface enabled on `:9093`. Auto-inflate off. |
| Event Hubs (Kafka topics) | 6 hubs matching `docs/phase2/topics.md` | partition counts 3/3/1/1/1/1 | Compaction is **not** supported on Standard; the two compacted HQQQ topics fall back to time-based retention. Override `eventHubsSku=Premium` per environment for compaction parity. |
| Namespace SAS rule | `hqqq-services` | `Send,Listen` | Single namespace-scoped key — used as the Kafka SASL password by every HQQQ client. |

Required additional `phase2-demo` GitHub environment secret when
`provision_data_tier=true`:

| Secret | Required | Purpose |
|--------|----------|---------|
| `POSTGRES_ADMIN_PASSWORD` | yes (data-tier mode only) | Admin password for the new PostgreSQL Flexible Server. Must satisfy Azure complexity rules (8–128 chars, 3 of {upper, lower, digits, symbols}). |

In bring-your-own mode (`provision_data_tier=false`, the default),
`POSTGRES_ADMIN_PASSWORD` is unused and the historic
`KAFKA_*` / `REDIS_CONFIGURATION` / `TIMESCALE_CONNECTION_STRING`
secrets remain required.

---

## 3) One-time bootstrap

### 3.1 Create the resource group

```bash
RG=rg-hqqq-p2-demo-eus-01
LOCATION=eastus
az group create --name "$RG" --location "$LOCATION"
```

### 3.2 Create the GitHub OIDC federated identity

Phase 2 uses **OIDC-only** auth from GitHub Actions to Azure. There
are no long-lived client secrets / publish profiles in either
workflow.

```bash
APP_NAME=gh-hqqq-p2-deployer
SUBSCRIPTION_ID=<your-subscription-id>
GITHUB_OWNER=<your-github-org-or-user>
GITHUB_REPO=nasdaq-etf-engine

# 1) App registration + service principal
APP_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
az ad sp create --id "$APP_ID"
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

# 2) Federated credentials — one for branch pushes, one for the
#    deploy environment. Add more for additional envs later.
az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"main-branch\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:${GITHUB_OWNER}/${GITHUB_REPO}:ref:refs/heads/main\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"phase2-demo-env\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:${GITHUB_OWNER}/${GITHUB_REPO}:environment:phase2-demo\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# 3) Grant Contributor on the resource group (deploy + manage apps)
az role assignment create \
  --assignee-object-id "$SP_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  --role Contributor \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RG}"

# 4) AcrPush on the Phase 2 ACR (only after the first deploy creates
#    it; until then, grant at the RG scope or run a stand-alone
#    `az acr create` ahead of time).
ACR_NAME=acrhqqqp2demo01
az role assignment create \
  --assignee-object-id "$SP_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  --role AcrPush \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RG}/providers/Microsoft.ContainerRegistry/registries/${ACR_NAME}"

echo "Set the following GitHub repo secrets:"
echo "  AZURE_CLIENT_ID       = $APP_ID"
echo "  AZURE_TENANT_ID       = $(az account show --query tenantId -o tsv)"
echo "  AZURE_SUBSCRIPTION_ID = $SUBSCRIPTION_ID"
```

### 3.3 GitHub configuration

Repository **secrets** (used by both Phase 2 workflows):

| Secret                  | Source                              |
| ----------------------- | ----------------------------------- |
| `AZURE_CLIENT_ID`       | `appId` from the app registration   |
| `AZURE_TENANT_ID`       | `az account show --query tenantId`  |
| `AZURE_SUBSCRIPTION_ID` | Target subscription ID              |

Repository **variables** (optional, defaults baked into the
workflows match the demo names):

| Variable                  | Default                       |
| ------------------------- | ----------------------------- |
| `PHASE2_ACR_NAME`         | `acrhqqqp2demo01`             |
| `PHASE2_RESOURCE_GROUP`   | `rg-hqqq-p2-demo-eus-01`      |
| `PHASE2_LOCATION`         | `eastus`                      |

GitHub **environment** `phase2-demo` secrets (used only by the
deploy workflow; environment scope keeps blast radius small and
allows future approval gates):

| Secret                          | Required when                                            | Purpose                                                         |
| ------------------------------- | -------------------------------------------------------- | --------------------------------------------------------------- |
| `KAFKA_BOOTSTRAP_SERVERS`       | `provision_data_tier=false` (the default; bring-your-own data tier) | All Kafka producers/consumers                                   |
| `KAFKA_SECURITY_PROTOCOL`       | `provision_data_tier=false`                              | Kafka SASL/SSL protocol (e.g. `SaslSsl` for Event Hubs)         |
| `KAFKA_SASL_MECHANISM`          | `provision_data_tier=false`                              | Kafka SASL mechanism (e.g. `Plain` for Event Hubs)              |
| `KAFKA_SASL_USERNAME`           | `provision_data_tier=false`                              | Kafka SASL username (`$ConnectionString` literal for Event Hubs) |
| `KAFKA_SASL_PASSWORD`           | `provision_data_tier=false`                              | Kafka SASL password (Event Hubs namespace connection string)    |
| `REDIS_CONFIGURATION`           | `provision_data_tier=false`                              | Gateway, quote-engine                                           |
| `TIMESCALE_CONNECTION_STRING`   | `provision_data_tier=false`                              | Gateway, persistence, analytics                                 |
| `POSTGRES_ADMIN_PASSWORD`       | `provision_data_tier=true` (data-tier bootstrap mode)    | Admin password for the new PostgreSQL Flexible Server. Must satisfy Azure complexity rules (8–128 chars, 3 of {upper, lower, digits, symbols}). |
| `TIINGO_API_KEY`                | optional                                                 | Ingress only                                                    |

The two columns of secrets are mutually exclusive per run: in
data-tier-bootstrap mode (`provision_data_tier=true`) the seven
KAFKA/REDIS/TIMESCALE secrets are produced by `data.bicep` and
forwarded as masked job outputs, so only `POSTGRES_ADMIN_PASSWORD`
needs to be set ahead of time. In bring-your-own mode the seven
secrets stay required and `POSTGRES_ADMIN_PASSWORD` is unused.

---

## 4) Workflows

### 4.1 [`phase2-images.yml`](../../.github/workflows/phase2-images.yml)

Build + test the solution, then build/push every service image to
the Phase 2 ACR using OIDC.

Triggers:

- Push to `main` under any service / building-block / test path.
- `workflow_dispatch` with optional `services` (CSV) and
  `tag_override` inputs.

Tags pushed:

- `vsha-<short-sha>` — immutable, what the deploy workflow pins to.
- `latest` — moving tag, only useful for first stand-up.
- `tag_override` — replaces both above when set.

### 4.2 [`phase2-deploy.yml`](../../.github/workflows/phase2-deploy.yml)

Manual-only Bicep deploy via OIDC, with a workflow-enforced
release-hardening gate. The pipeline shape is:

```
preflight  →  deploy  →  smoke  →  rollback-assist (opt, on failure)
```

- **preflight** (`needs: -`) — fails fast before any what-if. Validates
  required secrets are present, the bicep param file exists on disk,
  the resource group exists, the ACR exists, and the requested
  `image_tag` is published in ACR for **all six** Phase 2 images
  (`hqqq-gateway`, `hqqq-reference-data`, `hqqq-ingress`,
  `hqqq-quote-engine`, `hqqq-persistence`, `hqqq-analytics`). Missing
  tags are aggregated into a single error block.
- **deploy** (`needs: preflight`) — `bicep build` + `what-if` + `create`.
  Publishes a structured `outputs:` block (deployment name, image tag,
  RG, Container Apps env, gateway app name, gateway FQDN, gateway
  latest revision name, analytics job name) and a machine-readable
  table to the run summary.
- **smoke** (`needs: deploy`, runs only when `what_if_only=false`) —
  HTTPS probes via [`infra/azure/scripts/phase2-azure-smoke.sh`](scripts/phase2-azure-smoke.sh).
  Validates `/healthz/live`, `/healthz/ready`, `/api/system/health`
  (asserts `sourceMode == "aggregated"` so a fall-back to legacy/stub
  is caught), `/api/quote`, `/api/constituents`, and
  `/api/history?range=1D` (JSON shape: `pointCount`, `series` array,
  21-bucket `distribution`). On failure, the workflow emits the failing
  endpoint plus copy-paste `az` commands for revision listing, log
  tailing, and (if applicable) the analytics execution.
- **rollback-assist** (optional, runs only on smoke failure when
  `rollback_on_smoke_failure=true`) — gateway-only revision flip:
  identifies the previous revision, activates it, and shifts 100%
  traffic. Workers and the analytics job are not touched by design.

Inputs:

- `image_tag` (default `latest`) — the tag deployed across all 6
  services. For reproducibility prefer `vsha-...`.
- `bicep_param_file` (default `infra/azure/params/main.demo.bicepparam`)
  — swap this when adding additional environments.
- `provision_data_tier` (boolean, default `false`) — when true, runs
  an extra `provision-data` job before `deploy` that deploys
  [`infra/azure/data.bicep`](data.bicep) (Managed Redis + PostgreSQL
  Flexible Server + Event Hubs) and pipes its connection-string
  outputs into `main.bicep` as masked job outputs. Defaults to
  `false` so app-tier-only redeploys keep their existing fast path.
  See [§2b](#2b-data--messaging-tier-opt-in-via-databicep) and
  [`docs/phase2/azure-deploy.md` §11](../../docs/phase2/azure-deploy.md).
- `data_bicep_param_file` (default `infra/azure/params/data.demo.bicepparam`)
  — only consumed when `provision_data_tier=true`.
- `what_if_only` (boolean, default `false`) — dry-run; skips
  deploy create, smoke, and rollback-assist.
- `run_analytics_smoke` (boolean, default `false`) — when true, the
  smoke job also kicks the analytics Container Apps Job over a tight
  one-hour window, polls execution status (bounded ~5 min), and
  requires exit code 0. Empty windows are still expected to succeed
  by design.
- `rollback_on_smoke_failure` (boolean, default `false`) — when true,
  the rollback-assist job runs on smoke failure. When false, smoke
  failure simply fails the workflow and prints the manual `az`
  commands needed to inspect or roll back.

---

## 5) Local validation (no deploy)

```bash
# Lint + compile both templates
az bicep build --file infra/azure/main.bicep
az bicep build --file infra/azure/data.bicep

# What-if the app tier against a real RG (read-only, but requires Azure auth):
az deployment group what-if \
  --resource-group rg-hqqq-p2-demo-eus-01 \
  --template-file infra/azure/main.bicep \
  --parameters infra/azure/params/main.demo.bicepparam \
  --parameters \
    imageTag=vsha-abcdef0 \
    kafkaBootstrapServers='<...>' \
    kafkaSecurityProtocol='SaslSsl' \
    kafkaSaslMechanism='Plain' \
    kafkaSaslUsername='$ConnectionString' \
    kafkaSaslPassword='<event-hubs-connection-string>' \
    redisConfiguration='<...>' \
    timescaleConnectionString='<...>'

# What-if the data tier (only postgresAdministratorPassword is required):
az deployment group what-if \
  --resource-group rg-hqqq-p2-demo-eus-01 \
  --template-file infra/azure/data.bicep \
  --parameters infra/azure/params/data.demo.bicepparam \
  --parameters \
    postgresAdministratorPassword='<a-strong-password>'
```

---

## 6) Smoke validation

The primary post-deploy smoke gate is the `smoke` job inside
[`phase2-deploy.yml`](../../.github/workflows/phase2-deploy.yml). On a
green deploy it runs automatically; if it fails, the workflow fails
and the run summary lists the failing endpoint plus diagnostic `az`
commands.

The same probe is re-runnable locally (or against any environment) via
the helper script:

```bash
RG=rg-hqqq-p2-demo-eus-01
GATEWAY=$(az containerapp show -g $RG -n ca-hqqq-p2-gateway-demo-01 \
  --query properties.configuration.ingress.fqdn -o tsv)

GATEWAY_FQDN="$GATEWAY" \
  ./infra/azure/scripts/phase2-azure-smoke.sh
```

Bare `curl` fallback (single-endpoint check):

```bash
curl -fsS https://$GATEWAY/healthz/ready
curl -fsS https://$GATEWAY/api/system/health
```

Run the analytics job on demand:

```bash
az containerapp job start \
  --name caj-hqqq-p2-analytics-demo-01 \
  --resource-group $RG \
  --env-vars \
    Analytics__StartUtc=2026-04-17T00:00:00Z \
    Analytics__EndUtc=2026-04-18T00:00:00Z

# Inspect the latest execution
az containerapp job execution list \
  --name caj-hqqq-p2-analytics-demo-01 \
  --resource-group $RG \
  --query '[0]'
```

---

## 7) File map

```text
infra/azure/
├── main.bicep                               RG-scope orchestrator (app tier)
├── data.bicep                               RG-scope orchestrator (data + messaging tier, opt-in)
├── modules/
│   ├── acr.bicep                            ACR (no admin user)
│   ├── logAnalytics.bicep                   Workspace + retention
│   ├── managedIdentity.bicep                User-assigned MI
│   ├── acrPullRole.bicep                    AcrPull on ACR for the MI
│   ├── containerAppsEnvironment.bicep       CAE bound to LAW
│   ├── containerApp.bicep                   Generic app module (now supports volumes)
│   ├── containerAppJob.bicep                Generic job module
│   ├── storageAccount.bicep                 Storage account + Azure Files share (opt-in)
│   ├── managedEnvironmentStorage.bicep      CAE storage definition for Azure Files (opt-in)
│   ├── managedRedis.bicep                   Azure Managed Redis cluster + database (data tier)
│   ├── postgresFlexible.bicep               PostgreSQL Flexible Server + hqqq DB + firewall + azure.extensions allow-list (data tier)
│   └── eventHubsNamespace.bicep             Event Hubs namespace + 6 hubs + Send,Listen SAS rule (data tier)
├── params/
│   ├── main.demo.bicepparam                 Concrete demo names (app tier)
│   ├── main.example.bicepparam              Placeholder reference (app tier)
│   ├── data.demo.bicepparam                 Concrete demo names (data tier)
│   └── data.example.bicepparam              Placeholder reference (data tier)
├── scripts/
│   └── phase2-azure-smoke.sh                Post-deploy HTTPS smoke probe
└── README.md                                this file
```
