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
      ACR `acrhqqqp2demo01` (or your target ACR).
- [ ] Note the immutable `vsha-<short-sha>` tag from the workflow run
      summary. **Do not deploy `latest`** — pin to `vsha-...` for
      reproducibility.
- [ ] Spot-check the tag exists in ACR:

      ```bash
      az acr repository show-tags -n acrhqqqp2demo01 \
        --repository hqqq-gateway --orderby time_desc -o tsv | head
      ```

## 3) Pre-flight: required secrets + variables present

Repository **secrets** (used by both Phase 2 workflows):

- [ ] `AZURE_CLIENT_ID`
- [ ] `AZURE_TENANT_ID`
- [ ] `AZURE_SUBSCRIPTION_ID`

Repository **variables** (optional, defaults match the demo bicepparam
file):

- [ ] `PHASE2_ACR_NAME` (default `acrhqqqp2demo01`)
- [ ] `PHASE2_RESOURCE_GROUP` (default `rg-hqqq-p2-demo-eus-01`)
- [ ] `PHASE2_LOCATION` (default `eastus`)

GitHub Environment **`phase2-demo`** secrets (Bicep `@secure()` params):

- [ ] `KAFKA_BOOTSTRAP_SERVERS`
- [ ] `REDIS_CONFIGURATION`
- [ ] `TIMESCALE_CONNECTION_STRING`
- [ ] `TIINGO_API_KEY` (optional — only if exercising real ingress)

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

## 5) Deploy: dry run first

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

## 6) Deploy: real run

- [ ] Re-run `phase2-deploy.yml` with the same `image_tag` and
      `what_if_only=false`.
- [ ] Workflow run summary prints:
      - Deployment name (`phase2-YYYYMMDD-HHMMSS-<runId>`)
      - ACR login server
      - Gateway FQDN
      - Ready-to-paste smoke commands

## 7) Post-deploy: health + smoke

Substitute the gateway FQDN from the run summary.

- [ ] Liveness:
      `curl -fsS https://<gatewayFqdn>/healthz/live` → `200`
- [ ] Readiness:
      `curl -fsS https://<gatewayFqdn>/healthz/ready` → `200`
- [ ] System health (D1 native aggregator):
      `curl -fsS https://<gatewayFqdn>/api/system/health` → `200`,
      payload includes every configured downstream
      (`reference-data`, `ingress`, `quote-engine`, `persistence`,
      `analytics`) plus local Redis and Timescale probes. A missing
      worker reports `status: "unknown"`; not-configured reports
      `status: "idle"`. Top-level `status` may be `degraded` on a
      fresh environment — that is acceptable.

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

- [ ] Trigger a short-window analytics report:

      ```bash
      RG=rg-hqqq-p2-demo-eus-01
      JOB=caj-hqqq-p2-analytics-demo-01

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
