import type {
  MarketSnapshot,
  ConstituentSnapshot,
  HistorySnapshot,
  SystemSnapshot,
  AppStatus,
  TimeSeriesPoint,
  Mover,
  Constituent,
  HistoryPoint,
  DistributionBucket,
  ServiceHealth,
  PipelineStatus,
  SystemEvent,
} from "./types";

// ── module-level state ──────────────────────────────

const START = Date.now();
let _nav = 453.2;
let _mkt = 453.05;

const _series: TimeSeriesPoint[] = [];
for (let i = 0; i < 60; i++) {
  const m = i * 5;
  const h = 9 + Math.floor((30 + m) / 60);
  const min = (30 + m) % 60;
  const n = +(453 + Math.sin(i / 8) * 2 + Math.sin(i / 3) * 0.5 + i * 0.02).toFixed(2);
  const mk = +(453 + Math.sin(i / 8) * 2 + Math.sin(i / 3.5) * 0.6 + i * 0.018 - 0.15).toFixed(2);
  _series.push({ time: `${h}:${min.toString().padStart(2, "0")}`, nav: n, market: mk });
}
_nav = _series[_series.length - 1].nav;
_mkt = _series[_series.length - 1].market;

let _mktTick = 0;

// ── helpers ─────────────────────────────────────────

function jitter(base: number, pct: number): number {
  return +(base * (1 + (Math.random() - 0.5) * pct)).toFixed(2);
}

// ── market ──────────────────────────────────────────

const MOVER_BASE = [
  { symbol: "NVDA", base: 2.87, weight: 7.81 },
  { symbol: "AAPL", base: 1.24, weight: 8.92 },
  { symbol: "AMZN", base: 0.45, weight: 5.47 },
  { symbol: "TSLA", base: -2.14, weight: 2.31 },
  { symbol: "META", base: -1.12, weight: 4.89 },
];

function getMovers(): Mover[] {
  return MOVER_BASE.map((m) => {
    const changePct = +(m.base + (Math.random() - 0.5) * 0.08).toFixed(2);
    return { symbol: m.symbol, changePct, impactBps: +(changePct * m.weight).toFixed(1) };
  });
}

export function getMarketSnapshot(): MarketSnapshot {
  _mktTick++;
  _nav += (Math.random() - 0.49) * 0.07;
  _mkt += (Math.random() - 0.49) * 0.08;

  if (_mktTick % 5 === 0 && _series.length < 78) {
    const m = _series.length * 5;
    _series.push({
      time: `${9 + Math.floor((30 + m) / 60)}:${((30 + m) % 60).toString().padStart(2, "0")}`,
      nav: +_nav.toFixed(2),
      market: +_mkt.toFixed(2),
    });
  } else if (_series.length > 0) {
    _series[_series.length - 1] = {
      ..._series[_series.length - 1],
      nav: +_nav.toFixed(2),
      market: +_mkt.toFixed(2),
    };
  }

  const navNow = +_nav.toFixed(2);
  const mktNow = +_mkt.toFixed(2);

  return {
    nav: navNow,
    navChangePct: +((navNow - 453.2) / 453.2 * 100).toFixed(3),
    marketPrice: mktNow,
    premiumDiscountPct: +((mktNow - navNow) / navNow * 100).toFixed(4),
    qqq: +(452.89 + Math.sin(_mktTick / 20) * 0.3).toFixed(2),
    basketValueB: 18.42,
    asOf: new Date(),
    series: _series.map((p) => ({ ...p })),
    movers: getMovers(),
    freshness: {
      lastNavCalcMs: 15 + Math.floor(Math.random() * 50),
      lastTickMs: 5 + Math.floor(Math.random() * 25),
      calcLatencyP99Ms: +(1.5 + Math.random() * 1.2).toFixed(1),
      avgTickIntervalMs: 100 + Math.floor(Math.random() * 50),
      staleSymbols: 0,
      totalSymbols: 101,
    },
    feeds: [
      { name: "Finnhub", status: "healthy" },
      { name: "Polygon", status: "healthy" },
      { name: "Backup feed", status: "unknown", label: "standby" },
    ],
  };
}

// ── constituents ────────────────────────────────────

const BASE_HOLDINGS: Constituent[] = [
  { symbol: "AAPL", name: "Apple Inc.", weight: 8.92, shares: 7_710_000, price: 213.07, changePct: 1.24 },
  { symbol: "MSFT", name: "Microsoft Corp.", weight: 8.43, shares: 3_320_000, price: 467.56, changePct: -0.31 },
  { symbol: "NVDA", name: "NVIDIA Corp.", weight: 7.81, shares: 1_615_000, price: 891.12, changePct: 2.87 },
  { symbol: "AMZN", name: "Amazon.com Inc.", weight: 5.47, shares: 4_478_000, price: 225.01, changePct: 0.45 },
  { symbol: "META", name: "Meta Platforms", weight: 4.89, shares: 1_441_000, price: 624.91, changePct: -1.12 },
  { symbol: "GOOG", name: "Alphabet Inc. A", weight: 3.21, shares: 3_262_000, price: 181.23, changePct: 0.65 },
  { symbol: "GOOGL", name: "Alphabet Inc. C", weight: 2.98, shares: 3_053_000, price: 179.84, changePct: 0.62 },
  { symbol: "AVGO", name: "Broadcom Inc.", weight: 2.74, shares: 289_000, price: 1748.92, changePct: -0.85 },
  { symbol: "COST", name: "Costco Wholesale", weight: 2.48, shares: 485_000, price: 942.31, changePct: 0.18 },
  { symbol: "TSLA", name: "Tesla Inc.", weight: 2.31, shares: 1_712_000, price: 248.42, changePct: -2.14 },
];

