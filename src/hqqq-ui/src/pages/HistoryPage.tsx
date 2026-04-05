import { useState } from "react";
import { useHistoryData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { Chart } from "@/components/Chart";
import { MetricRow } from "@/components/MetricRow";
import type { EChartsOption } from "echarts";

const AX = { text: "#8b949e", grid: "#1e293b" };
const RANGES = ["1D", "5D", "1M", "3M", "YTD", "1Y"] as const;

function ConnectionBanner({ connectionState, error }: { connectionState: string; error?: string }) {
  if (connectionState === "live") return null;
  const isConnecting = connectionState === "connecting";
  const cls = isConnecting
    ? "border-accent/30 bg-accent/10 text-accent"
    : "border-yellow-500/30 bg-yellow-500/10 text-yellow-400";
  return (
    <div className={`rounded border px-3 py-1.5 text-xs ${cls}`}>
      {isConnecting ? "Loading history\u2026" : error ?? "Connection lost \u2014 showing last known data"}
    </div>
  );
}

export function HistoryPage() {
  const [range, setRange] = useState<string>("1D");
  const { data: d, connectionState, error } = useHistoryData(range);
  const hasSeries = d.series.length > 0;

  const times = d.series.map((p) => p.time);
  const pdPct = d.series.map((p) =>
    p.nav > 0 ? +(((p.reference - p.nav) / p.nav) * 100).toFixed(3) : 0,
  );

  const compChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    legend: { right: 0, textStyle: { color: AX.text, fontSize: 11 } },
    grid: { left: 50, right: 12, top: 30, bottom: 24 },
    xAxis: { data: times, axisLabel: { color: AX.text, fontSize: 10 }, axisLine: { lineStyle: { color: AX.grid } }, splitLine: { show: false } },
    yAxis: { scale: true, axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: hasSeries
      ? [
          { name: "HQQQ iNAV", type: "line", data: d.series.map((p) => p.nav), symbol: "none", lineStyle: { width: 2, color: "#3b82f6" } },
          { name: "QQQ", type: "line", data: d.series.map((p) => p.reference), symbol: "none", lineStyle: { width: 1.5, color: "#8b949e" } },
        ]
      : [],
  };

  const pdHistChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    grid: { left: 50, right: 12, top: 8, bottom: 24 },
    xAxis: { data: times, axisLabel: { color: AX.text, fontSize: 10 }, axisLine: { lineStyle: { color: AX.grid } }, splitLine: { show: false } },
    yAxis: { axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: hasSeries
      ? [{ type: "line", data: pdPct, symbol: "none", lineStyle: { width: 1.5, color: "#3b82f6" }, areaStyle: { color: "rgba(59,130,246,0.08)" } }]
      : [],
  };

  const distChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: { trigger: "axis" },
    grid: { left: 45, right: 12, top: 8, bottom: 28 },
    xAxis: { type: "category", data: d.distribution.map((b) => b.label), axisLabel: { color: AX.text, fontSize: 9 }, axisLine: { lineStyle: { color: AX.grid } } },
    yAxis: { axisLabel: { color: AX.text, fontSize: 10 }, splitLine: { lineStyle: { color: AX.grid } } },
    series: d.distribution.length > 0
      ? [{ type: "bar", data: d.distribution.map((b) => b.count), itemStyle: { color: "#3b82f633" } }]
      : [],
  };

  const te = d.trackingError;
  const diag = d.diagnostics;

  return (
    <div className="space-y-3">
      <ConnectionBanner connectionState={connectionState} error={error} />

      <div className="flex items-center gap-3 rounded border border-edge bg-surface px-3 py-2 text-xs">
        <span className="font-medium text-content">Range</span>
        {RANGES.map((r) => (
          <button
            key={r}
            onClick={() => setRange(r)}
            className={`rounded px-2.5 py-1 text-xs font-medium ${
              r === range
                ? "border border-accent/30 bg-accent/15 text-accent"
                : "border border-edge text-muted hover:text-content"
            }`}
          >
            {r}
          </button>
        ))}
        <span className="text-edge">│</span>
        {d.pointCount > 0 && (
          <>
            <span className="text-muted">{d.pointCount} pts</span>
            {d.isPartial && <span className="text-yellow-400">(partial)</span>}
          </>
        )}
        <span className="ml-auto font-mono text-muted">
          {d.startDate && d.endDate ? `${d.startDate} – ${d.endDate}` : "—"}
        </span>
      </div>

      {!hasSeries && connectionState === "live" ? (
        <Panel>
          <div className="flex h-48 items-center justify-center text-sm text-muted">
            No history data available for the selected range.
            {range === "1D" && " Data is recorded during market hours (9:30–16:00 ET)."}
          </div>
        </Panel>
      ) : (
        <>
          <Panel title="HQQQ iNAV vs QQQ Reference">
            <Chart option={compChart} className="h-56 p-1" />
          </Panel>

          <Panel title="Premium / Discount History (%)">
            <Chart option={pdHistChart} className="h-40 p-1" />
          </Panel>
        </>
      )}

      <div className="grid grid-cols-3 gap-3">
        <Panel title="Tracking Error">
          <div className="space-y-0.5 p-3">
            <MetricRow label="Basis RMSE" value={hasSeries ? `${te.rmseBps.toFixed(2)} bps` : "—"} />
            <MetricRow label="Max abs basis" value={hasSeries ? `${te.maxAbsBasisBps.toFixed(2)} bps` : "—"} />
            <MetricRow label="Avg abs basis" value={hasSeries ? `${te.avgAbsBasisBps.toFixed(2)} bps` : "—"} />
            <MetricRow label="Max deviation" value={hasSeries ? `${te.maxDeviationPct.toFixed(4)}%` : "—"} />
            <MetricRow label="Correlation (r)" value={hasSeries ? te.correlation.toFixed(5) : "—"} />
          </div>
        </Panel>

        <Panel title={`P/D Distribution (${range})`}>
          {d.distribution.length > 0 ? (
            <Chart option={distChart} className="h-[140px] p-1" />
          ) : (
            <div className="flex h-[140px] items-center justify-center text-xs text-muted">No data</div>
          )}
        </Panel>

        <Panel title="Diagnostics">
          <div className="space-y-0.5 p-3">
            <MetricRow label="Snapshots recorded" value={diag.snapshots.toLocaleString()} />
            <MetricRow label="Days loaded" value={String(diag.daysLoaded)} />
            <MetricRow label="Gaps detected" value={
              <span className={diag.gaps === 0 ? "text-positive" : "text-negative"}>{diag.gaps}</span>
            } />
            <MetricRow label="Data completeness" value={`${diag.completenessPct}%`} />
          </div>
        </Panel>
      </div>
    </div>
  );
}
