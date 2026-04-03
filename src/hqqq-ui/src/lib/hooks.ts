import { useState, useEffect, useRef, useCallback } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";
import {
  fetchQuote,
  fetchConstituents,
  fetchSystemHealth,
  createMarketHubConnection,
} from "./api";
import {
  adaptQuote,
  adaptConstituents,
  adaptSystemHealth,
  deriveSymbolCount,
  toHealthStatus,
} from "./adapters";
import { getHistorySnapshot } from "./mock";
import { recordUpdate, unregisterFeed, getMinIntervalMs } from "./updateTracker";
import type {
  MarketSnapshot,
  ConstituentSnapshot,
  SystemSnapshot,
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
    calcLatencyP99Ms: 0,
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
  pipelines: [],
  events: [],
};

// ── Market (initial REST fetch + SignalR real-time push) ─────

export function useMarketData(): LiveDataResult<MarketSnapshot> {
  const [data, setData] = useState<MarketSnapshot>(EMPTY_MARKET);
  const [connectionState, setConnectionState] =
    useState<ConnectionState>("connecting");
  const [error, setError] = useState<string>();
  const hubRef = useRef<HubConnection | null>(null);
  const retryTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    let cancelled = false;

    const onQuoteUpdate = (raw: unknown) => {
      if (cancelled) return;
      try {
        setData(adaptQuote(raw));
        recordUpdate("market");
        setConnectionState("live");
        setError(undefined);
      } catch (e) {
        console.error("Failed to process QuoteUpdate", e);
      }
    };

    const stopRetryTimer = () => {
      if (retryTimerRef.current) {
        clearInterval(retryTimerRef.current);
        retryTimerRef.current = null;
      }
    };

    const setupHub = (hub: HubConnection) => {
      hub.on("QuoteUpdate", onQuoteUpdate);

      hub.onreconnecting(() => {
        if (!cancelled) {
          setConnectionState("stale");
          setError("Reconnecting to market feed\u2026");
        }
      });

      hub.onreconnected(() => {
        if (!cancelled) {
          fetchQuote()
            .then((raw) => { if (!cancelled) onQuoteUpdate(raw); })
            .catch(() => {});
          setConnectionState("live");
          setError(undefined);
        }
      });

      hub.onclose(() => {
        if (cancelled) return;
        setConnectionState("error");
        setError("Market feed disconnected — retrying\u2026");
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
          onQuoteUpdate(raw);

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
        setData(adaptQuote(raw));
        recordUpdate("market");
        setConnectionState("live");
        setError(undefined);
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

// ── System (poll every 5 s) ─────────────────────────

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
    const id = setInterval(poll, 5_000);
    return () => {
      clearInterval(id);
      unregisterFeed("system");
    };
  }, [poll]);

  return { data, connectionState, error };
}

// ── History (static/mock — unchanged) ───────────────

export function useHistoryData() {
  return useState(getHistorySnapshot)[0];
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
