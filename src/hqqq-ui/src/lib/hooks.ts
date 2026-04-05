import { useState, useEffect, useRef, useCallback } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";
import {
  fetchQuote,
  fetchConstituents,
  fetchSystemHealth,
  fetchHistory,
  createMarketHubConnection,
} from "./api";
import {
  adaptQuote,
  adaptQuoteDelta,
  mergeQuoteDelta,
  adaptConstituents,
  adaptSystemHealth,
  adaptHistory,
  deriveSymbolCount,
  toHealthStatus,
} from "./adapters";
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
  pipelines: [],
  events: [],
};

// ── Market (full REST snapshot + slim SignalR deltas) ─────

export function useMarketData(): LiveDataResult<MarketSnapshot> {
  const [data, setData] = useState<MarketSnapshot>(EMPTY_MARKET);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();
  const hubRef = useRef<HubConnection | null>(null);
  const retryTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const latencyEmaRef = useRef<number | null>(null);
  const latencySampleAtRef = useRef<number | null>(null);

  useEffect(() => {
    let cancelled = false;

    const updateLatencyEma = (asOf: Date): number => {
      const receivedAt = Date.now();
      const sampleMs = Math.max(0, receivedAt - asOf.getTime());

      const previousSampleAt = latencySampleAtRef.current;
      const dtMs = previousSampleAt === null ? 0 : Math.max(0, receivedAt - previousSampleAt);
      const tauMs = 1_000;
      const alpha = dtMs <= 0 ? 1 : 1 - Math.exp(-dtMs / tauMs);
      const previousEma = latencyEmaRef.current;
      const ema = previousEma === null ? sampleMs : previousEma + alpha * (sampleMs - previousEma);

      latencySampleAtRef.current = receivedAt;
      latencyEmaRef.current = ema;
      return Number.isFinite(ema) ? Math.round(ema) : 0;
    };

    const applyFullSnapshot = (raw: unknown) => {
      if (cancelled) return;
      try {
        const snapshot = adaptQuote(raw);
        const networkLatencyMs = updateLatencyEma(snapshot.asOf);
        setData({
          ...snapshot,
          freshness: { ...snapshot.freshness, networkLatencyMs },
        });
        recordUpdate("market");
        setConnectionState("live");
        setError(undefined);
      } catch (e) {
        console.error("Failed to process full snapshot", e);
      }
    };

    const onDelta = (raw: unknown) => {
      if (cancelled) return;
      try {
        const delta = adaptQuoteDelta(raw);
        const networkLatencyMs = updateLatencyEma(delta.asOf);
        setData((prev) => {
          const merged = mergeQuoteDelta(prev, delta);
          return {
            ...merged,
            freshness: { ...merged.freshness, networkLatencyMs },
          };
        });
        recordUpdate("market");
        setConnectionState("live");
        setError(undefined);
      } catch (e) {
        console.error("Failed to process QuoteUpdate delta", e);
      }
    };

    const stopRetryTimer = () => {
      if (retryTimerRef.current) {
        clearInterval(retryTimerRef.current);
        retryTimerRef.current = null;
      }
    };

    const setupHub = (hub: HubConnection) => {
      hub.on("QuoteUpdate", onDelta);

      hub.onreconnecting(() => {
        if (!cancelled) {
          setConnectionState("stale");
          setError("Reconnecting to market feed\u2026");
        }
      });

      hub.onreconnected(() => {
        if (!cancelled) {
          fetchQuote()
            .then((raw) => { if (!cancelled) applyFullSnapshot(raw); })
            .catch(() => {});
          setConnectionState("live");
          setError(undefined);
        }
      });

      hub.onclose(() => {
        if (cancelled) return;
        setConnectionState("error");
        setError("Market feed disconnected \u2014 retrying\u2026");
        startRetryLoop();
      });
    };

    const startRetryLoop = () => {
      stopRetryTimer();
      retryTimerRef.current = setInterval(async () => {
        if (cancelled) { stopRetryTimer(); return; }
        try {
          const raw = await fetchQuote();
          if (cancelled) return;
          applyFullSnapshot(raw);

          stopRetryTimer();
          const newHub = createMarketHubConnection();
          hubRef.current = newHub;
          setupHub(newHub);
          await newHub.start();
        } catch {
          // backend still down, keep retrying
        }
      }, 5_000);
    };

    fetchQuote()
      .then((raw) => {
        if (cancelled) return;
        applyFullSnapshot(raw);
      })
      .catch((err) => {
        if (cancelled) return;
        setConnectionState("error");
        setError(err.message);
        startRetryLoop();
      });

    const hub = createMarketHubConnection();
    hubRef.current = hub;
    setupHub(hub);

    hub.start().catch((err) => {
      if (!cancelled) {
        setConnectionState("error");
        setError(err.message);
      }
    });

    return () => {
      cancelled = true;
      stopRetryTimer();
      unregisterFeed("market");
      if (hubRef.current?.state !== HubConnectionState.Disconnected) {
        hubRef.current?.stop();
      }
    };
  }, []);

  return { data, connectionState, error };
}

// ── Constituents (poll every 5 s) ───────────────────

export function useConstituentData(): LiveDataResult<ConstituentSnapshot> {
  const [data, setData] = useState<ConstituentSnapshot>(EMPTY_CONSTITUENTS);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();

  const poll = useCallback(async () => {
    try {
      const raw = await fetchConstituents();
      setData(adaptConstituents(raw));
      recordUpdate("constituents");
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
    const id = setInterval(poll, 5_000);
    return () => {
      clearInterval(id);
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
    const healthId = setInterval(poll, 5_000);

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
