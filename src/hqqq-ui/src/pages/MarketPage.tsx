import { useMemo } from "react";
import { useMarketData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { StatCard } from "@/components/StatCard";
import { Chart } from "@/components/Chart";
import { MetricRow } from "@/components/MetricRow";
import { StatusBadge } from "@/components/StatusBadge";
import type { EChartsOption } from "echarts";

const AX = { text: "#8b949e", grid: "#1e293b" };

function getMarketBoundsUtc(): { open: number; close: number } {
  const now = new Date();
  const todayEt = new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(now);

  const utcParsed = new Date(now.toLocaleString("en-US", { timeZone: "UTC" }));
  const nyParsed = new Date(now.toLocaleString("en-US", { timeZone: "America/New_York" }));
  const offsetMs = utcParsed.getTime() - nyParsed.getTime();

  return {
    open: new Date(`${todayEt}T09:30:00Z`).getTime() + offsetMs,
    close: new Date(`${todayEt}T16:00:00Z`).getTime() + offsetMs,
  };
}

function formatEtTime(utcMs: number): string {
  return new Date(utcMs).toLocaleTimeString("en-US", {
    timeZone: "America/New_York",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
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

export function MarketPage() {
  const { data: d, connectionState, error } = useMarketData();
  const bounds = useMemo(() => getMarketBoundsUtc(), []);
  const hasSeries = d.series.length > 0;
  const networkLatencyMs = Number.isFinite(d.freshness.networkLatencyMs)
    ? d.freshness.networkLatencyMs
    : 0;

  const navData = d.series.map((p) => [p.time, p.nav]);
  const marketData = d.series.map((p) => [p.time, p.market]);

  const pdData = d.series.map((p) => {
    const bps = p.nav > 0 ? +(((p.market - p.nav) / p.nav) * 10000).toFixed(1) : 0;
    return { time: p.time, value: bps };
  });

  const mainChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: {
      trigger: "axis",
      formatter: (params: unknown) => {
        const items = params as { value: [string, number]; seriesName: string; color: string }[];
        if (!items?.length) return "";
        const time = formatEtTime(new Date(items[0].value[0]).getTime());
        const lines = items.map(
          (i) => `<span style="color:${i.color}">\u25CF</span> ${i.seriesName}: $${i.value[1].toFixed(2)}`,
        );
        return `${time} ET<br/>${lines.join("<br/>")}`;
      },
    },
    legend: { right: 0, textStyle: { color: AX.text, fontSize: 11 } },
    grid: { left: 50, right: 12, top: 30, bottom: 24 },
    xAxis: {
      type: "time",
      min: bounds.open,
      max: bounds.close,
      axisLabel: {
        color: AX.text,
        fontSize: 10,
        showMinLabel: true,
        showMaxLabel: true,
        formatter: (value: number) => formatEtTime(value),
      },
      axisLine: { lineStyle: { color: AX.grid } },
      splitLine: { show: false },
    },
    yAxis: {
      scale: true,
      axisLabel: { color: AX.text, fontSize: 10 },
      splitLine: { lineStyle: { color: AX.grid } },
    },
    series: hasSeries
      ? [
          { name: "iNAV", type: "line", data: navData, symbol: "none", lineStyle: { width: 2, color: "#3b82f6" } },
          { name: "Market", type: "line", data: marketData, symbol: "none", lineStyle: { width: 1.5, color: "#22c55e" } },
        ]
      : [],
  };

  const pdChart: EChartsOption = {
    backgroundColor: "transparent",
    animation: false,
    tooltip: {
      trigger: "axis",
      formatter: (params: unknown) => {
        const items = params as { value: [string, number] }[];
        if (!items?.length) return "";
        const time = formatEtTime(new Date(items[0].value[0]).getTime());
        return `${time} ET<br/>${items[0].value[1].toFixed(1)} bps`;
      },
    },
    grid: { left: 45, right: 12, top: 8, bottom: 20 },
    xAxis: {
      type: "time",
      min: bounds.open,
      max: bounds.close,
      axisLabel: { show: false },
      axisLine: { lineStyle: { color: AX.grid } },
      splitLine: { show: false },
    },
    yAxis: {
      axisLabel: { color: AX.text, fontSize: 10 },
      splitLine: { lineStyle: { color: AX.grid } },
    },
    series: hasSeries
      ? [{
          type: "bar",
          data: pdData.map((p) => ({
            value: [p.time, p.value],
            itemStyle: { color: p.value >= 0 ? "#22c55e44" : "#ef444444" },
          })),
        }]
      : [],
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
        <StatCard label="Basket Market Value" value={`$${d.basketValueB.toFixed(2)}B`} />
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
              <MetricRow label="Last iNAV Calc" value={`${d.freshness.lastNavCalcMs}ms ago`} />
              <MetricRow label="Last Tick Received" value={`${d.freshness.lastTickMs}ms ago`} />
              <MetricRow label="Network Latency" value={`${networkLatencyMs}ms`} />
              <MetricRow label="Stale Symbols" value={`${d.freshness.staleSymbols} / ${d.freshness.totalSymbols}`} />
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
            <MetricRow label="Basket Market Value" value={`$${d.basketValueB.toFixed(2)}B`} />
            <MetricRow label="Avg Tick Interval" value={`${d.freshness.avgTickIntervalMs}ms`} />
          </div>
        </Panel>

        <Panel title="Feed Status">
          <div className="space-y-0.5 p-3">
            {d.feeds.map((f) => (
              <MetricRow key={f.name} label={f.name} value={<StatusBadge status={f.status} label={f.label} />} />
            ))}
            <MetricRow label="Symbols Active" value={`${d.freshness.totalSymbols - d.freshness.staleSymbols} / ${d.freshness.totalSymbols}`} />
          </div>
        </Panel>
      </div>
    </div>
  );
}
