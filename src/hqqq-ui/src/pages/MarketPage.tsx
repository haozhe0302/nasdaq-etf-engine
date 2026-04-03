import { useMarketData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { StatCard } from "@/components/StatCard";
import { Chart } from "@/components/Chart";
import { MetricRow } from "@/components/MetricRow";
import { StatusBadge } from "@/components/StatusBadge";
import type { EChartsOption } from "echarts";

const AX = { text: "#8b949e", grid: "#1e293b" };

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

export function MarketPage() {
  const { data: d, connectionState, error } = useMarketData();

  const times = d.series.map((p) => p.time);
  const pdBps = d.series.map((p) => +(((p.market - p.nav) / p.nav) * 10000).toFixed(1));

  const mainChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    legend: { right: 0, textStyle: { color: AX.text, fontSize: 11 } },
    grid: { left: 50, right: 12, top: 30, bottom: 24 },
    xAxis: { data: times, axisLabel: { color: AX.text, fontSize: 10 }, axisLine: { lineStyle: { color: AX.grid } }, splitLine: { show: false } },
    yAxis: { scale: true, axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: [
      { name: "iNAV", type: "line", data: d.series.map((p) => p.nav), symbol: "none", lineStyle: { width: 2, color: "#3b82f6" } },
      { name: "Market", type: "line", data: d.series.map((p) => p.market), symbol: "none", lineStyle: { width: 1.5, color: "#22c55e" } },
    ],
  };

  const pdChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    grid: { left: 45, right: 12, top: 8, bottom: 20 },
    xAxis: { data: times, axisLabel: { show: false }, axisLine: { lineStyle: { color: AX.grid } }, splitLine: { show: false } },
    yAxis: { axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: [{
      type: "bar",
      data: pdBps.map((v) => ({ value: v, itemStyle: { color: v >= 0 ? "#22c55e44" : "#ef444444" } })),
    }],
  };

  const fmtPct = (v: number) => `${v >= 0 ? "+" : ""}${v.toFixed(3)}%`;

  return (
    <div className="space-y-3">
      <ConnectionBanner connectionState={connectionState} error={error} />
      <div className="grid grid-cols-5 gap-3">
        <StatCard label="Indicative NAV" value={`$${d.nav.toFixed(2)}`} sub={fmtPct(d.navChangePct)} status={d.navChangePct >= 0 ? "positive" : "negative"} />
        <StatCard label="Market Price" value={`$${d.marketPrice.toFixed(2)}`} sub={`$${(d.marketPrice - d.nav).toFixed(2)} vs NAV`} />
        <StatCard label="Premium / Discount" value={`${d.premiumDiscountPct.toFixed(4)}%`} status={d.premiumDiscountPct >= 0 ? "positive" : "negative"} />
        <StatCard label="QQQ Reference" value={`$${d.qqq.toFixed(2)}`} sub="NASDAQ" />
        <StatCard label="Basket Mkt Value" value={`$${d.basketValueB.toFixed(2)}B`} />
      </div>

      <div className="grid grid-cols-3 gap-3">
        <Panel title="iNAV vs Market Price" className="col-span-2">
          <Chart option={mainChart} className="h-64 p-1" />
        </Panel>
        <div className="flex flex-col gap-3">
          <Panel title="Premium / Discount (bps)">
            <Chart option={pdChart} className="h-[122px] p-1" />
          </Panel>
          <Panel title="Quote Freshness" className="flex-1">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Last iNAV calc" value={`${d.freshness.lastNavCalcMs}ms ago`} />
              <MetricRow label="Last tick received" value={`${d.freshness.lastTickMs}ms ago`} />
              <MetricRow label="Calc latency (p99)" value={<span className="text-positive">{d.freshness.calcLatencyP99Ms}ms</span>} />
              <MetricRow label="Stale symbols" value={`${d.freshness.staleSymbols} / ${d.freshness.totalSymbols}`} />
            </div>
          </Panel>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <Panel title="Top Movers (NAV Impact)">
          <table className="w-full text-xs">
            <tbody>
              {d.movers.map((m) => (
                <tr key={m.symbol} className="border-b border-edge/30 last:border-0">
                  <td className="px-3 py-1.5 font-mono font-medium text-accent">{m.symbol}</td>
                  <td className={`px-3 py-1.5 text-right font-mono ${m.changePct >= 0 ? "text-positive" : "text-negative"}`}>
                    {m.changePct >= 0 ? "+" : ""}{m.changePct.toFixed(2)}%
                  </td>
                  <td className="px-3 py-1.5 text-right font-mono text-muted">{m.impactBps >= 0 ? "+" : ""}{m.impactBps.toFixed(1)} bps</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>

        <Panel title="Basket Summary">
          <div className="space-y-0.5 p-3">
            <MetricRow label="Constituents" value={String(d.freshness.totalSymbols)} />
            <MetricRow label="Basket market value" value={`$${d.basketValueB.toFixed(2)}B`} />
            <MetricRow label="Cash component" value="$2.14M" />
            <MetricRow label="Shares outstanding" value="40.6M" />
            <MetricRow label="Creation unit" value="50,000 shs" />
          </div>
        </Panel>

        <Panel title="Feed Status">
          <div className="space-y-0.5 p-3">
            {d.feeds.map((f) => (
              <MetricRow key={f.name} label={f.name} value={<StatusBadge status={f.status} label={f.label} />} />
            ))}
            <MetricRow label="Symbols active" value={`${d.freshness.totalSymbols - d.freshness.staleSymbols} / ${d.freshness.totalSymbols}`} />
            <MetricRow label="Avg tick interval" value={`${d.freshness.avgTickIntervalMs}ms`} />
          </div>
        </Panel>
      </div>
    </div>
  );
}
