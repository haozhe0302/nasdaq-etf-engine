import type {
  MarketSnapshot,
  ConstituentSnapshot,
  SystemSnapshot,
  TimeSeriesPoint,
  Mover,
  FreshnessMetrics,
  FeedStatus,
  Constituent,
  ConcentrationMetrics,
  DataQualityMetrics,
  ServiceHealth,
  HealthStatus,
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

interface BSystemHealth {
  serviceName: string;
  status: string;
  checkedAtUtc: string;
  version: string;
  dependencies: {
    name: string;
    status: string;
    lastCheckedAtUtc: string;
    details: string | null;
  }[];
}

// ── Helpers ──────────────────────────────────────────

function formatTime(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  const h = d.getHours();
  const m = d.getMinutes().toString().padStart(2, "0");
  return `${h}:${m}`;
}

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
  const lastTickMs = q.freshness.lastTickUtc
    ? now - new Date(q.freshness.lastTickUtc).getTime()
    : 0;

  const series: TimeSeriesPoint[] = q.series.map((p) => ({
    time: formatTime(p.time),
    nav: p.nav,
    market: p.market,
  }));

  const movers: Mover[] = q.movers.map((m) => ({
    symbol: m.symbol,
    changePct: m.changePct,
    impactBps: m.impact,
  }));

  const freshness: FreshnessMetrics = {
    lastNavCalcMs: Math.max(0, Math.round(now - asOfMs)),
    lastTickMs: Math.max(0, Math.round(lastTickMs)),
    calcLatencyP99Ms: 0,
    avgTickIntervalMs: 0,
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

// ── Constituents → ConstituentSnapshot ──────────────

export function adaptConstituents(raw: unknown): ConstituentSnapshot {
  const c = raw as BConstituentSnapshot;

  const holdings: Constituent[] = c.holdings.map((h) => ({
    symbol: h.symbol,
    name: h.name,
    sector: h.sector,
    weight: h.weight,
    shares: h.shares,
    price: h.price ?? 0,
    changePct: h.changePct ?? 0,
  }));

  const concentration: ConcentrationMetrics = {
    top5: c.concentration.top5Pct,
    top10: c.concentration.top10Pct,
    top20: 0,
    sectors: c.concentration.sectorCount,
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

  return {
    services,
    runtime: {
      uptimeSeconds: 0,
      memoryMb: 0,
      memoryMaxMb: 0,
      cpuPct: 0,
      gcCollections: 0,
      activeConnections: 0,
      requestsPerSec: 0,
      avgResponseMs: 0,
      errorRatePct: 0,
    },
    pipelines: [],
    events: [],
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
