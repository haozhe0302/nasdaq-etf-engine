# Phase 2 — Release Checklist

A concise gate to walk before promoting a Phase 2 image set to a real
environment (today: the `phase2-demo` Azure Container Apps environment).
Every item is a small, observable check — no item should require
guessing.

Companion docs: [runbook.md](../runbook.md),
[azure-deploy.md](azure-deploy.md), [rollback.md](rollback.md),
[config-matrix.md](config-matrix.md).

---

## 1) Pre-flight: build + test green

- [ ] `dotnet build Hqqq.sln` succeeds locally (no warnings escalated to
      errors).
- [ ] `dotnet test Hqqq.sln` succeeds locally — Phase 2 unit tests must
      be green.
- [ ] CI [`phase2-ci.yml`](../../.github/workflows/phase2-ci.yml) is
      green on the commit you intend to release.

## 2) Pre-flight: images built + pushed

- [ ] [`phase2-images.yml`](../../.github/workflows/phase2-images.yml)
      ran on the target commit and pushed all six images
      (`hqqq-gateway`, `hqqq-reference-data`, `hqqq-ingress`,
      `hqqq-quote-engine`, `hqqq-persistence`, `hqqq-analytics`) to
      ACR `acrhqqqp2` (or your target ACR; legacy Bicep environments
      use `acrhqqqp2demo01`).
- [ ] Note the immutable `vsha-<short-sha>` tag from the workflow run
      summary. **Do not deploy `latest`** — pin to `vsha-...` for
      reproducibility.
- [ ] Spot-check the tag exists in ACR:

      ```bash
      az acr repository show-tags -n acrhqqqp2 \
        --repository hqqq-gateway --orderby time_desc -o tsv | head
      ```

## 3) Pre-flight: required secrets + variables present

Two Phase 2 workflows exist. Pick the one that matches your
target environment and verify its specific prerequisites.

### 3a) `phase2-rollout-existing.yml` (default — manual-resource model)

> Automated by the `preflight` job in
> [`phase2-rollout-existing.yml`](../../.github/workflows/phase2-rollout-existing.yml).
> The job fails fast with an aggregated error block if any of the
> resources below are missing or misnamed, or if any repo secret
> is empty. Verify the workflow's preflight job is green; you do
> not need to re-check these by hand.

Repository **secrets** (required, shared with `phase2-images.yml`
and the legacy `phase2-deploy.yml`):

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

GitHub Environment **`phase2`** — **no** secrets required. App-level
secrets (`Kafka__*`, `Redis__*`, `Timescale__*`, `Tiingo__*`) live on
each Azure Container App's configuration and are never plumbed
through this workflow. The environment exists only for optional
approval reviewers.

Repository **variables** (all optional — workflow defaults match
the portal naming in
[`docs/phase2/azure-deploy.md`](azure-deploy.md) §0.1):

- `PHASE2_RESOURCE_GROUP` (default `rg-hqqq-p2`)
- `PHASE2_ACR_NAME` (default `acrhqqqp2`)
- `PHASE2_CAE_NAME` (default `cae-hqqq-p2`)
- `PHASE2_GATEWAY_APP` (default `ca-hqqq-p2-gateway`)
- `PHASE2_REFDATA_APP` (default `ca-hqqq-p2-refdata`)
- `PHASE2_INGRESS_APP` (default `ca-hqqq-p2-ingress`)
- `PHASE2_QUOTE_APP` (default `ca-hqqq-p2-quote-engine`)
- `PHASE2_PERSISTENCE_APP` (default `ca-hqqq-p2-persist`)
- `PHASE2_ANALYTICS_JOB` (default `caj-hqqq-p2-analytics`)
- `PHASE2_ENVIRONMENT_NAME` (default `phase2`)

Azure resources (must exist before the run — the workflow does
not create them):

- [ ] Resource group `rg-hqqq-p2`.
- [ ] ACR `acrhqqqp2`.
- [ ] Container Apps environment `cae-hqqq-p2`.
- [ ] The five Container Apps listed above, each with a UAMI that
      has `AcrPull` on `acrhqqqp2`.
