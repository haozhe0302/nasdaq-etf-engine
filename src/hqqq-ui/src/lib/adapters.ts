import type {
  MarketSnapshot,
  ConstituentSnapshot,
  SystemSnapshot,
  HistorySnapshot,
  TimeSeriesPoint,
  Mover,
  FreshnessMetrics,
  FeedStatus,
  Constituent,
  ConcentrationMetrics,
  DataQualityMetrics,
  ServiceHealth,
  HealthStatus,
  RuntimeMetricsData,
  HistoryPoint,
} from "./types";

// ── Backend DTO shapes (camelCase as serialized by ASP.NET Core) ──

interface BQuoteSnapshot {
  nav: number;
  navChangePct: number;
  marketPrice: number;
  premiumDiscountPct: number;
  qqq: number;
  basketValueB: number;
  asOf: string;
  series: { time: string; nav: number; market: number }[];
  movers: {
    symbol: string;
    name: string;
    changePct: number;
    impact: number;
    direction: string;
  }[];
  freshness: {
    symbolsTotal: number;
    symbolsFresh: number;
    symbolsStale: number;
    freshPct: number;
    lastTickUtc: string | null;
    avgTickIntervalMs: number | null;
  };
  feeds: {
    webSocketConnected: boolean;
    fallbackActive: boolean;
    pricingActive: boolean;
    basketState: string;
    pendingActivationBlocked: boolean;
    pendingBlockedReason: string | null;
  };
}

interface BConstituentSnapshot {
  holdings: {
    symbol: string;
    name: string;
    sector: string;
    weight: number;
    shares: number;
    price: number | null;
    changePct: number | null;
    marketValue: number | null;
    sharesOrigin: string;
    isStale: boolean;
  }[];
  concentration: {
    top5Pct: number;
    top10Pct: number;
    top20Pct: number;
    sectorCount: number;
    herfindahlIndex: number;
  };
  quality: {
    totalSymbols: number;
    officialSharesCount: number;
    derivedSharesCount: number;
    pricedCount: number;
    staleCount: number;
    priceCoveragePct: number;
    basketMode: string;
  };
  source: {
    anchorSource: string;
    tailSource: string;
    basketMode: string;
    isDegraded: boolean;
    asOfDate: string;
    fingerprint: string;
  };
  asOf: string;
}

interface BLatencyStats {
  p50: number;
  p95: number;
  p99: number;
  sampleCount: number;
}

interface BRuntimeMetrics {
  snapshotAgeMs: number;
  pricedWeightCoverage: number;
  staleSymbolRatio: number;
  tickToQuoteMs: BLatencyStats;
  quoteBroadcastMs: BLatencyStats;
  lastFailoverRecoverySeconds: number | null;
  lastActivationJumpBps: number | null;
  totalTicksIngested: number;
  totalQuoteBroadcasts: number;
  totalFallbackActivations: number;
  totalBasketActivations: number;
}

interface BSystemHealth {
  serviceName: string;
  status: string;
  checkedAtUtc: string;
  version: string;
  runtime: {
    uptimeSeconds: number;
    memoryMb: number;
    gcGen0: number;
    gcGen1: number;
    gcGen2: number;
    threadCount: number;
  };
  metrics?: BRuntimeMetrics | null;
  dependencies: {
    name: string;
    status: string;
    lastCheckedAtUtc: string;
    details: string | null;
  }[];
}

// ── Helpers ──────────────────────────────────────────

export function toHealthStatus(s: string): HealthStatus {
  if (s === "healthy" || s === "degraded" || s === "unhealthy" || s === "unknown")
    return s;
  if (s === "initializing" || s === "blocked") return "degraded";
  return "unknown";
}

// ── Quote → MarketSnapshot ──────────────────────────

