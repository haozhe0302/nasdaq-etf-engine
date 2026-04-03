import { useState, useEffect } from "react";
import { useConstituentData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { MetricRow } from "@/components/MetricRow";

function formatElapsed(ms: number): string {
  if (ms <= 0) return "—";
  const seconds = Math.floor(ms / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${seconds % 60}s ago`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m ago`;
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

export function ConstituentsPage() {
  const { data: d, connectionState, error } = useConstituentData();
  const [refreshAgo, setRefreshAgo] = useState("—");

  useEffect(() => {
    if (!d.lastRefreshAt) return;
    const tick = () => setRefreshAgo(formatElapsed(Date.now() - d.lastRefreshAt));
    tick();
    const id = setInterval(tick, 1_000);
    return () => clearInterval(id);
  }, [d.lastRefreshAt]);

  return (
    <div className="space-y-3">
      <ConnectionBanner connectionState={connectionState} error={error} />
      <div className="flex items-center gap-3 rounded border border-edge bg-surface px-3 py-2 text-xs">
        <input
          readOnly
          placeholder="Search symbol or name…"
          className="w-52 rounded border border-edge bg-canvas px-2 py-1 text-content placeholder:text-muted focus:outline-none"
        />
        <span className="text-edge">│</span>
        <span className="text-muted">Sort: Weight ↓</span>
        <span className="ml-auto text-muted">
          Showing <span className="text-content">{d.holdings.length}</span> of{" "}
          <span className="text-content">{d.totalCount}</span> constituents &middot; As of{" "}
          <span className="font-mono text-content">{d.asOfDate}</span>
        </span>
      </div>

      <div className="grid grid-cols-4 gap-3">
        <Panel className="col-span-3 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="border-b border-edge text-[11px] text-muted">
                  <th className="px-3 py-2 font-medium">#</th>
                  <th className="px-3 py-2 font-medium">Symbol</th>
                  <th className="px-3 py-2 font-medium">Name</th>
                  <th className="px-3 py-2 font-medium text-right">Weight</th>
                  <th className="px-3 py-2 font-medium text-right">Shares</th>
                  <th className="px-3 py-2 font-medium text-right">Price</th>
                  <th className="px-3 py-2 font-medium text-right">Chg %</th>
                  <th className="px-3 py-2 font-medium text-right">Mkt Value</th>
                </tr>
              </thead>
              <tbody>
                {d.holdings.map((h, i) => (
                  <tr key={h.symbol} className="border-b border-edge/30 hover:bg-overlay">
                    <td className="px-3 py-1.5 text-muted">{i + 1}</td>
                    <td className="px-3 py-1.5 font-mono font-medium text-accent">{h.symbol}</td>
                    <td className="px-3 py-1.5 text-muted">{h.name}</td>
                    <td className="px-3 py-1.5 text-right font-mono">{h.weight.toFixed(2)}%</td>
                    <td className="px-3 py-1.5 text-right font-mono text-muted">{h.shares.toLocaleString()}</td>
                    <td className="px-3 py-1.5 text-right font-mono">${h.price.toFixed(2)}</td>
                    <td className={`px-3 py-1.5 text-right font-mono ${h.changePct >= 0 ? "text-positive" : "text-negative"}`}>
                      {h.changePct >= 0 ? "+" : ""}{h.changePct.toFixed(2)}%
                    </td>
                    <td className="px-3 py-1.5 text-right font-mono text-muted">
                      ${((h.shares * h.price) / 1e9).toFixed(2)}B
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>

        <div className="flex flex-col gap-3">
          <Panel title="Concentration">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Top 5 weight" value={`${d.concentration.top5}%`} />
              <MetricRow label="Top 10 weight" value={`${d.concentration.top10}%`} />
              <MetricRow label="Top 20 weight" value={`${d.concentration.top20}%`} />
              <MetricRow label="HHI index" value={String(d.concentration.hhi)} />
            </div>
          </Panel>

          <Panel title="Top Weights">
            <div className="space-y-2.5 p-3">
              {d.holdings.slice(0, 5).map((h) => (
                <div key={h.symbol} className="text-xs">
                  <div className="mb-0.5 flex justify-between">
                    <span className="font-mono text-accent">{h.symbol}</span>
                    <span className="font-mono">{h.weight.toFixed(2)}%</span>
                  </div>
                  <div className="h-1 rounded bg-edge">
                    <div className="h-1 rounded bg-accent" style={{ width: `${(h.weight / 10) * 100}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </Panel>

          <Panel title="Data Quality">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Stale prices" value={<span className="text-positive">{d.quality.stalePrices}</span>} />
              <MetricRow label="Missing symbols" value={<span className="text-positive">{d.quality.missingSymbols}</span>} />
              <MetricRow label="Last refresh" value={refreshAgo} />
              <MetricRow label="Coverage" value={`${d.quality.coverage} / ${d.quality.totalSymbols}`} />
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}