- [ ] Container Apps Job `caj-hqqq-p2-analytics`.
- [ ] OIDC federated app with `AcrPush` on `acrhqqqp2` and
      `Container Apps Contributor` (or `Contributor`) on
      `rg-hqqq-p2`.

### 3b) `phase2-deploy.yml` (legacy — Bicep provisioning)

> Automated by the `preflight` job in
> [`phase2-deploy.yml`](../../.github/workflows/phase2-deploy.yml).
> The job fails fast with an explicit error block if any of the
> required secrets below are missing or empty. Verify the workflow's
> preflight job is green; you do not need to re-check these by hand.

Repository **secrets** (used by both Phase 2 workflows):

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Repository **variables** (optional, defaults match the demo bicepparam
file):

- `PHASE2_ACR_NAME` (default `acrhqqqp2demo01`)
- `PHASE2_RESOURCE_GROUP` (default `rg-hqqq-p2-demo-eus-01`)
- `PHASE2_LOCATION` (default `eastus`)

GitHub Environment **`phase2-demo`** secrets (Bicep `@secure()` params):

- `KAFKA_BOOTSTRAP_SERVERS`
- `REDIS_CONFIGURATION`
- `TIMESCALE_CONNECTION_STRING`
- `TIINGO_API_KEY` (optional — only if exercising real ingress)

## 4) Pre-flight: external infra endpoints reachable

These resources are not provisioned by the Phase 2 Bicep template; they
must already exist and be reachable from the Container Apps environment:

- [ ] Kafka broker reachable on `KAFKA_BOOTSTRAP_SERVERS`; required
      topics exist (`market.raw_ticks.v1`, `market.latest_by_symbol.v1`,
      `refdata.basket.active.v1`, `refdata.basket.events.v1`,
      `pricing.snapshots.v1`, `ops.incidents.v1`). Topic
      auto-creation is **disabled** by design — bootstrap explicitly if
      needed.
- [ ] Redis reachable on `REDIS_CONFIGURATION` (the gateway and the
      quote-engine both depend on it).
- [ ] TimescaleDB reachable on `TIMESCALE_CONNECTION_STRING`. The
      persistence service performs idempotent schema bootstrap on
      first start (`Persistence__SchemaBootstrapOnStart=true`); allow
      ~1 minute on a fresh database.

## 5) Rollout: dispatch the workflow

Pick the path that matches your environment — do not run both
against the same resource group.

### 5a) Default path — `phase2-rollout-existing.yml`

> The workflow preflight validates that the requested `image_tag`
> is published in ACR for **all six** Phase 2 images, and that all
> target resources already exist, before any update runs.

- [ ] Run
      [`phase2-rollout-existing.yml`](../../.github/workflows/phase2-rollout-existing.yml)
      with `image_tag=vsha-<short-sha>`. Leave `services=all`,
      `update_analytics_job=true`, `skip_smoke=false`, and
      `rollback_on_smoke_failure=false` unless you have a reason to
      deviate.
- [ ] Workflow run summary prints a structured table with:
      - Resource group, ACR (+ login server), Container Apps env
      - All five app names + the analytics job name
      - Gateway FQDN + pre-rollout revision
      - Per-app `new_revision` names after the matrix `rollout` legs
      - Image tag (same across every service)

### 5b) Legacy path — `phase2-deploy.yml` (Bicep provisioning, optional)

Only use this path when intentionally re-applying infrastructure.

- [ ] Run [`phase2-deploy.yml`](../../.github/workflows/phase2-deploy.yml)
      with `image_tag=vsha-<short-sha>`,
      `bicep_param_file=infra/azure/params/main.demo.bicepparam`, and
      `what_if_only=true`.
- [ ] Read the `what-if` output in the workflow log. Expect:
      - **No** unintended resource deletions.
      - The image references on every `Microsoft.App/containerApps` and
        the `Microsoft.App/jobs` resource flip to the new tag.
      - No surprise changes to `secrets`, `ingress`, or `identity`
        blocks.

