import { useState, useEffect, useRef, useCallback } from "react";
import {
  fetchQuote,
  fetchConstituents,
  fetchSystemHealth,
  fetchHistory,
  createMarketHubConnection,
  pingLiveness,
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

// ── Shared network-latency probe (EMA) ───────────────────────
//
// "Network Latency" must reflect the pure frontend↔gateway round-trip, NOT
// server-side work. Previously it timed GET /api/system/health, but that
// endpoint fans out to every downstream service plus the database probe (with
// a per-call timeout), so when the DB was down the measured value was pinned
// above the timeout and no longer represented network latency at all.
//
// Instead we run a dedicated probe against /healthz/live — the gateway's
// "live"-tagged self check that never touches the DB — on its own interval and
// keep an EMA of the round-trip. The probe is reference-counted across hooks
// so multiple consumers share a single timer.

const NETWORK_RTT_EMA_ALPHA = 0.3;
const LATENCY_PROBE_INTERVAL_MS = 3_000;

let networkRttEmaMs = 0;
let probeSubscribers = 0;
let probeTimer: ReturnType<typeof setInterval> | null = null;
let probeInFlight = false;

function recordNetworkRtt(startMs: number): void {
  const rtt = performance.now() - startMs;
  if (!Number.isFinite(rtt) || rtt < 0) return;
  const prev = networkRttEmaMs;
  networkRttEmaMs = prev === 0 ? rtt : prev + NETWORK_RTT_EMA_ALPHA * (rtt - prev);
}

function getNetworkLatencyMs(): number {
  return Math.max(0, Math.round(networkRttEmaMs));
}

async function runLatencyProbe(): Promise<void> {
  if (probeInFlight) return;
  probeInFlight = true;
  const start = performance.now();
  try {
    await pingLiveness();
    recordNetworkRtt(start);
  } catch {
    // Gateway unreachable / aborted: a failed fetch carries no meaningful
    // timing, so we skip the sample and keep the last good EMA rather than
    // polluting it. Connection state is surfaced separately by each feed.
  } finally {
    probeInFlight = false;
  }
}

function useNetworkLatencyProbe(): void {
  useEffect(() => {
    probeSubscribers += 1;
    if (probeTimer === null) {
      void runLatencyProbe();
      probeTimer = setInterval(() => void runLatencyProbe(), LATENCY_PROBE_INTERVAL_MS);
    }
    return () => {
      probeSubscribers = Math.max(0, probeSubscribers - 1);
      if (probeSubscribers === 0 && probeTimer !== null) {
        clearInterval(probeTimer);
        probeTimer = null;
      }
    };
  }, []);
}

// ── Market (full REST snapshot + slim SignalR deltas) ─────

export function useMarketData(): LiveDataResult<MarketSnapshot> {
  const [data, setData] = useState<MarketSnapshot>(EMPTY_MARKET);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();

  useNetworkLatencyProbe();

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
          const networkLatencyMs = getNetworkLatencyMs();
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
      const raw = await fetchSystemHealth();
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
      const raw = await fetchSystemHealth();
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
