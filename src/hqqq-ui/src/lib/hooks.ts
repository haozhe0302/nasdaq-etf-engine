import { useState, useEffect, useRef, useCallback } from "react";
import {
  fetchQuote,
  fetchConstituents,
  fetchSystemHealth,
  fetchHistory,
  createMarketHubConnection,
} from "./api";
import {
  adaptConstituents,
  adaptSystemHealth,
  adaptHistory,
  deriveSymbolCount,
  toHealthStatus,
} from "./adapters";
import { createMarketController } from "./marketController";
import { recordUpdate, unregisterFeed, getMinIntervalMs } from "./updateTracker";
import type {
  MarketSnapshot,
  ConstituentSnapshot,
  SystemSnapshot,
  HistorySnapshot,
  TimeSeriesPoint,
  AppStatus,
  ConnectionState,
  LiveDataResult,
} from "./types";

// ── Default empty snapshots (safe to render while loading) ───

const EMPTY_MARKET: MarketSnapshot = {
  nav: 0,
  navChangePct: 0,
  marketPrice: 0,
  premiumDiscountPct: 0,
  qqq: 0,
  qqqChangePct: 0,
  basketValueB: 0,
  asOf: new Date(),
  series: [],
  movers: [],
  freshness: {
    asOfUtc: null,
    lastTickUtc: null,
    lastNavCalcMs: 0,
    lastTickMs: 0,
    networkLatencyMs: 0,
    avgTickIntervalMs: 0,
    staleSymbols: 0,
    totalSymbols: 0,
  },
  feeds: [],
  quoteState: "initializing",
  isLive: false,
  isFrozen: false,
  pauseReason: null,
  marketSession: {
    state: "unknown",
    label: "",
    isRegularSessionOpen: false,
    isTradingDay: false,
    nextOpenUtc: null,
  },
};

const EMPTY_CONSTITUENTS: ConstituentSnapshot = {
  asOfDate: "",
  totalCount: 0,
  holdings: [],
  concentration: { top5: 0, top10: 0, top20: 0, hhi: 0 },
  quality: { stalePrices: 0, missingSymbols: 0, coverage: 0, totalSymbols: 0 },
  lastRefreshAt: 0,
};

const EMPTY_SYSTEM: SystemSnapshot = {
  services: [],
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
  metrics: undefined,
  upstream: undefined,
  pipelines: [],
  events: [],
};

// ── Shared health-probe RTT tracker (EMA) ────────────────────

const HEALTH_RTT_EMA_ALPHA = 0.3;
let healthRttEmaMs = 0;

function recordHealthProbeRtt(startMs: number): void {
  const rtt = performance.now() - startMs;
  if (!Number.isFinite(rtt) || rtt < 0) return;
  const prev = healthRttEmaMs;
  healthRttEmaMs = prev === 0 ? rtt : prev + HEALTH_RTT_EMA_ALPHA * (rtt - prev);
}

function getHealthProbeRttMs(): number {
  return Math.max(0, Math.round(healthRttEmaMs));
}

// ── Market (full REST snapshot + slim SignalR deltas) ─────

export function useMarketData(): LiveDataResult<MarketSnapshot> {
  const [data, setData] = useState<MarketSnapshot>(EMPTY_MARKET);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();

  useEffect(() => {
    let cancelled = false;

    const controller = createMarketController(
      {
        fetchSnapshot: fetchQuote,
        createHub: createMarketHubConnection,
      },
      {
        onSnapshot: (snapshot) => {
          if (cancelled) return;
          const networkLatencyMs = getHealthProbeRttMs();
          setData({
            ...snapshot,
            freshness: { ...snapshot.freshness, networkLatencyMs },
          });
          recordUpdate("market");
        },
        onState: (state, err) => {
          if (cancelled) return;
          setConnectionState(state);
          setError(err);
        },
      },
    );

    void controller.start();

    return () => {
      cancelled = true;
      void controller.stop();
      unregisterFeed("market");
    };
  }, []);

  return { data, connectionState, error };
}

// ── Constituents (poll every 5 s) ───────────────────
//
// Polls /api/constituents on a fixed 5s interval regardless of previous
// success or failure. On failure we keep the last successful snapshot
// displayed and only flip the connection state to "stale"/"error".

const CONSTITUENTS_POLL_INTERVAL_MS = 5_000;

export function useConstituentData(): LiveDataResult<ConstituentSnapshot> {
  const [data, setData] = useState<ConstituentSnapshot>(EMPTY_CONSTITUENTS);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const poll = useCallback(async () => {
    try {
      const raw = await fetchConstituents();
      // adaptConstituents stamps lastRefreshAt = Date.now() on every success
      setData(adaptConstituents(raw));
      recordUpdate("constituents");
      setConnectionState("live");
      setError(undefined);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      console.warn("[constituents] poll failed, keeping last snapshot", err);
      setConnectionState((prev) => (prev === "live" ? "stale" : "error"));
      setError(msg);
    }
  }, []);

  useEffect(() => {
    void poll();
    // Guard against accidental duplicate intervals (e.g. on fast remounts).
    if (timerRef.current !== null) {
      clearInterval(timerRef.current);
    }
    timerRef.current = setInterval(() => { void poll(); }, CONSTITUENTS_POLL_INTERVAL_MS);
    return () => {
      if (timerRef.current !== null) {
        clearInterval(timerRef.current);
        timerRef.current = null;
      }
      unregisterFeed("constituents");
    };
  }, [poll]);

  return { data, connectionState, error };
}

// ── System (poll every 3 s) ─────────────────────────