export function adaptQuote(raw: unknown): MarketSnapshot {
  const q = raw as BQuoteSnapshot;
  const now = Date.now();
  const asOfMs = new Date(q.asOf).getTime();
  const asOfAgeMs = Number.isFinite(asOfMs) ? Math.max(0, Math.round(now - asOfMs)) : 0;
  const lastTickMs = q.freshness.lastTickUtc
    ? now - new Date(q.freshness.lastTickUtc).getTime()
    : 0;

  const series: TimeSeriesPoint[] = q.series.map((p) => ({
    time: p.time,
    nav: p.nav,
    market: p.market,
  }));

  const movers: Mover[] = q.movers.map((m) => ({
    symbol: m.symbol,
    changePct: m.changePct,
    impactBps: m.impact,
  }));

  const freshness: FreshnessMetrics = {
    lastNavCalcMs: asOfAgeMs,
    lastTickMs: Math.max(0, Math.round(lastTickMs)),
    networkLatencyMs: asOfAgeMs,
    avgTickIntervalMs: q.freshness.avgTickIntervalMs
      ? Math.round(q.freshness.avgTickIntervalMs)
      : 0,
    staleSymbols: q.freshness.symbolsStale,
    totalSymbols: q.freshness.symbolsTotal,
  };

  const feeds: FeedStatus[] = [
    {
      name: "Market Data",
      status: q.feeds.webSocketConnected ? "healthy" : "unhealthy",
    },
    {
      name: "Pricing Engine",
      status: q.feeds.pricingActive ? "healthy" : "unhealthy",
    },
    {
      name: "Basket",
      status: q.feeds.basketState === "active" ? "healthy" : "degraded",
      label: q.feeds.basketState,
    },
  ];

  if (q.feeds.fallbackActive) {
    feeds.push({ name: "REST Fallback", status: "degraded", label: "active" });
  }

  return {
    nav: q.nav,
    navChangePct: q.navChangePct,
    marketPrice: q.marketPrice,
    premiumDiscountPct: q.premiumDiscountPct,
    qqq: q.qqq,
    basketValueB: q.basketValueB,
    asOf: new Date(q.asOf),
    series,
    movers,
    freshness,
    feeds,
  };
}

// ── QuoteRealtimeUpdate → QuoteDelta (slim SignalR delta) ──

interface BQuoteRealtimeUpdate {
  nav: number;
  navChangePct: number;
  marketPrice: number;
  premiumDiscountPct: number;
  qqq: number;
  basketValueB: number;
  asOf: string;
  latestSeriesPoint: { time: string; nav: number; market: number } | null;
  movers: {
    symbol: string;
    name: string;
    changePct: number;
    impact: number;
    direction: string;
  }[];
  freshness: {
    symbolsTotal: number;
    symbolsFresh: number;
    symbolsStale: number;
    freshPct: number;
    lastTickUtc: string | null;
    avgTickIntervalMs: number | null;
  };
  feeds: {
    webSocketConnected: boolean;
    fallbackActive: boolean;
    pricingActive: boolean;
    basketState: string;
    pendingActivationBlocked: boolean;
    pendingBlockedReason: string | null;
  };
}

export interface QuoteDelta {
  nav: number;
  navChangePct: number;
  marketPrice: number;
  premiumDiscountPct: number;
  qqq: number;
  basketValueB: number;
  asOf: Date;
  latestSeriesPoint: TimeSeriesPoint | null;
  movers: Mover[];
  freshness: FreshnessMetrics;
  feeds: FeedStatus[];
}

