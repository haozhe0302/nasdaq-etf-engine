// ── shared primitives ───────────────────────────────

export type HealthStatus = "healthy" | "degraded" | "unhealthy" | "unknown";

export type ConnectionState = "connecting" | "live" | "error" | "stale";

export interface LiveDataResult<T> {
  data: T;
  connectionState: ConnectionState;
  error?: string;
}

// ── market ──────────────────────────────────────────

export interface MarketSnapshot {
  nav: number;
  navChangePct: number;
  marketPrice: number;
  premiumDiscountPct: number;
  qqq: number;
  basketValueB: number;
  asOf: Date;
  series: TimeSeriesPoint[];
  movers: Mover[];
  freshness: FreshnessMetrics;
  feeds: FeedStatus[];
  quoteState: string;
  isLive: boolean;
  isFrozen: boolean;
  pauseReason: string | null;
  marketSession: MarketSessionInfo;
}

export interface MarketSessionInfo {
  state: string;
  label: string;
  isRegularSessionOpen: boolean;
  isTradingDay: boolean;
  nextOpenUtc: string | null;
}

export interface TimeSeriesPoint {
  time: string;
  nav: number;
  market: number;
}

export interface Mover {
  symbol: string;
  changePct: number;
  impactBps: number;
}

export interface FreshnessMetrics {
  lastNavCalcMs: number;
  lastTickMs: number;
  networkLatencyMs: number;
  avgTickIntervalMs: number;
  staleSymbols: number;
  totalSymbols: number;
}

export interface FeedStatus {
  name: string;
  status: HealthStatus;
  label?: string;
}

// ── constituents ────────────────────────────────────

export interface ConstituentSnapshot {
  asOfDate: string;
  totalCount: number;
  holdings: Constituent[];
  concentration: ConcentrationMetrics;
  quality: DataQualityMetrics;
  lastRefreshAt: number;
}

export interface Constituent {
  symbol: string;
  name: string;
  weight: number;
  shares: number;
  price: number;
  changePct: number;
}

export interface ConcentrationMetrics {
  top5: number;
  top10: number;
  top20: number;
  hhi: number;
}

export interface DataQualityMetrics {
  stalePrices: number;
  missingSymbols: number;
  coverage: number;
  totalSymbols: number;
}

// ── history ─────────────────────────────────────────

export interface HistorySnapshot {
  range: string;
  startDate: string;
  endDate: string;
  pointCount: number;
  totalPoints: number;
  isPartial: boolean;
  series: HistoryPoint[];
  trackingError: TrackingErrorMetrics;
  distribution: DistributionBucket[];
  diagnostics: HistoryDiagnostics;
}

export interface HistoryPoint {
  time: string;
  nav: number;
  reference: number;
}

export interface TrackingErrorMetrics {
  rmseBps: number;
  maxAbsBasisBps: number;
  avgAbsBasisBps: number;
  maxDeviationPct: number;
  correlation: number;
}

export interface DistributionBucket {
  label: string;
  count: number;
}

export interface HistoryDiagnostics {
  snapshots: number;
  gaps: number;
  completenessPct: number;
  daysLoaded: number;
}

// ── system ──────────────────────────────────────────

export interface SystemSnapshot {
  services: ServiceHealth[];
  runtime: RuntimeMetrics;
  pipelines: PipelineStatus[];
  events: SystemEvent[];
  metrics?: RuntimeMetricsData;
}

export interface RuntimeMetricsData {
  snapshotAgeMs: number;
  pricedWeightCoverage: number;
  staleSymbolRatio: number;
  tickToQuoteMs: LatencyStatsData;
  quoteBroadcastMs: LatencyStatsData;
  lastFailoverRecoverySeconds: number | null;
  lastActivationJumpBps: number | null;
  totalTicksIngested: number;
  totalQuoteBroadcasts: number;
  totalFallbackActivations: number;
  totalBasketActivations: number;
}

export interface LatencyStatsData {
  p50: number;
  p95: number;
  p99: number;
  sampleCount: number;
}

export interface ServiceHealth {
  name: string;
  status: HealthStatus;
  latencyMs: number;
  detail: string;
}

export interface RuntimeMetrics {
  uptimeSeconds: number;
  memoryMb: number;
  memoryMaxMb: number;
  cpuPct: number;
  gcCollections: number;
  activeConnections: number;
  requestsPerSec: number;
  avgResponseMs: number;
  errorRatePct: number;
}

export interface PipelineStatus {
  name: string;
  status: HealthStatus;
  throughputPerSec: number;
  lagMs: number;
  lastProcessedAgo: string;
  errorsLastHour: number;
}

export interface SystemEvent {
  time: string;
  message: string;
  level: "info" | "success" | "warning" | "error";
}

// ── global status bar ───────────────────────────────

export interface AppStatus {
  mode: "mock" | "live";
  updateIntervalMs: number;
  lastUpdate: Date;
  symbolCount: number;
  overallHealth: HealthStatus;
}