export function useSystemData(): LiveDataResult<SystemSnapshot> {
  const [data, setData] = useState<SystemSnapshot>(EMPTY_SYSTEM);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();

  const poll = useCallback(async () => {
    try {
      const start = performance.now();
      const raw = await fetchSystemHealth();
      recordHealthProbeRtt(start);
      setData(adaptSystemHealth(raw));
      recordUpdate("system");
      setConnectionState("live");
      setError(undefined);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setConnectionState((prev) => (prev === "live" ? "stale" : "error"));
      setError(msg);
    }
  }, []);

  useEffect(() => {
    poll();
    const id = setInterval(poll, 3_000);
    return () => {
      clearInterval(id);
      unregisterFeed("system");
    };
  }, [poll]);

  return { data, connectionState, error };
}

// ── History (real backend API with range selection) ──

const EMPTY_HISTORY: HistorySnapshot = {
  range: "1D",
  startDate: "",
  endDate: "",
  pointCount: 0,
  totalPoints: 0,
  isPartial: true,
  series: [],
  trackingError: { rmseBps: 0, maxAbsBasisBps: 0, avgAbsBasisBps: 0, maxDeviationPct: 0, correlation: 0 },
  distribution: [],
  diagnostics: { snapshots: 0, gaps: 0, completenessPct: 0, daysLoaded: 0 },
};

export function useHistoryData(range: string): LiveDataResult<HistorySnapshot> {
  const [data, setData] = useState<HistorySnapshot>(EMPTY_HISTORY);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();

  const poll = useCallback(async () => {
    try {
      const raw = await fetchHistory(range);
      setData(adaptHistory(raw));
      recordUpdate("history");
      setConnectionState("live");
      setError(undefined);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setConnectionState((prev) => (prev === "live" ? "stale" : "error"));
      setError(msg);
    }
  }, [range]);

  useEffect(() => {
    setConnectionState("connecting");
    poll();
    const id = setInterval(poll, 30_000);
    return () => {
      clearInterval(id);
      unregisterFeed("history");
    };
  }, [poll]);

  return { data, connectionState, error };
}

// ── App status (derived from live health endpoint) ──

export function useAppStatus(): AppStatus {
  const [status, setStatus] = useState<AppStatus>({
    mode: "live",
    updateIntervalMs: 0,
    lastUpdate: new Date(),
    symbolCount: 0,
    overallHealth: "unknown",
  });

  const poll = useCallback(async () => {
    try {
      const start = performance.now();
      const raw = await fetchSystemHealth();
      recordHealthProbeRtt(start);
      const health = toHealthStatus(
        (raw as { status: string }).status,
      );
      const symbolCount = deriveSymbolCount(raw);
      setStatus((prev) => ({
        ...prev,
        mode: "live",
        lastUpdate: new Date(),
        symbolCount,
        overallHealth: health,
        updateIntervalMs: getMinIntervalMs(),
      }));
    } catch {
      setStatus((prev) => ({
        ...prev,
        lastUpdate: new Date(),
        overallHealth: "unhealthy",
      }));
    }
  }, []);

  useEffect(() => {
    poll();
    const healthId = setInterval(poll, 3_000);

    const tickId = setInterval(() => {
      setStatus((prev) => ({ ...prev, updateIntervalMs: getMinIntervalMs() }));
    }, 1_000);

    return () => {
      clearInterval(healthId);
      clearInterval(tickId);
    };
  }, [poll]);

  return status;
}

// ── Local 1s clock (for age displays) ───────────────
//
// Returns Date.now() refreshed on a fixed interval so components can render
// "age since X" values that advance without waiting for the next data push.

export function useNowTick(intervalMs = 1_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}

// ── Last-session chart fallback (after-hours) ────────
//
// When `enabled` (the /market chart is in after-hours mode), fetch the most
// recent completed regular session from /api/history?range=1D and expose it
// as a raw TimeSeriesPoint[] (ISO timestamps preserved for the ECharts time
// axis). Refetches every 5 minutes to absorb late prints. No-op while
// disabled, so the regular session never spins up a duplicate timer.

interface RawHistorySeries {
  series?: { time: string; nav: number; marketPrice: number }[];
}

export function useLastSessionFallback(enabled: boolean): TimeSeriesPoint[] {
  const [series, setSeries] = useState<TimeSeriesPoint[]>([]);

  useEffect(() => {
    if (!enabled) {
      setSeries([]);
      return;
    }

    let cancelled = false;
    const load = async () => {
      try {
        const raw = (await fetchHistory("1D")) as RawHistorySeries;
        if (cancelled) return;
        setSeries(
          (raw.series ?? []).map((p) => ({
            time: p.time,
            nav: p.nav,
            market: p.marketPrice,
          })),
        );
      } catch {
        // Keep the last successfully loaded session on transient failures.
      }
    };

    void load();
    const id = setInterval(() => { void load(); }, 5 * 60_000);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [enabled]);

  return series;
}

export function useEstClock(): string {
  const fmt = () => {
    const p = new Intl.DateTimeFormat("en-CA", {
      timeZone: "America/New_York",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false,
    }).formatToParts(new Date());
    const g = (t: string) => p.find((x) => x.type === t)!.value;
    return `${g("year")}/${g("month")}/${g("day")} ${g("hour")}:${g("minute")}:${g("second")}`;
  };

  const [clock, setClock] = useState(fmt);

  useEffect(() => {
    const id = setInterval(() => setClock(fmt()), 1_000);
    return () => clearInterval(id);
  }, []);

  return clock;
}
