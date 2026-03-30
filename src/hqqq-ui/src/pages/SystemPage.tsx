import { Panel } from "@/components/Panel";
import { StatusBadge } from "@/components/StatusBadge";
import { MetricRow } from "@/components/MetricRow";

const services = [
  { name: "API Server", status: "healthy" as const, latency: "2.1ms", detail: "hqqq-api :5015" },
  { name: "Kafka", status: "healthy" as const, latency: "4.8ms", detail: "KRaft cluster" },
  { name: "Redis", status: "healthy" as const, latency: "0.3ms", detail: "7.4.8-alpine" },
  { name: "TimescaleDB", status: "healthy" as const, latency: "1.2ms", detail: "2.17.2-pg16" },
  { name: "Market Feed", status: "healthy" as const, latency: "18ms", detail: "Finnhub WS" },
];

const pipelines = [
  { name: "Tick Ingestion", status: "healthy" as const, throughput: "824/s", lag: "0ms", last: "< 1s", errors: "0" },
  { name: "iNAV Calculation", status: "healthy" as const, throughput: "142/s", lag: "2ms", last: "< 1s", errors: "0" },
  { name: "Quote Publishing", status: "healthy" as const, throughput: "142/s", lag: "1ms", last: "< 1s", errors: "0" },
  { name: "History Writer", status: "healthy" as const, throughput: "142/s", lag: "48ms", last: "< 1s", errors: "0" },
];

export function SystemPage() {
  return (
    <div className="space-y-3">
      {/* health cards */}
      <div className="grid grid-cols-5 gap-3">
        {services.map((s) => (
          <Panel key={s.name}>
            <div className="p-3">
              <StatusBadge status={s.status} />
              <div className="mt-2 text-sm font-medium">{s.name}</div>
              <div className="mt-0.5 text-xs text-muted">{s.detail}</div>
              <div className="mt-2 flex items-baseline gap-1 font-mono text-xs">
                <span className="text-content">{s.latency}</span>
                <span className="text-muted">latency</span>
              </div>
            </div>
          </Panel>
        ))}
      </div>

      <div className="grid grid-cols-3 gap-3">
        {/* runtime metrics */}
        <Panel title="Runtime" className="col-span-2">
          <div className="grid grid-cols-2 gap-x-8 p-3">
            <div className="space-y-0.5">
              <MetricRow label="Process uptime" value="4h 23m 11s" />
              <MetricRow label="Memory usage" value="148 / 512 MB" />
              <MetricRow label="CPU usage" value="3.2%" />
              <MetricRow label="GC collections (gen0)" value="847" />
            </div>
            <div className="space-y-0.5">
              <MetricRow label="Active connections" value="7" />
              <MetricRow label="Requests / sec" value="142" />
              <MetricRow label="Avg response (p50)" value="1.4ms" />
              <MetricRow label="Error rate (5m)" value={<span className="text-positive">0.00%</span>} />
            </div>
          </div>
        </Panel>

        {/* diagnostics */}
        <Panel title="Recent Events">
          <div className="space-y-2 p-3 text-xs">
            <div className="flex gap-2">
              <span className="shrink-0 font-mono text-muted">14:28:03</span>
              <span>Basket composition refreshed (101 symbols)</span>
            </div>
            <div className="flex gap-2">
              <span className="shrink-0 font-mono text-muted">14:15:00</span>
              <span>Health check passed — all dependencies healthy</span>
            </div>
            <div className="flex gap-2">
              <span className="shrink-0 font-mono text-muted">10:02:41</span>
              <span className="text-positive">API server started on :5015</span>
            </div>
            <div className="flex gap-2">
              <span className="shrink-0 font-mono text-muted">10:02:38</span>
              <span>Connected to Kafka cluster (hqqq-dev-kafka-cluster-01)</span>
            </div>
          </div>
        </Panel>
      </div>

      {/* pipeline status */}
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
              {pipelines.map((p) => (
                <tr key={p.name} className="border-b border-edge/30 last:border-0">
                  <td className="px-3 py-1.5 font-medium">{p.name}</td>
                  <td className="px-3 py-1.5">
                    <StatusBadge status={p.status} />
                  </td>
                  <td className="px-3 py-1.5 text-right font-mono">{p.throughput}</td>
                  <td className="px-3 py-1.5 text-right font-mono">{p.lag}</td>
                  <td className="px-3 py-1.5 text-right font-mono text-muted">{p.last}</td>
                  <td className="px-3 py-1.5 text-right font-mono text-positive">{p.errors}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </div>
  );
}