## 6) Legacy path only — real Bicep run

- [ ] Re-run `phase2-deploy.yml` with the same `image_tag` and
      `what_if_only=false`.
- [ ] Workflow run summary prints a structured table with:
      - Deployment name (`phase2-YYYYMMDD-HHMMSS-<runId>`)
      - Image tag, resource group, Container Apps environment
      - ACR login server
      - Gateway app name + FQDN + latest revision name
      - Analytics job name
      - Ready-to-paste manual smoke / analytics commands

> For the default rollout path (§5a), the run summary already covers
> the equivalent fields; §6 is a no-op when you are not using Bicep.

## 7) Post-deploy: health + smoke

> Automated by the `smoke` job in both
> [`phase2-rollout-existing.yml`](../../.github/workflows/phase2-rollout-existing.yml)
> and
> [`phase2-deploy.yml`](../../.github/workflows/phase2-deploy.yml),
> each of which calls
> [`infra/azure/scripts/phase2-azure-smoke.sh`](../../infra/azure/scripts/phase2-azure-smoke.sh)
> (one shared probe script, no duplication). The job fails the
> workflow if any endpoint below regresses. Verify the smoke job is
> green; you do not need to re-run these `curl`s by hand for the
> `safe-to-demo` gate.

- Liveness: `GET /healthz/live` → `200`
- Readiness: `GET /healthz/ready` → `200`
- System health (D1 native aggregator): `GET /api/system/health` →
  `200`, payload `sourceMode == "aggregated"` (the smoke asserts this
  to catch a silent fall-back to the legacy/stub adapter). Payload
  includes every configured downstream (`reference-data`, `ingress`,
  `quote-engine`, `persistence`, `analytics`) plus local Redis and
  Timescale probes. A missing worker reports `status: "unknown"`;
  not-configured reports `status: "idle"`. Top-level `status` may be
  `degraded` on a fresh environment — that is acceptable.
- `GET /api/quote` → `200` OR documented `503 quote_unavailable`
  (cold-start state).
- `GET /api/constituents` → `200` OR documented
  `503 constituents_unavailable`.
- `GET /api/history?range=1D` → `200` with render-safe JSON shape
  (`pointCount`, `series` array, 21-bucket `distribution`). Empty
  data is fine; `503` is a regression.

## 8) Post-deploy: live data flowing

These checks confirm the data plane, not just the process plane.

- [ ] Quote bootstrap:
      `curl -fsS https://<gatewayFqdn>/api/quote` → `200` once
      `hqqq-quote-engine` has produced at least one Redis snapshot.
      A `503 {"error":"quote_unavailable", ...}` immediately after
      deploy is expected; it should clear within one compute cycle.
- [ ] Constituents:
      `curl -fsS https://<gatewayFqdn>/api/constituents` → `200`.
- [ ] History:
      `curl -fsS "https://<gatewayFqdn>/api/history?range=1D"` →
      `200`. An empty payload (`pointCount=0`, empty `series`,
      stable 21-bucket `distribution`) is acceptable on a fresh
      environment before persistence has accumulated data; **never**
      `503` once Timescale is reachable.
- [ ] Live SignalR fan-out: connect a client to
      `wss://<gatewayFqdn>/hubs/market` (the
      [`Hqqq.Gateway.ReplicaSmoke`](../../tests/Hqqq.Gateway.ReplicaSmoke)
      harness or any minimal SignalR client). Confirm at least one
      `QuoteUpdate` event arrives within ~30 s of the quote-engine
      having warm state.

## 9) Post-deploy: analytics dry run

> Automated when `phase2-deploy.yml` is dispatched with
> `run_analytics_smoke=true`. The smoke job kicks the analytics
> Container Apps Job over a tight one-hour window ending at "now",
> polls the execution (~5 min cap), and requires `Succeeded`. Empty
> windows producing `hasData=false` still exit `0` by design.

The manual fallback (e.g. for a real, populated window before
promoting beyond demo):