export function getConstituentSnapshot(): ConstituentSnapshot {
  return {
    asOfDate: "2026-03-28",
    totalCount: 101,
    holdings: BASE_HOLDINGS.map((h) => ({
      ...h,
      price: jitter(h.price, 0.002),
      changePct: +(h.changePct + (Math.random() - 0.5) * 0.04).toFixed(2),
    })),
    concentration: { top5: 35.52, top10: 49.24, top20: 68.91, hhi: 0.041 },
    quality: { stalePrices: 0, missingSymbols: 0, coverage: 101, totalSymbols: 101 },
    lastRefreshAt: Date.now(),
  };
}

// ── history (static) ────────────────────────────────

const HIST_SERIES: HistoryPoint[] = [];
for (let i = 0; i < 100; i++) {
  const day = Math.floor(i / 20) + 24;
  const slot = i % 20;
  const h = 9 + Math.floor((30 + slot * 20) / 60);
  const m = (30 + slot * 20) % 60;
  HIST_SERIES.push({
    time: `Mar ${day} ${h}:${m.toString().padStart(2, "0")}`,
    nav: +(450 + Math.sin(i / 12) * 3 + Math.cos(i / 30) * 5 + i * 0.04).toFixed(2),
    reference: +(449.8 + Math.sin(i / 12) * 3.1 + Math.cos(i / 30) * 4.8 + i * 0.04).toFixed(2),
  });
}

const HIST_DIST: DistributionBucket[] = Array.from({ length: 11 }, (_, i) => ({
  label: `${-5 + i}`,
  count: Math.max(1, Math.round(18 - Math.abs(i - 5) * 3 + Math.sin(i) * 2)),
}));

export function getHistorySnapshot(): HistorySnapshot {
  return {
    series: HIST_SERIES,
    trackingError: { te1dPct: 0.012, te5dPct: 0.018, maxDeviationPct: 0.041, meanAbsPdBps: 0.8, correlation: 0.99997 },
    distribution: HIST_DIST,
    diagnostics: { snapshots: 1847, gaps: 0, maxLatencyMs: 8.4, avgLatencyMs: 1.9, completenessPct: 100 },
  };
}

// ── system ──────────────────────────────────────────

const BASE_SERVICES: ServiceHealth[] = [
  { name: "API Server", status: "healthy", latencyMs: 2.1, detail: "hqqq-api :5015" },
  { name: "Kafka", status: "healthy", latencyMs: 4.8, detail: "KRaft cluster" },
  { name: "Redis", status: "healthy", latencyMs: 0.3, detail: "7.4.8-alpine" },
  { name: "TimescaleDB", status: "healthy", latencyMs: 1.2, detail: "2.17.2-pg16" },
  { name: "Market Feed", status: "healthy", latencyMs: 18, detail: "Finnhub WS" },
];

const BASE_PIPELINES: PipelineStatus[] = [
  { name: "Tick Ingestion", status: "healthy", throughputPerSec: 824, lagMs: 0, lastProcessedAgo: "< 1s", errorsLastHour: 0 },
  { name: "iNAV Calculation", status: "healthy", throughputPerSec: 142, lagMs: 2, lastProcessedAgo: "< 1s", errorsLastHour: 0 },
  { name: "Quote Publishing", status: "healthy", throughputPerSec: 142, lagMs: 1, lastProcessedAgo: "< 1s", errorsLastHour: 0 },
  { name: "History Writer", status: "healthy", throughputPerSec: 142, lagMs: 48, lastProcessedAgo: "< 1s", errorsLastHour: 0 },
];

const BASE_EVENTS: SystemEvent[] = [
  { time: "14:28:03", message: "Basket composition refreshed (101 symbols)", level: "info" },
  { time: "14:15:00", message: "Health check passed — all dependencies healthy", level: "info" },
  { time: "10:02:41", message: "API server started on :5015", level: "success" },
  { time: "10:02:38", message: "Connected to Kafka cluster (hqqq-dev-kafka-cluster-01)", level: "info" },
];

export function getSystemSnapshot(): SystemSnapshot {
  const elapsed = Math.floor((Date.now() - START) / 1000);
  return {
    services: BASE_SERVICES.map((s) => ({
      ...s,
      latencyMs: +(s.latencyMs * (0.85 + Math.random() * 0.3)).toFixed(1),
    })),
    runtime: {
      uptimeSeconds: 15791 + elapsed,
      memoryMb: Math.round(148 + (Math.random() - 0.5) * 8),
      memoryMaxMb: 512,
      cpuPct: +(3 + Math.random()).toFixed(1),
      gcCollections: 847 + Math.floor(elapsed / 10),
      activeConnections: 5 + Math.floor(Math.random() * 4),
      requestsPerSec: Math.round(140 + (Math.random() - 0.5) * 20),
      avgResponseMs: +(1.2 + Math.random() * 0.6).toFixed(1),
      errorRatePct: 0,
    },
    pipelines: BASE_PIPELINES.map((p) => ({
      ...p,
      throughputPerSec: Math.round(p.throughputPerSec * (0.9 + Math.random() * 0.2)),
      lagMs: Math.max(0, Math.round(p.lagMs + (Math.random() - 0.5) * p.lagMs * 0.4)),
    })),
    events: BASE_EVENTS,
  };
}

// ── global status ───────────────────────────────────

export function getAppStatus(): AppStatus {
  return {
    mode: "mock",
    updateIntervalMs: 1000,
    lastUpdate: new Date(),
    symbolCount: 101,
    overallHealth: "healthy",
  };
}
