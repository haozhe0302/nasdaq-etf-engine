import { useMarketData, useNowTick, useLastSessionFallback } from "@/lib/hooks";
import { getChartMode, getChartBoundsUtc } from "@/lib/marketSessionClient";
import { Panel } from "@/components/Panel";
import { StatCard } from "@/components/StatCard";
import { Chart } from "@/components/Chart";
import { MetricRow } from "@/components/MetricRow";
import { StatusBadge } from "@/components/StatusBadge";
import type { EChartsOption } from "echarts";

const AX = { text: "#8b949e", grid: "#1e293b" };

function fmtAge(ageMs: number | null): string {
  if (ageMs == null || !Number.isFinite(ageMs)) return "\u2014";
  const secs = Math.max(0, Math.round(ageMs / 1000));
  if (secs < 60) return `${secs}s ago`;
  if (secs < 3600) return `${Math.floor(secs / 60)}m ${secs % 60}s ago`;
  return `${Math.floor(secs / 3600)}h ${Math.floor((secs % 3600) / 60)}m ago`;
}

function formatEtTime(utcMs: number): string {
  return new Date(utcMs).toLocaleTimeString("en-US", {
    timeZone: "America/New_York",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
}

function toTitleCase(text: string): string {
  return text
    .split(/[\s_-]+/)
    .filter(Boolean)
    .map((w) => w[0].toUpperCase() + w.slice(1).toLowerCase())
    .join(" ");
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

function SessionBanner({ snapshot }: { snapshot: ReturnType<typeof useMarketData>["data"] }) {
  if (snapshot.isLive) return null;

  if (!snapshot.marketSession.isRegularSessionOpen && snapshot.marketSession.state !== "unknown") {
    const nextOpen = snapshot.marketSession.nextOpenUtc
      ? new Date(snapshot.marketSession.nextOpenUtc).toLocaleString("en-US", {
          timeZone: "America/New_York",
          weekday: "short",
          hour: "2-digit",
          minute: "2-digit",
          hour12: true,
        })
      : null;

    return (
      <div className="rounded border border-blue-500/30 bg-blue-500/10 text-blue-400 px-3 py-1.5 text-xs">
        {snapshot.marketSession.label || "Market Closed"} / Market Closed
        {nextOpen && <span className="ml-1 text-blue-500/60">(Opens {nextOpen} ET)</span>}
      </div>
    );
  }

  if (snapshot.isFrozen) {
    return (
      <div className="rounded border border-yellow-500/30 bg-yellow-500/10 text-yellow-400 px-3 py-1.5 text-xs">
        Quote frozen \u2014 {snapshot.pauseReason || "all data sources stale"}
      </div>
    );
  }

  return null;
}

export function MarketPage() {
  const { data: d, connectionState, error } = useMarketData();
  const nowMs = useNowTick(1_000);
  const now = new Date(nowMs);

  const chartMode = getChartMode(now);
  const bounds = getChartBoundsUtc(now);

  // After-hours fallback: load the most recent completed regular session
  // from /api/history?range=1D when the live series is no longer appending.
  const fallbackSeries = useLastSessionFallback(chartMode === "after_hours");

  // Decide which series feeds the chart based on session mode.
  //  - regular:        live series (primary path).
  //  - pre_open_reset: blank ("waiting for market open").
  //  - after_hours:    live series if it still holds the most recent
  //                    completed session; otherwise the history fallback.
  let chartSource = d.series;
  if (chartMode === "pre_open_reset") {
    chartSource = [];
  } else if (chartMode === "after_hours") {
    const lastLiveMs = d.series.length
      ? new Date(d.series[d.series.length - 1].time).getTime()
      : 0;
    const liveCoversSession = Number.isFinite(lastLiveMs) && lastLiveMs >= bounds.open;
    chartSource = liveCoversSession && d.series.length ? d.series : fallbackSeries;
  }

  const hasSeries = chartSource.length > 0;

  // ── Freshness ages (driven by the 1s clock, from preserved timestamps) ──
  const asOfAgeMs = d.freshness.asOfUtc ? nowMs - new Date(d.freshness.asOfUtc).getTime() : null;
  const lastTickAgeMs = d.freshness.lastTickUtc ? nowMs - new Date(d.freshness.lastTickUtc).getTime() : null;
  const pricingActive =
    d.quoteState === "live" && asOfAgeMs != null && asOfAgeMs < 30_000;
  const transportLabel =
    connectionState === "live"
      ? "SignalR live"
      : connectionState === "stale"
        ? "REST fallback"
        : connectionState === "connecting"
          ? "Connecting"
          : "Disconnected";
  const sessionLabel =
    d.marketSession.label ||
    (chartMode === "regular"
      ? "Regular session"
      : chartMode === "pre_open_reset"
        ? "Pre-open"
        : "Closed");

  const navData = chartSource.map((p) => [p.time, p.nav]);
  const marketData = chartSource.map((p) => [p.time, p.market]);

  const pdData = chartSource.map((p) => {
    const bps = p.nav > 0 ? +(((p.market - p.nav) / p.nav) * 10000).toFixed(1) : 0;
    return { time: p.time, value: bps };
  });

  const chartCaption =
    chartMode === "after_hours"
      ? "Last regular session"
      : chartMode === "pre_open_reset"
        ? "Waiting for market open (09:30 ET)"
        : null;

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
      <SessionBanner snapshot={d} />
      <div className="grid grid-cols-5 gap-3">
        <StatCard label="Indicative NAV" value={`$${d.nav.toFixed(2)}`} sub={fmtPct(d.navChangePct)} status={d.navChangePct >= 0 ? "positive" : "negative"} />
        <StatCard label="Market Price" value={`$${d.marketPrice.toFixed(2)}`} sub={`$${(d.marketPrice - d.nav).toFixed(2)} vs NAV`} />
        <StatCard label="Premium / Discount" value={`${d.premiumDiscountPct.toFixed(4)}%`} status={d.premiumDiscountPct >= 0 ? "positive" : "negative"} />
        <StatCard label="QQQ Reference" value={`$${d.qqq.toFixed(2)}`} sub={fmtPct(d.qqqChangePct)} status={d.qqqChangePct >= 0 ? "positive" : "negative"} />
        <StatCard label="Basket Market Value" value={`$${d.basketValueB.toFixed(2)}B`} />
      </div>

      <div className="grid grid-cols-3 gap-3">
        <Panel title="iNAV vs Market Price" className="col-span-2">
          {chartCaption && (
            <div className="px-3 pt-2 text-[11px] text-muted">
              {chartCaption}
            </div>
          )}
          <Chart option={mainChart} className="h-64 p-1" />
        </Panel>
        <div className="flex flex-col gap-3">
          <Panel title="Premium / Discount (bps)">
            <Chart option={pdChart} className="h-[122px] p-1" />
          </Panel>
          <Panel title="Quote Freshness" className="flex-1">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Last quote update" value={fmtAge(asOfAgeMs)} />
              <MetricRow label="Last upstream tick" value={fmtAge(lastTickAgeMs)} />
              <MetricRow
                label="Symbol freshness"
                value={
                  d.freshness.totalSymbols > 0
                    ? `${d.freshness.totalSymbols - d.freshness.staleSymbols} / ${d.freshness.totalSymbols}${d.freshness.staleSymbols > 0 ? ` \u00b7 ${d.freshness.staleSymbols} stale` : ""}`
                    : "\u2014"
                }
              />
              <MetricRow
                label="Avg tick interval"
                value={d.freshness.avgTickIntervalMs > 0 ? `${d.freshness.avgTickIntervalMs}ms` : "\u2014"}
              />
              <MetricRow label="Transport" value={transportLabel} />
              <MetricRow
                label="Pricing"
                value={<StatusBadge status={pricingActive ? "healthy" : "degraded"} label={pricingActive ? "Active" : "Idle"} />}
              />
              <MetricRow label="Session" value={sessionLabel} />
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
              <MetricRow
                key={f.name}
                label={f.name}
                value={<StatusBadge status={f.status} label={toTitleCase(f.label ?? f.status)} />}
              />
            ))}
            <MetricRow label="Symbols Active" value={`${d.freshness.totalSymbols - d.freshness.staleSymbols} / ${d.freshness.totalSymbols}`} />
          </div>
        </Panel>
      </div>
    </div>
  );
}
