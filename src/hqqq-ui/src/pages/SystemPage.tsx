import { useMarketData, useSystemData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { StatusBadge } from "@/components/StatusBadge";
import { MetricRow } from "@/components/MetricRow";
import type { RuntimeMetricsData, LatencyStatsData } from "@/lib/types";

function fmtUptime(s: number): string {
  if (s <= 0) return "—";
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = Math.floor(s % 60);
  if (h > 0) return `${h}h ${m}m ${sec}s`;
  if (m > 0) return `${m}m ${sec}s`;
  return `${sec}s`;
}

function fmtMs(v: number): string {
  if (v >= 1000) return `${(v / 1000).toFixed(1)}s`;
  return `${v.toFixed(1)}ms`;
}

function fmtPct(v: number): string {
  return `${(v * 100).toFixed(1)}%`;
}

function fmtCount(v: number): string {
  if (v >= 1_000_000) return `${(v / 1_000_000).toFixed(1)}M`;
  if (v >= 1_000) return `${(v / 1_000).toFixed(1)}K`;
  return String(v);
}

function fmtLatency(stats: LatencyStatsData | undefined, percentile: "p50" | "p95"): string {
  if (!stats || stats.sampleCount === 0) return "—";
  return fmtMs(stats[percentile]);
}

function toTitleCase(text: string): string {
  return text
    .split(/[\s_-]+/)
    .filter(Boolean)
    .map((w) => w[0].toUpperCase() + w.slice(1).toLowerCase())
    .join(" ");
}

function ConnectionBanner({ connectionState, error }: { connectionState: string; error?: string }) {
  if (connectionState === "live") return null;
  const isConnecting = connectionState === "connecting";
  const cls = isConnecting
    ? "border-accent/30 bg-accent/10 text-accent"
    : "border-yellow-500/30 bg-yellow-500/10 text-yellow-400";
  return (
    <div className={`rounded border px-3 py-1.5 text-xs ${cls}`}>
      {isConnecting ? "Connecting to backend\u2026" : error ?? "Connection lost \u2014 showing last known data"}
    </div>
  );
}

function RuntimeMetricsPanel({
  metrics,
  isRegularSessionOpen,
}: {
  metrics?: RuntimeMetricsData;
  isRegularSessionOpen: boolean;
}) {
  if (!metrics) {
    return (
      <Panel title="Runtime Metrics" className="col-span-3">
        <div className="p-3 text-xs text-muted">
          Waiting for metrics data\u2026
        </div>
      </Panel>
    );
  }

  return (
    <Panel title="Runtime Metrics" className="col-span-3">
      <div className="grid grid-cols-4 gap-x-8 p-3">
        <div className="space-y-0.5">
          <div className="mb-1 text-[10px] font-medium uppercase tracking-wider text-muted/70">
            Freshness
          </div>
          <MetricRow label="Snapshot age" value={fmtMs(metrics.snapshotAgeMs)} />
          <MetricRow
            label="Weight coverage"
            value={
              <span className={metrics.pricedWeightCoverage >= 0.95 ? "text-positive" : "text-negative"}>
                {fmtPct(metrics.pricedWeightCoverage)}
              </span>
            }
          />
          <MetricRow
            label="Stale ratio"
            value={
              <span className={metrics.staleSymbolRatio <= 0.05 ? "text-positive" : "text-negative"}>
                {fmtPct(metrics.staleSymbolRatio)}
              </span>
            }
          />
        </div>

        <div className="space-y-0.5">
          <div className="mb-1 text-[10px] font-medium uppercase tracking-wider text-muted/70">
            Latency
          </div>
          <MetricRow
            label="Tick→Quote (p50)"
            value={isRegularSessionOpen ? fmtLatency(metrics.tickToQuoteMs, "p50") : "Market Closed"}
          />
          <MetricRow
            label="Tick→Quote (p95)"
            value={isRegularSessionOpen ? fmtLatency(metrics.tickToQuoteMs, "p95") : "Market Closed"}
          />
          <MetricRow
            label="Broadcast (p50)"
            value={fmtLatency(metrics.quoteBroadcastMs, "p50")}
          />
          <MetricRow
            label="Broadcast (p95)"
            value={fmtLatency(metrics.quoteBroadcastMs, "p95")}
          />
        </div>

        <div className="space-y-0.5">
          <div className="mb-1 text-[10px] font-medium uppercase tracking-wider text-muted/70">
            Counters
          </div>
          <MetricRow label="Ticks ingested" value={fmtCount(metrics.totalTicksIngested)} />
          <MetricRow label="Broadcasts" value={fmtCount(metrics.totalQuoteBroadcasts)} />
          <MetricRow label="Fallback acts." value={String(metrics.totalFallbackActivations)} />
          <MetricRow label="Basket acts." value={String(metrics.totalBasketActivations)} />
        </div>

        <div className="space-y-0.5">
          <div className="mb-1 text-[10px] font-medium uppercase tracking-wider text-muted/70">
            Events
          </div>
          <MetricRow
            label="Failover recovery"
            value={
              metrics.lastFailoverRecoverySeconds != null
                ? `${metrics.lastFailoverRecoverySeconds.toFixed(1)}s`
                : "—"
            }
          />
          <MetricRow
            label="Activation jump"
            value={
              metrics.lastActivationJumpBps != null
                ? `${metrics.lastActivationJumpBps.toFixed(1)} bps`
                : "—"
            }
          />
        </div>
      </div>
    </Panel>
  );
}

export function SystemPage() {
  const { data: d, connectionState, error } = useSystemData();
  const { data: marketData } = useMarketData();
  const rt = d.runtime;

  return (
    <div className="space-y-3">
      <ConnectionBanner connectionState={connectionState} error={error} />
      <div className="grid grid-cols-5 gap-3">
        {d.services.map((s) => (
          <Panel key={s.name}>
            <div className="p-3">
              <StatusBadge status={s.status} label={toTitleCase(s.status)} />
              <div className="mt-2 text-sm font-medium">{s.name}</div>
              <div className="mt-0.5 text-xs text-muted">{s.detail}</div>
            </div>
          </Panel>
        ))}
      </div>

      <RuntimeMetricsPanel
        metrics={d.metrics}
        isRegularSessionOpen={marketData.marketSession.isRegularSessionOpen}
      />

      <div className="grid grid-cols-3 gap-3">
        <Panel title="Runtime" className="col-span-2">
          <div className="grid grid-cols-2 gap-x-8 p-3">
            <div className="space-y-0.5">
              <MetricRow label="Process uptime" value={fmtUptime(rt.uptimeSeconds)} />
              <MetricRow label="Memory usage" value={rt.memoryMb > 0 ? `${rt.memoryMb} MB` : "—"} />
              <MetricRow label="GC collections" value={rt.gcCollections > 0 ? String(rt.gcCollections) : "—"} />
            </div>
            <div className="space-y-0.5">
              <MetricRow label="Threads" value={rt.activeConnections > 0 ? String(rt.activeConnections) : "—"} />
              <MetricRow label="CPU usage" value={<span className="text-muted">N/A</span>} />
              <MetricRow label="Requests / sec" value={<span className="text-muted">N/A</span>} />
            </div>
          </div>
        </Panel>

        <Panel title="Notes">
          <div className="space-y-2 p-3 text-xs text-muted">
            <p>Prometheus metrics available at <code className="text-accent">/metrics</code> for scraping.</p>
            <p>CPU, request throughput, and error rates require additional middleware (future phase).</p>
          </div>
        </Panel>
      </div>
    </div>
  );
}
