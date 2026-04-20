# Phase 2 — Rollback Notes

Rollback options for the Phase 2 Azure Container Apps deployment, in
order from "fastest, least invasive" to "most invasive". Pick the
narrowest option that addresses the regression.

Companion docs: [release-checklist.md](release-checklist.md),
[runbook.md](../runbook.md), [azure-deploy.md](azure-deploy.md),
[config-matrix.md](config-matrix.md).

> **No Kubernetes rollback path is documented here.** Phase 2 deploys
> to Azure Container Apps; Kubernetes is Phase 3.

---

## 0) Common variables

```bash
RG=rg-hqqq-p2                  # PHASE2_RESOURCE_GROUP (manual-resource default)
APP=ca-hqqq-p2-gateway         # any of the 5 apps; matches phase2-rollout-existing.yml defaults
JOB=caj-hqqq-p2-analytics      # analytics Container Apps Job
```

> Override `RG` / `APP` / `JOB` if your portal naming deviates from
> the manual-resource defaults documented in
> [`azure-deploy.md` §0.1](azure-deploy.md). For environments still
> on the legacy Bicep path, swap to the `*-demo-eus-01` names from
> `infra/azure/params/main.demo.bicepparam`.

---

## 1) Fastest rollback: flip the active Container App revision

Container Apps keeps previous revisions around. If the new revision is
broken but the old image and its env vars are still good, flip the
traffic split back without touching ACR or running Bicep.

### 1.1 Inspect revisions

```bash
az containerapp revision list \
  --name $APP \
  --resource-group $RG \
  --query "[].{name:name, active:properties.active, traffic:properties.trafficWeight, image:properties.template.containers[0].image, created:properties.createdTime}" \
  --output table
```

### 1.2 Activate the previous revision

```bash
az containerapp revision activate \
  --name $APP \
  --resource-group $RG \
  --revision <previous-revision-name>

# Move 100% traffic to it
az containerapp ingress traffic set \
  --name $APP \
  --resource-group $RG \
  --revision-weight <previous-revision-name>=100
```

### 1.3 When to use it

- You just deployed and the gateway is returning 5xx.
- The previous revision had healthy probes and the regression is
  clearly in the new image (not an external dependency change).
- The revision count is still within the Container App's
  `revisionsMode` retention.

### 1.4 When NOT to use it

- The regression is upstream (Kafka/Redis/Timescale) — flipping the
  revision will not help; fix the dependency first.
- The previous revision was already known broken.

---

## 1.5) Workflow-assisted gateway rollback

The same gateway-only revision flip in §1 is also available as an
optional automatic step inside
[`phase2-deploy.yml`](../../.github/workflows/phase2-deploy.yml).

**How to opt in:** dispatch `phase2-deploy.yml` with
`rollback_on_smoke_failure=true`. If the post-deploy `smoke` job
fails, the `rollback-assist` job runs and:

1. Identifies the previous gateway revision (most-recent revision on
   the gateway app whose name is not the one this run created — the
   workflow knows which revision its own deploy created via the
   `gatewayLatestRevisionName` output of `main.bicep`).
2. Activates that previous revision.
3. Shifts 100% of gateway traffic back to it.
4. Prints the resulting active-revision table to the run summary.

**Scope guarantees:**

- Gateway-only. Worker apps (`hqqq-reference-data`, `hqqq-ingress`,
  `hqqq-quote-engine`, `hqqq-persistence`) and the analytics job are
  **not** touched. Use §2 (image-tag redeploy) for an all-services
  rollback.
- If no retained previous revision exists, the workflow does **not**
  shift traffic; it only prints the manual `az` commands an operator
  should run after building a known-good image set. This avoids
  destructive action with an empty fallback.
- The workflow's preflight + deploy outputs (image tag, RG, gateway
  app name, gateway latest revision) are persisted to the run summary
  so the rollback decision is auditable from the workflow run alone.