export function adaptQuoteDelta(raw: unknown): QuoteDelta {
  const q = raw as BQuoteRealtimeUpdate;
  const now = Date.now();
  const asOfMs = new Date(q.asOf).getTime();
  const asOfAgeMs = Number.isFinite(asOfMs) ? Math.max(0, Math.round(now - asOfMs)) : 0;
  const lastTickMs = q.freshness.lastTickUtc
    ? now - new Date(q.freshness.lastTickUtc).getTime()
    : 0;

  const latestSeriesPoint: TimeSeriesPoint | null = q.latestSeriesPoint
    ? { time: q.latestSeriesPoint.time, nav: q.latestSeriesPoint.nav, market: q.latestSeriesPoint.market }
    : null;

  const movers: Mover[] = q.movers.map((m) => ({
    symbol: m.symbol,
    changePct: m.changePct,
    impactBps: m.impact,
  }));

  const freshness: FreshnessMetrics = {
    lastNavCalcMs: asOfAgeMs,
    lastTickMs: Math.max(0, Math.round(lastTickMs)),
    networkLatencyMs: asOfAgeMs,
    avgTickIntervalMs: q.freshness.avgTickIntervalMs
      ? Math.round(q.freshness.avgTickIntervalMs)
      : 0,
    staleSymbols: q.freshness.symbolsStale,
    totalSymbols: q.freshness.symbolsTotal,
  };

  const feeds: FeedStatus[] = [
    {
      name: "Market Data",
      status: q.feeds.webSocketConnected ? "healthy" : "unhealthy",
    },
    {
      name: "Pricing Engine",
      status: q.feeds.pricingActive ? "healthy" : "unhealthy",
    },
    {
      name: "Basket",
      status: q.feeds.basketState === "active" ? "healthy" : "degraded",
      label: q.feeds.basketState,
    },
  ];

  if (q.feeds.fallbackActive) {
    feeds.push({ name: "REST Fallback", status: "degraded", label: "active" });
  }

  return {
    nav: q.nav,
    navChangePct: q.navChangePct,
    marketPrice: q.marketPrice,
    premiumDiscountPct: q.premiumDiscountPct,
    qqq: q.qqq,
    basketValueB: q.basketValueB,
    asOf: new Date(q.asOf),
    latestSeriesPoint,
    movers,
    freshness,
    feeds,
  };
}

// ── Merge a slim delta into a full MarketSnapshot ───

const REGULAR_SESSION_MS = 6.5 * 60 * 60 * 1_000; // 9:30–16:00 ET
const SERIES_RECORD_INTERVAL_MS = 5_000; // matches backend PricingOptions.SeriesRecordIntervalMs default

/** Client-side cap: covers a full regular trading day plus a small buffer. */
export const MAX_SERIES_POINTS =
  Math.ceil(REGULAR_SESSION_MS / SERIES_RECORD_INTERVAL_MS) + 120;

export function mergeQuoteDelta(
  prev: MarketSnapshot,
  delta: QuoteDelta,
): MarketSnapshot {
  let series = prev.series;

  if (delta.latestSeriesPoint) {
    const incoming = delta.latestSeriesPoint;
    if (series.length === 0) {
      series = [incoming];
    } else {
      const lastTs = series[series.length - 1].time;
      if (incoming.time > lastTs) {
        series = [...series, incoming];
      } else if (incoming.time === lastTs) {
        series = [...series.slice(0, -1), incoming];
      }
      // incoming.time < lastTs → stale/out-of-order, ignore
    }
    if (series.length > MAX_SERIES_POINTS) {
      series = series.slice(series.length - MAX_SERIES_POINTS);
    }
  }

  return {
    nav: delta.nav,
    navChangePct: delta.navChangePct,
    marketPrice: delta.marketPrice,
    premiumDiscountPct: delta.premiumDiscountPct,
    qqq: delta.qqq,
    basketValueB: delta.basketValueB,
    asOf: delta.asOf,
    series,
    movers: delta.movers,
    freshness: delta.freshness,
    feeds: delta.feeds,
  };
}

// ── Constituents → ConstituentSnapshot ──────────────

export function adaptConstituents(raw: unknown): ConstituentSnapshot {
  const c = raw as BConstituentSnapshot;

  const holdings: Constituent[] = c.holdings.map((h) => ({
    symbol: h.symbol,
    name: h.name,
    weight: h.weight,
    shares: h.shares,
    price: h.price ?? 0,
    changePct: h.changePct ?? 0,
  }));

  const concentration: ConcentrationMetrics = {
    top5: c.concentration.top5Pct,
    top10: c.concentration.top10Pct,
    top20: c.concentration.top20Pct,
    hhi: c.concentration.herfindahlIndex,
  };

  const quality: DataQualityMetrics = {
    stalePrices: c.quality.staleCount,
    missingSymbols: c.quality.totalSymbols - c.quality.pricedCount,
    coverage: c.quality.pricedCount,
    totalSymbols: c.quality.totalSymbols,
  };

  return {
    asOfDate: c.source.asOfDate,
    totalCount: c.holdings.length,
    holdings,
    concentration,
    quality,
    lastRefreshAt: Date.now(),
  };
}

