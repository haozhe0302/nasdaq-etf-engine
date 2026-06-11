import type {
  MarketSnapshot,
  MarketSessionInfo,
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
  qqqChangePct: number;
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
    marketSessionState?: string;
    isRegularSessionOpen?: boolean;
    isTradingDay?: boolean;
    nextOpenUtc?: string | null;
    sessionLabel?: string;
  };
  quoteState?: string;
  isLive?: boolean;
  isFrozen?: boolean;
  pauseReason?: string | null;
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

interface BUpstreamDiagnostics {
  webSocketConnected: boolean;
  fallbackActive: boolean;
  lastUpstreamError: string | null;
  lastUpstreamErrorCode: number | null;
  lastUpstreamErrorAtUtc: string | null;
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
  upstream?: BUpstreamDiagnostics | null;
  dependencies: {
    name: string;
    status: string;
    lastCheckedAtUtc: string;
    details: string | null;
  }[];
}

// ── Helpers ──────────────────────────────────────────

export function toHealthStatus(s: string): HealthStatus {
  if (
    s === "healthy" ||
    s === "degraded" ||
    s === "unhealthy" ||
    s === "unknown" ||
    s === "idle"
  )
    return s;
  if (s === "initializing" || s === "blocked") return "degraded";
  return "unknown";
}

// ── Session-window helpers for market series ────────

type SessionWindowMode = "regular_open" | "closed_session" | "pre_open_empty" | "passthrough";

export interface SessionSeriesWindow {
  mode: SessionWindowMode;
  windowStartUtcMs: number | null;
  windowEndUtcMs: number | null;
}

function getEtDateParts(date: Date): { year: number; month: number; day: number } {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(date);
  return {
    year: Number(parts.find((p) => p.type === "year")?.value ?? "1970"),
    month: Number(parts.find((p) => p.type === "month")?.value ?? "01"),
    day: Number(parts.find((p) => p.type === "day")?.value ?? "01"),
  };
}

function getTimeZoneOffsetMs(date: Date, timeZone: string): number {
  const utcParsed = new Date(date.toLocaleString("en-US", { timeZone: "UTC" }));
  const tzParsed = new Date(date.toLocaleString("en-US", { timeZone }));
  return utcParsed.getTime() - tzParsed.getTime();
}

function etWallClockToUtcMs(year: number, month: number, day: number, hour: number, minute: number): number {
  const localDate = new Date(Date.UTC(year, month - 1, day, hour, minute, 0));
  return localDate.getTime() + getTimeZoneOffsetMs(localDate, "America/New_York");
}

function getEtRegularWindowForDate(year: number, month: number, day: number): { startUtcMs: number; endUtcMs: number } {
  return {
    startUtcMs: etWallClockToUtcMs(year, month, day, 9, 30),
    endUtcMs: etWallClockToUtcMs(year, month, day, 16, 0),
  };
}

function getMostRecentCompletedEtSessionWindow(nowUtcMs: number): { startUtcMs: number; endUtcMs: number } {
  const now = new Date(nowUtcMs);
  const p = getEtDateParts(now);
  // Use 12:00 UTC as a stable anchor so ET conversion always stays on the
  // intended calendar date while we walk backward by whole days.
  let candidate = new Date(Date.UTC(p.year, p.month - 1, p.day, 12, 0, 0));
  const todayWindow = getEtRegularWindowForDate(p.year, p.month, p.day);
  const afterTodayClose = nowUtcMs >= todayWindow.endUtcMs;

  if (!afterTodayClose) {
    candidate.setUTCDate(candidate.getUTCDate() - 1);
  }

  while (true) {
    const c = getEtDateParts(candidate);
    const dow = new Date(Date.UTC(c.year, c.month - 1, c.day)).getUTCDay();
    if (dow !== 0 && dow !== 6) {
      return getEtRegularWindowForDate(c.year, c.month, c.day);
    }
    candidate.setUTCDate(candidate.getUTCDate() - 1);
  }
}

