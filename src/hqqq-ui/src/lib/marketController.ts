import { HubConnectionState } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";
import { adaptQuote, adaptQuoteDelta, mergeQuoteDelta } from "./adapters";
import type { ConnectionState, MarketSnapshot } from "./types";

/**
 * REST fallback polling interval used when SignalR is NOT delivering deltas.
 * Matches /api/system/health cadence so the user still sees fresh data.
 */
export const FALLBACK_POLL_INTERVAL_MS = 5_000;

/**
 * Safety REST resync interval used while SignalR IS live. Guards against
 * the chart freezing if a SignalR delta is silently dropped.
 */
export const SAFETY_RESYNC_INTERVAL_MS = 30_000;

/** Subset of HubConnection we actually need; lets tests inject fakes. */
export type MarketHub = Pick<
  HubConnection,
  "on" | "onreconnecting" | "onreconnected" | "onclose" | "start" | "stop"
> & { readonly state: HubConnectionState };

export interface MarketControllerCallbacks {
  /** Called for both full snapshots and merged deltas. */
  onSnapshot: (snapshot: MarketSnapshot) => void;
  /** Called on every transport / connection state change. */
  onState: (state: ConnectionState, error?: string) => void;
}

export interface MarketControllerDeps {
  fetchSnapshot: () => Promise<unknown>;
  createHub: () => MarketHub;
  fallbackIntervalMs?: number;
  safetyIntervalMs?: number;
  logger?: Pick<Console, "info" | "warn" | "error">;
  /** Indirection for tests so they can stub setInterval/clearInterval. */
  scheduler?: Scheduler;
}

export interface Scheduler {
  setInterval: (handler: () => void, ms: number) => unknown;
  clearInterval: (handle: unknown) => void;
}

const DEFAULT_SCHEDULER: Scheduler = {
  setInterval: (handler, ms) => setInterval(handler, ms),
  clearInterval: (handle) => clearInterval(handle as ReturnType<typeof setInterval>),
};

export interface MarketController {
  start: () => Promise<void>;
  stop: () => Promise<void>;
}

/**
 * Encapsulates the SignalR-with-REST-fallback state machine for the /market
 * page. Extracted into a plain function so it can be unit-tested without
 * React.
 *
 * State machine summary
 * ---------------------
 * mount -> "connecting"
 *   |
 *   v
 * fetchSnapshot() once
 *   |
 *   v
 * createHub() + hub.start()
 *   |---- success ----> "live", start safety timer (30s)
 *   |                     |
 *   |                     +-- on QuoteUpdate ---> stays "live"
 *   |                     +-- onreconnecting --> "stale", start fallback (5s)
 *   |                     +-- onreconnected ---> "live", refetch snapshot
 *   |                     +-- onclose ---------> "stale", start fallback (5s)
 *   |
 *   '---- failure ----> "stale" (or "error" if no snapshot), start fallback (5s)
 *                         |
 *                         +-- every 5s: fetchSnapshot + try hub.start()
 *                         |        on success -> "live", swap to safety timer
 *
 * stop() always tears down both timers and the hub.
 */
