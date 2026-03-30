import { Panel } from "@/components/Panel";
import { StatCard } from "@/components/StatCard";
import { Chart } from "@/components/Chart";
import { MetricRow } from "@/components/MetricRow";
import { StatusBadge } from "@/components/StatusBadge";
import type { EChartsOption } from "echarts";

// ── placeholder series ──────────────────────────────
const N = 78;
const time: string[] = [];
const nav: number[] = [];
const mkt: number[] = [];

for (let i = 0; i < N; i++) {
  const m = Math.floor(i * (390 / N));
  time.push(
    `${9 + Math.floor((30 + m) / 60)}:${((30 + m) % 60).toString().padStart(2, "0")}`,
  );
  nav.push(+(453 + Math.sin(i / 8) * 2 + Math.sin(i / 3) * 0.5 + i * 0.02).toFixed(2));
  mkt.push(+(453 + Math.sin(i / 8) * 2 + Math.sin(i / 3.5) * 0.6 + i * 0.018 - 0.15).toFixed(2));
}

const pd = nav.map((v, i) => +(((mkt[i] - v) / v) * 10000).toFixed(1));

const T = "#8b949e";
const G = "#1e293b";

const mainChart: EChartsOption = {
  backgroundColor: "transparent",
  tooltip: { trigger: "axis" },
  legend: { right: 0, textStyle: { color: T, fontSize: 11 } },
  grid: { left: 50, right: 12, top: 30, bottom: 24 },
  xAxis: {
    data: time,
    axisLabel: { color: T, fontSize: 10 },
    axisLine: { lineStyle: { color: G } },
    splitLine: { show: false },
  },
  yAxis: {
    scale: true,
    axisLabel: { color: T, fontSize: 10 },
    splitLine: { lineStyle: { color: G } },
  },
  series: [
    { name: "iNAV", type: "line", data: nav, symbol: "none", lineStyle: { width: 2, color: "#3b82f6" } },
    { name: "Market", type: "line", data: mkt, symbol: "none", lineStyle: { width: 1.5, color: "#22c55e" } },
  ],
};

const pdChart: EChartsOption = {
  backgroundColor: "transparent",
  tooltip: { trigger: "axis" },
  grid: { left: 45, right: 12, top: 8, bottom: 20 },
  xAxis: {
    data: time,
    axisLabel: { show: false },
    axisLine: { lineStyle: { color: G } },
    splitLine: { show: false },
  },
  yAxis: {
    axisLabel: { color: T, fontSize: 10, formatter: "{value}" },
    splitLine: { lineStyle: { color: G } },
  },
  series: [
    {
      type: "bar",
      data: pd.map((v) => ({
        value: v,
        itemStyle: { color: v >= 0 ? "#22c55e33" : "#ef444433" },
      })),
    },
  ],
};

// ── static placeholder tables ───────────────────────
const movers = [
  { symbol: "NVDA", change: 2.87, impact: "+22.4" },
  { symbol: "AAPL", change: 1.24, impact: "+11.1" },
  { symbol: "AMZN", change: 0.45, impact: "+2.5" },
  { symbol: "TSLA", change: -2.14, impact: "-4.9" },
  { symbol: "META", change: -1.12, impact: "-5.5" },
];

export function MarketPage() {
  return (
    <div className="space-y-3">
      {/* KPI strip */}
      <div className="grid grid-cols-5 gap-3">
        <StatCard label="Indicative NAV" value="$453.27" sub="+0.34%" status="positive" />
        <StatCard label="Market Price" value="$453.12" sub="-$0.15 vs NAV" />
        <StatCard label="Premium / Discount" value="-0.033%" status="negative" />
        <StatCard label="QQQ Reference" value="$452.89" sub="NASDAQ" />
        <StatCard label="Basket Mkt Value" value="$18.42B" />
      </div>

      {/* primary charts */}
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
              <MetricRow label="Last iNAV calc" value="42ms ago" />
              <MetricRow label="Last tick received" value="18ms ago" />
              <MetricRow label="Calc latency (p99)" value={<span className="text-positive">2.1ms</span>} />
              <MetricRow label="Stale symbols" value="0 / 101" />
            </div>
          </Panel>
        </div>
      </div>

      {/* secondary panels */}
      <div className="grid grid-cols-3 gap-3">
        <Panel title="Top Movers (NAV Impact)">
          <table className="w-full text-xs">
            <tbody>
              {movers.map((m) => (
                <tr key={m.symbol} className="border-b border-edge/30 last:border-0">
                  <td className="px-3 py-1.5 font-mono font-medium text-accent">{m.symbol}</td>
                  <td className={`px-3 py-1.5 text-right font-mono ${m.change >= 0 ? "text-positive" : "text-negative"}`}>
                    {m.change >= 0 ? "+" : ""}{m.change.toFixed(2)}%
                  </td>
                  <td className="px-3 py-1.5 text-right font-mono text-muted">{m.impact} bps</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>

        <Panel title="Basket Summary">
          <div className="space-y-0.5 p-3">
            <MetricRow label="Constituents" value="101" />
            <MetricRow label="Basket market value" value="$18.42B" />
            <MetricRow label="Cash component" value="$2.14M" />
            <MetricRow label="Shares outstanding" value="40.6M" />
            <MetricRow label="Creation unit" value="50,000 shs" />
          </div>
        </Panel>

        <Panel title="Feed Status">
          <div className="space-y-0.5 p-3">
            <MetricRow label="Finnhub" value={<StatusBadge status="healthy" />} />
            <MetricRow label="Polygon" value={<StatusBadge status="healthy" />} />
            <MetricRow label="Backup feed" value={<StatusBadge status="unknown" label="standby" />} />
            <MetricRow label="Symbols active" value="101 / 101" />
            <MetricRow label="Avg tick interval" value="124ms" />
          </div>
        </Panel>
      </div>
    </div>
  );
}