export function resolveSessionSeriesWindow(
  marketSession: MarketSessionInfo,
  asOf: Date,
  now: Date = new Date(),
): SessionSeriesWindow {
  const state = marketSession.state?.toLowerCase() ?? "unknown";
  const refDate = new Date(asOf.getTime());
  const refParts = getEtDateParts(refDate);
  const regularWindow = getEtRegularWindowForDate(refParts.year, refParts.month, refParts.day);

  if (state === "pre_open") {
    return { mode: "pre_open_empty", windowStartUtcMs: null, windowEndUtcMs: null };
  }

  if (state === "regular_open" || marketSession.isRegularSessionOpen) {
    return {
      mode: "regular_open",
      windowStartUtcMs: regularWindow.startUtcMs,
      windowEndUtcMs: regularWindow.endUtcMs,
    };
  }

  if (state !== "unknown") {
    const closedWindow = getMostRecentCompletedEtSessionWindow(now.getTime());
    return {
      mode: "closed_session",
      windowStartUtcMs: closedWindow.startUtcMs,
      windowEndUtcMs: closedWindow.endUtcMs,
    };
  }

  return { mode: "passthrough", windowStartUtcMs: null, windowEndUtcMs: null };
}

function trimSeriesToWindow(
  series: TimeSeriesPoint[],
  window: SessionSeriesWindow,
): TimeSeriesPoint[] {
  if (window.mode === "pre_open_empty") return [];
  if (window.mode === "passthrough") return series;
  if (window.windowStartUtcMs === null || window.windowEndUtcMs === null) return series;

  return series.filter((p) => {
    const ts = new Date(p.time).getTime();
    return Number.isFinite(ts) && ts >= window.windowStartUtcMs && ts < window.windowEndUtcMs;
  });
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

  const rawSeries: TimeSeriesPoint[] = q.series.map((p) => ({
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
    networkLatencyMs: 0,
    avgTickIntervalMs: q.freshness.avgTickIntervalMs
      ? Math.round(q.freshness.avgTickIntervalMs)
      : 0,
    staleSymbols: q.freshness.symbolsStale,
    totalSymbols: q.freshness.symbolsTotal,
  };

  const feeds = buildFeeds(q.feeds);

  const marketSession: MarketSessionInfo = {
    state: q.feeds.marketSessionState ?? "unknown",
    label: q.feeds.sessionLabel ?? "",
    isRegularSessionOpen: q.feeds.isRegularSessionOpen ?? false,
    isTradingDay: q.feeds.isTradingDay ?? false,
    nextOpenUtc: q.feeds.nextOpenUtc ?? null,
  };
  const asOf = new Date(q.asOf);
  const window = resolveSessionSeriesWindow(marketSession, asOf, asOf);
  const series = trimSeriesToWindow(rawSeries, window);

  return {
    nav: q.nav,
    navChangePct: q.navChangePct,
    marketPrice: q.marketPrice,
    premiumDiscountPct: q.premiumDiscountPct,
    qqq: q.qqq,
    qqqChangePct: q.qqqChangePct ?? 0,
    basketValueB: q.basketValueB,
    asOf,
    series,
    movers,
    freshness,
    feeds,
    quoteState: q.quoteState ?? "live",
    isLive: q.isLive ?? true,
    isFrozen: q.isFrozen ?? false,
    pauseReason: q.pauseReason ?? null,
    marketSession,
  };
}

// ── QuoteRealtimeUpdate → QuoteDelta (slim SignalR delta) ──

interface BQuoteRealtimeUpdate {
  nav: number;
  navChangePct: number;
  marketPrice: number;
  premiumDiscountPct: number;
  qqq: number;
  qqqChangePct: number;
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
    marketSessionState?: string;
    isRegularSessionOpen?: boolean;
    isTradingDay?: boolean;
    nextOpenUtc?: string | null;
    sessionLabel?: string;
  };
  quoteState?: string;
  isLive?: boolean;
  isFrozen?: boolean;
  pauseReason?: string | null;
}

export interface QuoteDelta {
  nav: number;
  navChangePct: number;
  marketPrice: number;
  premiumDiscountPct: number;
  qqq: number;
  qqqChangePct: number;
  basketValueB: number;
  asOf: Date;
  latestSeriesPoint: TimeSeriesPoint | null;
  movers: Mover[];
  freshness: FreshnessMetrics;
  feeds: FeedStatus[];
  quoteState: string;
  isLive: boolean;
  isFrozen: boolean;
  pauseReason: string | null;
  marketSession: MarketSessionInfo;
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
    networkLatencyMs: 0,
    avgTickIntervalMs: q.freshness.avgTickIntervalMs
      ? Math.round(q.freshness.avgTickIntervalMs)
      : 0,
    staleSymbols: q.freshness.symbolsStale,
    totalSymbols: q.freshness.symbolsTotal,
  };

  const feeds = buildFeeds(q.feeds);

  const marketSession: MarketSessionInfo = {
    state: q.feeds.marketSessionState ?? "unknown",
    label: q.feeds.sessionLabel ?? "",
    isRegularSessionOpen: q.feeds.isRegularSessionOpen ?? false,
    isTradingDay: q.feeds.isTradingDay ?? false,
    nextOpenUtc: q.feeds.nextOpenUtc ?? null,
  };

  return {
    nav: q.nav,
    navChangePct: q.navChangePct,
    marketPrice: q.marketPrice,
    premiumDiscountPct: q.premiumDiscountPct,
    qqq: q.qqq,
    qqqChangePct: q.qqqChangePct ?? 0,
    basketValueB: q.basketValueB,
    asOf: new Date(q.asOf),
    latestSeriesPoint,
    movers,
    freshness,
    feeds,
    quoteState: q.quoteState ?? "live",
    isLive: q.isLive ?? true,
    isFrozen: q.isFrozen ?? false,
    pauseReason: q.pauseReason ?? null,
    marketSession,
  };
}