- [ ] Trigger a short-window analytics report:

      ```bash
      RG=rg-hqqq-p2
      JOB=caj-hqqq-p2-analytics

      az containerapp job start \
        --name $JOB \
        --resource-group $RG \
        --env-vars \
          Analytics__StartUtc=2026-04-17T00:00:00Z \
          Analytics__EndUtc=2026-04-17T01:00:00Z
      ```

- [ ] Tail the most recent execution and confirm exit code `0`:

      ```bash
      EXEC=$(az containerapp job execution list -n $JOB -g $RG \
        --query '[0].name' -o tsv)
      az containerapp job logs show -n $JOB -g $RG \
        --execution $EXEC --container $JOB --follow
      ```

      An empty window producing `hasData=false` and exit code `0` is
      a pass. Exit code `1` is a failure; exit code `2` indicates an
      unsupported `Analytics:Mode` (regression — investigate before
      promoting).

## 10) Rollback triggers

If any of the following are observed in the first 30 minutes after the
deploy, follow [rollback.md](rollback.md):

- Persistent gateway 5xx on `/healthz/ready` or `/api/system/health`
  (top-level `status: "unhealthy"`) for > 5 minutes.
- `/api/quote` stuck on `503 quote_unavailable` for > 10 minutes
  (suggests a quote-engine bootstrap loop or Redis write failure).
- `/api/quote` returning `502 quote_malformed` (quote-engine writing a
  shape-mismatched payload — regression).
- `hqqq-persistence` log loop on Timescale write failures
  (`ON CONFLICT` should make this idempotent; persistent failures
  indicate a schema or connectivity regression).
- `hqqq-analytics` exit code `1` on the dry-run window from §9.
- Replica-smoke regression: rerun `replica-smoke.{ps1,sh}` against a
  comparable local stack (or against gateway-a + a temporary
  gateway-b in Azure if available); a SignalR-wait timeout means
  fan-out broke.

If smoke fails inside the workflow itself, optionally re-dispatch
either `phase2-rollout-existing.yml` (default) or
`phase2-deploy.yml` (legacy) with `rollback_on_smoke_failure=true`
to attempt a gateway-only revision flip. The rollout workflow
flips back to the *pre-rollout* revision captured in its preflight
(deterministic); the Bicep workflow picks the most-recent revision
other than the one it just created. See [rollback.md](rollback.md)
§1.5.

---

## 11) Promotion gates: "safe to demo" vs. "safe to promote beyond demo"

**Safe to demo** — green check on the target workflow with the
target `image_tag`:

- Default path (`phase2-rollout-existing.yml`):
  - `preflight` job green (repo secrets, RG, ACR, CAE, 5 apps, 1
    job, 6-image tag check).
  - `rollout` matrix green (`az containerapp update --image` on
    all five apps).
  - `rollout-job` green (`az containerapp job update --image` on
    the analytics job) when `update_analytics_job=true`.
  - `smoke` job green (gateway HTTPS probes incl.
    `sourceMode=="aggregated"` and `/api/history` JSON shape).
- Legacy path (`phase2-deploy.yml`):
  - `preflight` job green (secrets, RG, ACR, 6-image tag check).
  - `deploy` job green (`bicep build`, `what-if`, `create`).
  - `smoke` job green (same shared probe script).

That is the minimum gate each workflow now enforces automatically.

**Safe to promote beyond demo** — additionally requires:

- §4 Pre-flight: external infra reachable (Kafka topics present, Redis
  reachable, Timescale reachable from the Container Apps environment).
- §8 Post-deploy: live data flowing — `/api/quote` and
  `/api/constituents` returning HTTP 200 with non-empty payload (not
  the documented cold-start `503 quote_unavailable` /
  `503 constituents_unavailable`); SignalR fan-out emitting
  `QuoteUpdate` events.
- §9 Post-deploy: analytics dry-run on a real, populated window
  (the workflow's `run_analytics_smoke` covers an empty/synthetic
  window only).
- Replica-smoke green against a multi-instance gateway when at least
  two gateway replicas are deployed.