When `rollback_on_smoke_failure=false` (default), smoke failure simply
fails the workflow and the run summary prints the manual `az`
commands needed to inspect or roll back. No rollback is attempted.

---

## 2) Image-tag rollback via the deploy workflow

When you want a clean, auditable redeploy of a known-good image set —
or when the previous revision is no longer retained.

### 2.1 Find the previous tag

```bash
az acr repository show-tags \
  -n acrhqqqp2demo01 \
  --repository hqqq-gateway \
  --orderby time_desc \
  -o tsv | head -n 5
```

All six Phase 2 services share one `vsha-...` tag per deploy
([`main.bicep`](../../infra/azure/main.bicep) accepts a single
`imageTag` parameter), so the same tag must exist for `hqqq-gateway`,
`hqqq-reference-data`, `hqqq-ingress`, `hqqq-quote-engine`,
`hqqq-persistence`, and `hqqq-analytics`. Spot-check at least the
gateway and the one most likely to have regressed.

### 2.2 Re-run the deploy workflow

Re-run [`phase2-deploy.yml`](../../.github/workflows/phase2-deploy.yml)
with:

- `image_tag=vsha-<previous-short-sha>`
- `bicep_param_file=infra/azure/params/main.demo.bicepparam`
- `what_if_only=true` (first pass)
- `what_if_only=false` (after reviewing the what-if diff)

The deploy is idempotent: re-running with the previous tag is
indistinguishable from a fresh deploy of that tag.

### 2.3 Why this is "atomic across all six services"

D4 deliberately ships one image tag for all six services per deploy.
This means an image-tag rollback puts the whole app tier back into a
known-good combined state — there is no risk of partially rolling back
(e.g. only the gateway) and ending up with an image-version mismatch
between the gateway and the workers it queries.

---

## 3) Gateway-only rollback when only the gateway is bad

If only the gateway image is broken and you need to revert it without
also reverting the workers (e.g. quote-engine has accumulated useful
checkpoint state since the last deploy), prefer **revision activation**
(§1) on `$APP` (default `ca-hqqq-p2-gateway`). The image-tag deploy
in §2 is all-or-nothing by design.

### 3.1 Workaround if you cannot activate a previous revision

For a temporary mitigation while a real fix lands, change just the
gateway's source-selection envs to fall back away from the broken path
(see §6 below). This avoids a full redeploy and keeps the rest of the
stack on the current image set.

---

## 4) Disable / suspend the analytics job

The analytics job is `triggerType=Manual` — it never auto-runs. The
"disable" path is therefore "stop calling it":

- Do not run `az containerapp job start ...`.
- If a buggy execution is currently running, cancel it:

      ```bash
      EXEC=$(az containerapp job execution list -n $JOB -g $RG \
        --query '[0].name' -o tsv)
      az containerapp job execution stop -n $JOB -g $RG --execution $EXEC
      ```

- For a hard guarantee that no execution can be started, scale the job
  out of action by redeploying with the previous image tag (§2) or by
  temporarily setting `Analytics__Mode` to an unsupported value via
  `az containerapp job update --set-env-vars Analytics__Mode=disabled`
  — the `AnalyticsOptionsValidator` will fail-fast with exit code `2`
  and no Timescale read will occur.

If a future scheduled trigger is added, the disable path becomes
"remove the schedule" — documented at that point.

---

## 5) Safe degraded behavior to expect during a rollback window

These are the documented degraded responses while a rollback is in
flight. Surface them on dashboards but do NOT page on them in
isolation — they all clear automatically once the underlying service
recovers.