// ── Shared feed-status builder (session-aware) ──────

interface FeedFields {
  webSocketConnected: boolean;
  fallbackActive: boolean;
  pricingActive: boolean;
  basketState: string;
  marketSessionState?: string;
  sessionLabel?: string;
}

function buildFeeds(f: FeedFields): FeedStatus[] {
  const feeds: FeedStatus[] = [];

  if (f.marketSessionState && f.marketSessionState !== "regular_open") {
    feeds.push({
      name: "Market Data",
      status: "healthy",
      label: f.sessionLabel ?? f.marketSessionState,
    });
  } else if (f.webSocketConnected) {
    feeds.push({ name: "Market Data", status: "healthy" });
  } else if (f.fallbackActive) {
    feeds.push({ name: "Market Data", status: "degraded", label: "REST fallback" });
  } else {
    feeds.push({ name: "Market Data", status: "unhealthy" });
  }

  feeds.push(
    { name: "Pricing Engine", status: f.pricingActive ? "healthy" : "unhealthy" },
    {
      name: "Basket",
      status: f.basketState === "active" ? "healthy" : "degraded",
      label: f.basketState,
    },
  );

  if (f.fallbackActive) {
    feeds.push({ name: "REST Fallback", status: "degraded", label: "active" });
  }

  return feeds;
}

// ── Merge a slim delta into a full MarketSnapshot ───

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
  }
  const window = resolveSessionSeriesWindow(delta.marketSession, delta.asOf, delta.asOf);
  series = trimSeriesToWindow(series, window);

  return {
    nav: delta.nav,
    navChangePct: delta.navChangePct,
    marketPrice: delta.marketPrice,
    premiumDiscountPct: delta.premiumDiscountPct,
    qqq: delta.qqq,
    qqqChangePct: delta.qqqChangePct,
    basketValueB: delta.basketValueB,
    asOf: delta.asOf,
    series,
    movers: delta.movers,
    freshness: delta.freshness,
    feeds: delta.feeds,
    quoteState: delta.quoteState,
    isLive: delta.isLive,
    isFrozen: delta.isFrozen,
    pauseReason: delta.pauseReason,
    marketSession: delta.marketSession,
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
    // Preserve null so the UI can show "—" instead of a misleading +0.00%.
    // Backend returns null when previous-close is unavailable.
    changePct: h.changePct ?? null,
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
    upstream: h.upstream
      ? {
          webSocketConnected: h.upstream.webSocketConnected,
          fallbackActive: h.upstream.fallbackActive,
          lastUpstreamError: h.upstream.lastUpstreamError ?? null,
          lastUpstreamErrorCode: h.upstream.lastUpstreamErrorCode ?? null,
          lastUpstreamErrorAtUtc: h.upstream.lastUpstreamErrorAtUtc ?? null,
        }
      : undefined,
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