// ── SystemHealth → SystemSnapshot ───────────────────

export function adaptSystemHealth(raw: unknown): SystemSnapshot {
  const h = raw as BSystemHealth;

  const services: ServiceHealth[] = [
    {
      name: h.serviceName,
      status: toHealthStatus(h.status),
      latencyMs: 0,
      detail: `v${h.version}`,
    },
    ...h.dependencies.map((d) => ({
      name: d.name,
      status: toHealthStatus(d.status),
      latencyMs: 0,
      detail: d.details ?? "",
    })),
  ];

  const rt = h.runtime;

  const metrics: RuntimeMetricsData | undefined = h.metrics
    ? {
        snapshotAgeMs: h.metrics.snapshotAgeMs,
        pricedWeightCoverage: h.metrics.pricedWeightCoverage,
        staleSymbolRatio: h.metrics.staleSymbolRatio,
        tickToQuoteMs: h.metrics.tickToQuoteMs,
        quoteBroadcastMs: h.metrics.quoteBroadcastMs,
        lastFailoverRecoverySeconds: h.metrics.lastFailoverRecoverySeconds,
        lastActivationJumpBps: h.metrics.lastActivationJumpBps,
        totalTicksIngested: h.metrics.totalTicksIngested,
        totalQuoteBroadcasts: h.metrics.totalQuoteBroadcasts,
        totalFallbackActivations: h.metrics.totalFallbackActivations,
        totalBasketActivations: h.metrics.totalBasketActivations,
      }
    : undefined;

  return {
    services,
    runtime: {
      uptimeSeconds: rt?.uptimeSeconds ?? 0,
      memoryMb: rt?.memoryMb ?? 0,
      memoryMaxMb: 0,
      cpuPct: 0,
      gcCollections: rt ? rt.gcGen0 + rt.gcGen1 + rt.gcGen2 : 0,
      activeConnections: rt?.threadCount ?? 0,
      requestsPerSec: 0,
      avgResponseMs: 0,
      errorRatePct: 0,
    },
    metrics,
    pipelines: [],
    events: [],
  };
}

// ── History ─────────────────────────────────────────

interface BHistoryResponse {
  range: string;
  startDate: string;
  endDate: string;
  pointCount: number;
  totalPoints: number;
  isPartial: boolean;
  series: { time: string; nav: number; marketPrice: number }[];
  trackingError: {
    rmseBps: number;
    maxAbsBasisBps: number;
    avgAbsBasisBps: number;
    maxDeviationPct: number;
    correlation: number;
  };
  distribution: { label: string; count: number }[];
  diagnostics: {
    snapshots: number;
    gaps: number;
    completenessPct: number;
    daysLoaded: number;
  };
}

export function adaptHistory(raw: unknown): HistorySnapshot {
  const h = raw as BHistoryResponse;

  const series: HistoryPoint[] = (h.series ?? []).map((p) => {
    const d = new Date(p.time);
    const label = d.toLocaleString("en-US", {
      timeZone: "America/New_York",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
    });
    return { time: label, nav: p.nav, reference: p.marketPrice };
  });

  return {
    range: h.range ?? "1D",
    startDate: h.startDate ?? "",
    endDate: h.endDate ?? "",
    pointCount: h.pointCount ?? 0,
    totalPoints: h.totalPoints ?? 0,
    isPartial: h.isPartial ?? true,
    series,
    trackingError: h.trackingError ?? {
      rmseBps: 0, maxAbsBasisBps: 0, avgAbsBasisBps: 0,
      maxDeviationPct: 0, correlation: 0,
    },
    distribution: h.distribution ?? [],
    diagnostics: h.diagnostics ?? {
      snapshots: 0, gaps: 0, completenessPct: 0, daysLoaded: 0,
    },
  };
}

// ── Derive symbol count from health response ────────

export function deriveSymbolCount(raw: unknown): number {
  const h = raw as BSystemHealth;
  const basket = h.dependencies.find((d) => d.name === "basket");
  if (!basket?.details) return 0;
  const match = basket.details.match(/(\d+)\s+constituents/);
  return match ? parseInt(match[1], 10) : 0;
}