export function createMarketController(
  deps: MarketControllerDeps,
  callbacks: MarketControllerCallbacks,
): MarketController {
  const {
    fetchSnapshot,
    createHub,
    fallbackIntervalMs = FALLBACK_POLL_INTERVAL_MS,
    safetyIntervalMs = SAFETY_RESYNC_INTERVAL_MS,
    logger = console,
    scheduler = DEFAULT_SCHEDULER,
  } = deps;

  let disposed = false;
  let hub: MarketHub | null = null;
  let lastSnapshot: MarketSnapshot | null = null;
  let fallbackHandle: unknown = null;
  let safetyHandle: unknown = null;
  let restartingHub = false;
  let currentState: ConnectionState = "connecting";

  const setState = (state: ConnectionState, error?: string) => {
    currentState = state;
    callbacks.onState(state, error);
  };

  const stopFallback = () => {
    if (fallbackHandle !== null) {
      scheduler.clearInterval(fallbackHandle);
      fallbackHandle = null;
    }
  };

  const stopSafety = () => {
    if (safetyHandle !== null) {
      scheduler.clearInterval(safetyHandle);
      safetyHandle = null;
    }
  };

  const ensureFallback = () => {
    if (disposed || fallbackHandle !== null) return;
    fallbackHandle = scheduler.setInterval(() => {
      void runFallbackTick();
    }, fallbackIntervalMs);
  };

  const ensureSafety = () => {
    if (disposed || safetyHandle !== null) return;
    safetyHandle = scheduler.setInterval(() => {
      void runSafetyTick();
    }, safetyIntervalMs);
  };

  const applySnapshot = (raw: unknown) => {
    if (disposed) return;
    try {
      const snapshot = adaptQuote(raw);
      lastSnapshot = snapshot;
      callbacks.onSnapshot(snapshot);
    } catch (err) {
      logger.error("[market] failed to adapt full snapshot", err);
    }
  };

  const onQuoteUpdate = (raw: unknown) => {
    if (disposed) return;
    try {
      const delta = adaptQuoteDelta(raw);
      if (lastSnapshot) {
        lastSnapshot = mergeQuoteDelta(lastSnapshot, delta);
        callbacks.onSnapshot(lastSnapshot);
      }
      // A delivered delta proves SignalR is live: drop fallback polling,
      // keep the slower safety resync.
      if (currentState !== "live") {
        setState("live");
      }
      stopFallback();
      ensureSafety();
    } catch (err) {
      logger.error("[market] failed to process QuoteUpdate delta", err);
    }
  };

  const runFallbackTick = async () => {
    if (disposed) return;
    try {
      const raw = await fetchSnapshot();
      if (disposed) return;
      applySnapshot(raw);
      // Don't downgrade "live" if SignalR happened to recover mid-fetch.
      if (currentState !== "live") {
        setState("stale", "Live feed unavailable \u2014 using REST fallback polling");
      }
    } catch (err) {
      if (disposed) return;
      const msg = err instanceof Error ? err.message : String(err);
      logger.warn("[market] fallback poll failed", err);
      if (currentState !== "live") {
        setState("error", msg);
      }
    }
    void tryRestartHub();
  };

  const runSafetyTick = async () => {
    if (disposed) return;
    try {
      const raw = await fetchSnapshot();
      if (disposed) return;
      applySnapshot(raw);
    } catch (err) {
      logger.warn("[market] safety resync failed", err);
    }
  };

  const tryRestartHub = async () => {
    if (disposed || restartingHub || !hub) return;
    if (hub.state !== HubConnectionState.Disconnected) return;
    restartingHub = true;
    try {
      await hub.start();
      if (disposed) return;
      logger.info("[market] SignalR restarted after fallback");
      setState("live");
      stopFallback();
      ensureSafety();
      try {
        const raw = await fetchSnapshot();
        if (!disposed) applySnapshot(raw);
      } catch (err) {
        logger.warn("[market] resync after restart failed", err);
      }
    } catch (err) {
      logger.warn("[market] SignalR restart attempt failed; staying in fallback", err);
    } finally {
      restartingHub = false;
    }
  };

  const wireHub = (h: MarketHub) => {
    h.on("QuoteUpdate", onQuoteUpdate);

    h.onreconnecting((err) => {
      if (disposed) return;
      logger.warn("[market] SignalR reconnecting", err);
      stopSafety();
      ensureFallback();
      setState("stale", "Reconnecting to market feed\u2026");
    });

    h.onreconnected((connectionId) => {
      if (disposed) return;
      logger.info("[market] SignalR reconnected", connectionId);
      stopFallback();
      ensureSafety();
      setState("live");
      fetchSnapshot()
        .then((raw) => { if (!disposed) applySnapshot(raw); })
        .catch((err) => logger.warn("[market] resync after reconnect failed", err));
    });

    h.onclose((err) => {
      if (disposed) return;
      logger.warn("[market] SignalR connection closed", err);
      stopSafety();
      ensureFallback();
      setState("stale", "Market feed disconnected \u2014 using REST fallback");
    });
  };

  const start = async () => {
    if (disposed) return;
    setState("connecting");

    let initialSnapshotOk = false;
    try {
      const raw = await fetchSnapshot();
      if (disposed) return;
      applySnapshot(raw);
      initialSnapshotOk = true;
    } catch (err) {
      if (disposed) return;
      logger.warn("[market] initial /api/quote fetch failed", err);
    }

    if (disposed) return;

    hub = createHub();
    wireHub(hub);

    try {
      await hub.start();
      if (disposed) return;
      logger.info("[market] SignalR start succeeded");
      setState("live");
      ensureSafety();

      if (!initialSnapshotOk) {
        try {
          const raw = await fetchSnapshot();
          if (!disposed) applySnapshot(raw);
        } catch (err) {
          logger.warn("[market] post-start snapshot retry failed", err);
        }
      }
    } catch (err) {
      if (disposed) return;
      const msg = err instanceof Error ? err.message : String(err);
      logger.warn("[market] SignalR start failed; entering REST fallback", err);
      if (initialSnapshotOk) {
        setState("stale", "Live feed unavailable \u2014 using REST fallback polling");
      } else {
        setState("error", `Backend unreachable: ${msg}`);
      }
      ensureFallback();
    }
  };

  const stop = async () => {
    disposed = true;
    stopFallback();
    stopSafety();
    if (hub && hub.state !== HubConnectionState.Disconnected) {
      try {
        await hub.stop();
      } catch {
        // ignore stop errors during teardown
      }
    }
    hub = null;
  };

  return { start, stop };
}
