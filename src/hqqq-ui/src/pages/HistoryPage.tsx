import { useHistoryData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { Chart } from "@/components/Chart";
import { MetricRow } from "@/components/MetricRow";
import type { EChartsOption } from "echarts";

const AX = { text: "#8b949e", grid: "#1e293b" };
const RANGES = ["1D", "5D", "1M", "3M", "YTD", "1Y"];

export function HistoryPage() {
  const d = useHistoryData();

  const times = d.series.map((p) => p.time);
  const pdPct = d.series.map((p) => +(((p.reference - p.nav) / p.nav) * 100).toFixed(3));

  const compChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    legend: { right: 0, textStyle: { color: AX.text, fontSize: 11 } },
    grid: { left: 50, right: 12, top: 30, bottom: 24 },
    xAxis: { data: times, axisLabel: { color: AX.text, fontSize: 10 }, axisLine: { lineStyle: { color: AX.grid } }, splitLine: { show: false } },
    yAxis: { scale: true, axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: [
      { name: "HQQQ iNAV", type: "line", data: d.series.map((p) => p.nav), symbol: "none", lineStyle: { width: 2, color: "#3b82f6" } },
      { name: "QQQ", type: "line", data: d.series.map((p) => p.reference), symbol: "none", lineStyle: { width: 1.5, color: "#8b949e" } },
    ],
  };

  const pdHistChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    grid: { left: 50, right: 12, top: 8, bottom: 24 },
    xAxis: { data: times, axisLabel: { color: AX.text, fontSize: 10 }, axisLine: { lineStyle: { color: AX.grid } }, splitLine: { show: false } },
    yAxis: { axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: [{ type: "line", data: pdPct, symbol: "none", lineStyle: { width: 1.5, color: "#3b82f6" }, areaStyle: { color: "rgba(59,130,246,0.08)" } }],
  };

  const distChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    grid: { left: 45, right: 12, top: 8, bottom: 28 },
    xAxis: { type: "category", data: d.distribution.map((b) => b.label), axisLabel: { color: AX.text, fontSize: 9 }, axisLine: { lineStyle: { color: AX.grid } } },
    yAxis: { axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: [{ type: "bar", data: d.distribution.map((b) => b.count), itemStyle: { color: "#3b82f633" } }],
  };

  const te = d.trackingError;
  const diag = d.diagnostics;

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-3 rounded border border-edge bg-surface px-3 py-2 text-xs">
        <span className="font-medium text-content">Range</span>
        {RANGES.map((r) => (
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
          {d.series[0]?.time} – {d.series[d.series.length - 1]?.time}
        </span>
      </div>

      <Panel title="HQQQ iNAV vs QQQ Reference">
        <Chart option={compChart} className="h-56 p-1" />
      </Panel>

      <Panel title="Premium / Discount History (%)">
        <Chart option={pdHistChart} className="h-40 p-1" />
      </Panel>

      <div className="grid grid-cols-3 gap-3">
        <Panel title="Tracking Error">
          <div className="space-y-0.5 p-3">
            <MetricRow label="TE (1D annualized)" value={`${te.te1dPct}%`} />
            <MetricRow label="TE (5D annualized)" value={`${te.te5dPct}%`} />
            <MetricRow label="Max daily deviation" value={`${te.maxDeviationPct}%`} />
            <MetricRow label="Mean absolute P/D" value={`${te.meanAbsPdBps} bps`} />
            <MetricRow label="Correlation (r²)" value={String(te.correlation)} />
          </div>
        </Panel>

        <Panel title="P/D Distribution (5D)">
          <Chart option={distChart} className="h-[140px] p-1" />
        </Panel>

        <Panel title="Replay Diagnostics">
          <div className="space-y-0.5 p-3">
            <MetricRow label="Snapshots recorded" value={diag.snapshots.toLocaleString()} />
            <MetricRow label="Gaps detected" value={<span className="text-positive">{diag.gaps}</span>} />
            <MetricRow label="Max calc latency" value={`${diag.maxLatencyMs}ms`} />
            <MetricRow label="Avg calc latency" value={`${diag.avgLatencyMs}ms`} />
            <MetricRow label="Data completeness" value={`${diag.completenessPct}%`} />
          </div>
        </Panel>
      </div>
    </div>
  );
}
