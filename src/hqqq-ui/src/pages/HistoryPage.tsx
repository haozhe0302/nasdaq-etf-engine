import { Panel } from "@/components/Panel";
import { Chart } from "@/components/Chart";
import { MetricRow } from "@/components/MetricRow";
import type { EChartsOption } from "echarts";

// ── placeholder historical data (5 days, ~20 slots/day) ────
const H = 100;
const hTime: string[] = [];
const hNav: number[] = [];
const hRef: number[] = [];

for (let i = 0; i < H; i++) {
  const day = Math.floor(i / 20) + 24;
  const slot = i % 20;
  const h = 9 + Math.floor((30 + slot * 20) / 60);
  const m = (30 + slot * 20) % 60;
  hTime.push(`Mar ${day} ${h}:${m.toString().padStart(2, "0")}`);
  hNav.push(+(450 + Math.sin(i / 12) * 3 + Math.cos(i / 30) * 5 + i * 0.04).toFixed(2));
  hRef.push(+(449.8 + Math.sin(i / 12) * 3.1 + Math.cos(i / 30) * 4.8 + i * 0.04).toFixed(2));
}

const hPd = hNav.map((v, i) => +(((hRef[i] - v) / v) * 100).toFixed(3));

const buckets = Array.from({ length: 11 }, (_, i) => ({
  label: `${(-5 + i).toFixed(0)} bps`,
  count: Math.max(1, Math.round(18 - Math.abs(i - 5) * 3 + Math.sin(i) * 2)),
}));

const T = "#8b949e";
const G = "#1e293b";

const compChart: EChartsOption = {
  backgroundColor: "transparent",
  tooltip: { trigger: "axis" },
  legend: { right: 0, textStyle: { color: T, fontSize: 11 } },
  grid: { left: 50, right: 12, top: 30, bottom: 24 },
  xAxis: { data: hTime, axisLabel: { color: T, fontSize: 10 }, axisLine: { lineStyle: { color: G } }, splitLine: { show: false } },
  yAxis: { scale: true, axisLabel: { color: T, fontSize: 10 }, splitLine: { lineStyle: { color: G } } },
  series: [
    { name: "HQQQ iNAV", type: "line", data: hNav, symbol: "none", lineStyle: { width: 2, color: "#3b82f6" } },
    { name: "QQQ", type: "line", data: hRef, symbol: "none", lineStyle: { width: 1.5, color: "#8b949e" } },
  ],
};

const pdHistChart: EChartsOption = {
  backgroundColor: "transparent",
  tooltip: { trigger: "axis" },
  grid: { left: 50, right: 12, top: 8, bottom: 24 },
  xAxis: { data: hTime, axisLabel: { color: T, fontSize: 10 }, axisLine: { lineStyle: { color: G } }, splitLine: { show: false } },
  yAxis: { axisLabel: { color: T, fontSize: 10 }, splitLine: { lineStyle: { color: G } } },
  series: [{
    type: "line",
    data: hPd,
    symbol: "none",
    lineStyle: { width: 1.5, color: "#3b82f6" },
    areaStyle: { color: "rgba(59,130,246,0.08)" },
  }],
};

const distChart: EChartsOption = {
  backgroundColor: "transparent",
  tooltip: { trigger: "axis" },
  grid: { left: 45, right: 12, top: 8, bottom: 28 },
  xAxis: { type: "category", data: buckets.map((b) => b.label), axisLabel: { color: T, fontSize: 9 }, axisLine: { lineStyle: { color: G } } },
  yAxis: { axisLabel: { color: T, fontSize: 10 }, splitLine: { lineStyle: { color: G } } },
  series: [{ type: "bar", data: buckets.map((b) => b.count), itemStyle: { color: "#3b82f633" } }],
};

const ranges = ["1D", "5D", "1M", "3M", "YTD", "1Y"];

export function HistoryPage() {
  return (
    <div className="space-y-3">
      {/* toolbar */}
      <div className="flex items-center gap-3 rounded border border-edge bg-surface px-3 py-2 text-xs">
        <span className="font-medium text-content">Range</span>
        {ranges.map((r) => (
          <button
            key={r}
            className={`rounded px-2.5 py-1 text-xs font-medium ${
              r === "5D"
                ? "border border-accent/30 bg-accent/15 text-accent"
                : "border border-edge text-muted hover:text-content"
            }`}
          >
            {r}
          </button>
        ))}
        <span className="text-edge">│</span>
        <span className="text-muted">Interval:</span>
        <span className="rounded bg-accent/10 px-2 py-0.5 text-accent">15m</span>
        <span className="ml-auto font-mono text-muted">
          Mar 24 – Mar 28, 2026
        </span>
      </div>

      {/* historical comparison */}
      <Panel title="HQQQ iNAV vs QQQ Reference">
        <Chart option={compChart} className="h-56 p-1" />
      </Panel>

      {/* premium / discount history */}
      <Panel title="Premium / Discount History (%)">
        <Chart option={pdHistChart} className="h-40 p-1" />
      </Panel>

      {/* diagnostic panels */}
      <div className="grid grid-cols-3 gap-3">
        <Panel title="Tracking Error">
          <div className="space-y-0.5 p-3">
            <MetricRow label="TE (1D annualized)" value="0.012%" />
            <MetricRow label="TE (5D annualized)" value="0.018%" />
            <MetricRow label="Max daily deviation" value="0.041%" />
            <MetricRow label="Mean absolute P/D" value="0.8 bps" />
            <MetricRow label="Correlation (r²)" value="0.99997" />
          </div>
        </Panel>

        <Panel title="P/D Distribution (5D)">
          <Chart option={distChart} className="h-[140px] p-1" />
        </Panel>

        <Panel title="Replay Diagnostics">
          <div className="space-y-0.5 p-3">
            <MetricRow label="Snapshots recorded" value="1,847" />
            <MetricRow label="Gaps detected" value={<span className="text-positive">0</span>} />
            <MetricRow label="Max calc latency" value="8.4ms" />
            <MetricRow label="Avg calc latency" value="1.9ms" />
            <MetricRow label="Data completeness" value="100%" />
          </div>
        </Panel>
      </div>
    </div>
  );
}
