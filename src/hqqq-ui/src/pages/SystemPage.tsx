import { useSystemData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { StatusBadge } from "@/components/StatusBadge";
import { MetricRow } from "@/components/MetricRow";

function fmtUptime(s: number): string {
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  return `${h}h ${m}m ${sec}s`;
}

export function SystemPage() {
  const d = useSystemData();
  const rt = d.runtime;

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-5 gap-3">
        {d.services.map((s) => (
          <Panel key={s.name}>
            <div className="p-3">
              <StatusBadge status={s.status} />
              <div className="mt-2 text-sm font-medium">{s.name}</div>
              <div className="mt-0.5 text-xs text-muted">{s.detail}</div>
              <div className="mt-2 flex items-baseline gap-1 font-mono text-xs">
                <span className="text-content">{s.latencyMs}ms</span>
                <span className="text-muted">latency</span>
              </div>
            </div>
          </Panel>
        ))}
      </div>

      <div className="grid grid-cols-3 gap-3">
        <Panel title="Runtime" className="col-span-2">
          <div className="grid grid-cols-2 gap-x-8 p-3">
            <div className="space-y-0.5">
              <MetricRow label="Process uptime" value={fmtUptime(rt.uptimeSeconds)} />
              <MetricRow label="Memory usage" value={`${rt.memoryMb} / ${rt.memoryMaxMb} MB`} />
              <MetricRow label="CPU usage" value={`${rt.cpuPct}%`} />
              <MetricRow label="GC collections (gen0)" value={String(rt.gcCollections)} />
            </div>
            <div className="space-y-0.5">
              <MetricRow label="Active connections" value={String(rt.activeConnections)} />
              <MetricRow label="Requests / sec" value={String(rt.requestsPerSec)} />
              <MetricRow label="Avg response (p50)" value={`${rt.avgResponseMs}ms`} />
              <MetricRow label="Error rate (5m)" value={<span className="text-positive">{rt.errorRatePct.toFixed(2)}%</span>} />
            </div>
          </div>
        </Panel>

        <Panel title="Recent Events">
          <div className="space-y-2 p-3 text-xs">
            {d.events.map((e, i) => (
              <div key={i} className="flex gap-2">
                <span className="shrink-0 font-mono text-muted">{e.time}</span>
                <span className={e.level === "success" ? "text-positive" : ""}>{e.message}</span>
              </div>
            ))}
          </div>
        </Panel>
      </div>

      <Panel title="Pipeline Status">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-edge text-[11px] text-muted">
                <th className="px-3 py-2 font-medium">Pipeline</th>
                <th className="px-3 py-2 font-medium">Status</th>
                <th className="px-3 py-2 font-medium text-right">Throughput</th>
                <th className="px-3 py-2 font-medium text-right">Lag</th>
                <th className="px-3 py-2 font-medium text-right">Last Processed</th>
                <th className="px-3 py-2 font-medium text-right">Errors (1h)</th>
              </tr>
            </thead>
            <tbody>
              {d.pipelines.map((p) => (
                <tr key={p.name} className="border-b border-edge/30 last:border-0">
                  <td className="px-3 py-1.5 font-medium">{p.name}</td>
                  <td className="px-3 py-1.5"><StatusBadge status={p.status} /></td>
                  <td className="px-3 py-1.5 text-right font-mono">{p.throughputPerSec}/s</td>
                  <td className="px-3 py-1.5 text-right font-mono">{p.lagMs}ms</td>
                  <td className="px-3 py-1.5 text-right font-mono text-muted">{p.lastProcessedAgo}</td>
                  <td className="px-3 py-1.5 text-right font-mono text-positive">{p.errorsLastHour}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </div>
  );
}