| Failed component | What stays up | What degrades |
|------------------|---------------|---------------|
| `hqqq-quote-engine` unhealthy | `/healthz/ready`, `/api/history` (Timescale-backed), `/api/system/health` (aggregator reports `quote-engine` as `unknown`/`unhealthy`, top-level `degraded`) | `/api/quote` and `/api/constituents` return `503 quote_unavailable` / `503 constituents_unavailable`. SignalR connections stay open but emit no `QuoteUpdate` until the quote-engine resumes publishing on `hqqq:channel:quote-update`. |
| `hqqq-persistence` unhealthy | `/api/quote`, `/api/constituents` (Redis-backed), live SignalR fan-out, `/healthz/ready` | `/api/history` returns `503 history_unavailable`. Timescale rows stop accumulating; analytics windows that overlap the outage will report `hasData=false` or partial data. |
| Redis unhealthy | `/api/history`, `/api/system/health` (with `redis` flagged degraded) | `/api/quote`, `/api/constituents`, and the SignalR fan-out all stop. |
| Timescale unhealthy | `/api/quote`, `/api/constituents`, live SignalR fan-out | `/api/history` returns `503`; `hqqq-persistence` write loop logs failures (replay-safe via `ON CONFLICT DO NOTHING` once it recovers). |
| One downstream worker unreachable from the gateway | `/api/system/health` returns `200` overall; that dependency reports `status: "unknown"` (or `"idle"` if no `BaseUrl` configured); top-level `status` rolls up to `degraded`. | No frontend alarm should trip on this in isolation. |
| Corp-action provider failure (legacy monolith) | Live serving | Pricing falls back to unadjusted shares (`SharesOrigin="official"` instead of `"official:split-adjusted"`); `corporate-actions` flagged in legacy `/api/system/health`. |

---

## 6) Reverting gateway source selection (no redeploy)

When a specific source path is the regression, you can flip the
gateway off it via env var on the existing revision (`az containerapp
update --set-env-vars ...` creates a new revision in place and
single-replica gateway is the default min/max in the demo Bicep, so
the cutover is fast).

| Symptom | Env-var flip | Effect |
|---------|--------------|--------|
| Quote/constituents returning malformed payloads from Redis | `Gateway__Sources__Quote=stub` and `Gateway__Sources__Constituents=stub` | Returns deterministic placeholder DTOs (HTTP 200). UI smoke remains green; live data is suspended until the quote-engine path is fixed. |
| Quote/constituents Redis path completely broken; legacy monolith available | `Gateway__DataSource=legacy`, `Gateway__LegacyBaseUrl=<monolith>`, `Gateway__Sources__Quote=legacy`, `Gateway__Sources__Constituents=legacy` | Forwards to the legacy `hqqq-api` so live data continues from the monolith path. |
| `/api/history` regressed (timescale path) | `Gateway__Sources__History=stub` (UI-safe placeholder) **or** `Gateway__Sources__History=legacy` (forward to monolith) | Per-endpoint override, leaves quote/constituents untouched. |
| `/api/system/health` aggregator regressed (D1) | `Gateway__Sources__SystemHealth=legacy` (forward to monolith) **or** `Gateway__Sources__SystemHealth=stub` (deterministic placeholder for offline UI smoke) | Per-endpoint override, leaves all other endpoints on their configured sources. |
| Live SignalR fan-out regressed (D2) | No env-var flip — restart the gateway revision; if the regression is in the quote-engine publisher, follow §1 / §2 on the quote-engine app. | The hub keeps serving REST `/api/quote` for bootstrap; clients reconnect and resync from REST until pub/sub resumes. |

### 6.1 Apply an env-var flip to a single revision

```bash
az containerapp update \
  --name $APP \
  --resource-group $RG \
  --set-env-vars \
    Gateway__Sources__History=stub
```

Container Apps creates a new revision with the env change. Verify:

```bash
curl -fsS https://<gatewayFqdn>/healthz/ready
curl -fsS "https://<gatewayFqdn>/api/history?range=1D"
```

### 6.2 Reverting the env-var flip

Repeat `az containerapp update --set-env-vars Gateway__Sources__History=timescale`
(or whatever the original value was — see
[config-matrix.md](config-matrix.md) for the canonical defaults), or
re-run the deploy workflow with the original `image_tag` to reset all
env vars to the values templated in
[`main.bicep`](../../infra/azure/main.bicep).
