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

Every name above is a **parameter** in `main.bicep`. Concrete values
live in [`params/main.demo.bicepparam`](params/main.demo.bicepparam).
To work around an ACR global-name collision (or to fork a second
environment), copy the demo param file and change names — no edits
to the template logic.

---

## 2) What is NOT provisioned

- Kafka cluster (managed Kafka surface or self-hosted)
- Redis (Azure Cache for Redis or equivalent)
- PostgreSQL with the TimescaleDB extension
- Custom domain + TLS cert on the gateway
- Persistent storage for the quote-engine checkpoint (currently `/tmp`)

These are passed in via `@secure()` deploy-time parameters
(`kafkaBootstrapServers`, `redisConfiguration`,
`timescaleConnectionString`, `tiingoApiKey`). The Phase 2 services
already speak generic .NET hierarchical config keys
(`Kafka__BootstrapServers`, `Redis__Configuration`,
`Timescale__ConnectionString`, `Tiingo__ApiKey`) so any provider
that emits a wire-compatible endpoint works without code changes.

For Azure-native paths to fill these in, see the deferred-work
notes in [`docs/phase2/azure-deploy.md`](../../docs/phase2/azure-deploy.md).

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

| Secret                          | Required | Purpose                         |
| ------------------------------- | -------- | ------------------------------- |
| `KAFKA_BOOTSTRAP_SERVERS`       | yes      | All Kafka producers/consumers   |
| `REDIS_CONFIGURATION`           | yes      | Gateway, quote-engine           |
| `TIMESCALE_CONNECTION_STRING`   | yes      | Gateway, persistence, analytics |
| `TIINGO_API_KEY`                | no       | Ingress only                    |

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

Manual-only Bicep deploy via OIDC. Validates with `bicep build` and
`what-if` before `create`. Prints the gateway FQDN and the smoke
commands in the run summary.

Inputs:

- `image_tag` (default `latest`) — the tag deployed across all 6
  services. For reproducibility prefer `vsha-...`.
- `bicep_param_file` (default `infra/azure/params/main.demo.bicepparam`)
  — swap this when adding additional environments.
- `what_if_only` (boolean, default `false`) — dry-run.

---

## 5) Local validation (no deploy)

```bash
# Lint + compile the template
az bicep build --file infra/azure/main.bicep

# What-if against a real RG (read-only, but requires Azure auth):
az deployment group what-if \
  --resource-group rg-hqqq-p2-demo-eus-01 \
  --template-file infra/azure/main.bicep \
  --parameters infra/azure/params/main.demo.bicepparam \
  --parameters \
    imageTag=vsha-abcdef0 \
    kafkaBootstrapServers='<...>' \
    redisConfiguration='<...>' \
    timescaleConnectionString='<...>'
```

---

## 6) Smoke validation

After a successful deploy, the workflow run summary contains
ready-to-paste commands. The same commands locally:

```bash
RG=rg-hqqq-p2-demo-eus-01
GATEWAY=$(az containerapp show -g $RG -n ca-hqqq-p2-gateway-demo-01 \
  --query properties.configuration.ingress.fqdn -o tsv)

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
├── main.bicep                               RG-scope orchestrator
├── modules/
│   ├── acr.bicep                            ACR (no admin user)
│   ├── logAnalytics.bicep                   Workspace + retention
│   ├── managedIdentity.bicep                User-assigned MI
│   ├── acrPullRole.bicep                    AcrPull on ACR for the MI
│   ├── containerAppsEnvironment.bicep       CAE bound to LAW
│   ├── containerApp.bicep                   Generic app module
│   └── containerAppJob.bicep                Generic job module
├── params/
│   ├── main.demo.bicepparam                 Concrete demo names
│   └── main.example.bicepparam              Placeholder reference
└── README.md                                this file
```
