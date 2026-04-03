import { useSystemData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { StatusBadge } from "@/components/StatusBadge";
import { MetricRow } from "@/components/MetricRow";

function fmtUptime(s: number): string {
  if (s <= 0) return "—";
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = Math.floor(s % 60);
  if (h > 0) return `${h}h ${m}m ${sec}s`;
  if (m > 0) return `${m}m ${sec}s`;
  return `${sec}s`;
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

export function SystemPage() {
  const { data: d, connectionState, error } = useSystemData();
  const rt = d.runtime;

  return (
    <div className="space-y-3">
      <ConnectionBanner connectionState={connectionState} error={error} />
      <div className="grid grid-cols-5 gap-3">
        {d.services.map((s) => (
          <Panel key={s.name}>
            <div className="p-3">
              <StatusBadge status={s.status} />
              <div className="mt-2 text-sm font-medium">{s.name}</div>
              <div className="mt-0.5 text-xs text-muted">{s.detail}</div>
            </div>
          </Panel>
        ))}
      </div>

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
            <p>CPU, request throughput, error rates, and pipeline metrics require instrumentation middleware (future phase).</p>
            <p>Future: Redis, Kafka, TimescaleDB, Prometheus, and Grafana are planned for caching, event streaming, persistence, and dashboards.</p>
          </div>
        </Panel>
      </div>
    </div>
  );
}
