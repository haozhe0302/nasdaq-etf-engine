import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { HubConnectionState } from "@microsoft/signalr";
import {
  createMarketController,
  FALLBACK_POLL_INTERVAL_MS,
  SAFETY_RESYNC_INTERVAL_MS,
  type MarketHub,
  type Scheduler,
} from "./marketController";
import type { ConnectionState, MarketSnapshot } from "./types";

// ── Fake backend payloads ───────────────────────────

function makeBackendQuote(overrides: Record<string, unknown> = {}) {
  return {
    nav: 100,
    navChangePct: 0,
    marketPrice: 400,
    premiumDiscountPct: 0,
    qqq: 400,
    qqqChangePct: 0,
    basketValueB: 1,
    asOf: "2026-04-05T14:00:00Z",
    series: [{ time: "2026-04-05T14:00:00Z", nav: 100, market: 400 }],
    movers: [],
    freshness: {
      symbolsTotal: 50,
      symbolsFresh: 50,
      symbolsStale: 0,
      freshPct: 100,
      lastTickUtc: null,
      avgTickIntervalMs: null,
    },
    feeds: {
      webSocketConnected: true,
      fallbackActive: false,
      pricingActive: true,
      basketState: "active",
      pendingActivationBlocked: false,
      pendingBlockedReason: null,
    },
    ...overrides,
  };
}

// ── Fake HubConnection ──────────────────────────────

interface FakeHub extends MarketHub {
  startMock: ReturnType<typeof vi.fn>;
  stopMock: ReturnType<typeof vi.fn>;
  emitQuoteUpdate: (raw: unknown) => void;
  emitReconnecting: (err?: Error) => void;
  emitReconnected: (id?: string) => void;
  emitClose: (err?: Error) => void;
  setState: (s: HubConnectionState) => void;
}

function makeFakeHub(
  startBehavior: "succeed" | "fail" | "queue" = "succeed",
): FakeHub {
  let state: HubConnectionState = HubConnectionState.Disconnected;
  const handlers: {
    quoteUpdate: ((raw: unknown) => void) | null;
    reconnecting: ((err?: Error) => void) | null;
    reconnected: ((id?: string) => void) | null;
    close: ((err?: Error) => void) | null;
  } = { quoteUpdate: null, reconnecting: null, reconnected: null, close: null };

  const startMock = vi.fn().mockImplementation(async () => {
    if (startBehavior === "fail") {
      throw new Error("hub start failed");
    }
    state = HubConnectionState.Connected;
  });
  const stopMock = vi.fn().mockImplementation(async () => {
    state = HubConnectionState.Disconnected;
  });

  const fake: FakeHub = {
    get state() {
      return state;
    },
    on: ((_method: string, handler: (raw: unknown) => void) => {
      handlers.quoteUpdate = handler;
    }) as MarketHub["on"],
    onreconnecting: ((handler: (err?: Error) => void) => {
      handlers.reconnecting = handler;
    }) as MarketHub["onreconnecting"],
    onreconnected: ((handler: (id?: string) => void) => {
      handlers.reconnected = handler;
    }) as MarketHub["onreconnected"],
    onclose: ((handler: (err?: Error) => void) => {
      handlers.close = handler;
    }) as MarketHub["onclose"],
    start: startMock as unknown as MarketHub["start"],
    stop: stopMock as unknown as MarketHub["stop"],
    startMock,
    stopMock,
    emitQuoteUpdate: (raw) => handlers.quoteUpdate?.(raw),
    emitReconnecting: (err) => {
      state = HubConnectionState.Reconnecting;
      handlers.reconnecting?.(err);
    },
    emitReconnected: (id) => {
      state = HubConnectionState.Connected;
      handlers.reconnected?.(id);
    },
    emitClose: (err) => {
      state = HubConnectionState.Disconnected;
      handlers.close?.(err);
    },
    setState: (s) => {
      state = s;
    },
  };

  return fake;
}

// ── Manual scheduler so timer tests are deterministic ──

interface ManualScheduler extends Scheduler {
  advanceByMs: (ms: number) => Promise<void>;
  /** Map: handle -> {handler, intervalMs, nextFireAt}. */
  active: () => number;
}

function makeManualScheduler(): ManualScheduler {
  interface Entry {
    handler: () => void;
    intervalMs: number;
    nextFireAt: number;
  }
  const entries = new Map<number, Entry>();
  let nextId = 1;
  let now = 0;

  return {
    setInterval(handler, ms) {
      const id = nextId++;
      entries.set(id, { handler, intervalMs: ms, nextFireAt: now + ms });
      return id;
    },
    clearInterval(handle) {
      entries.delete(handle as number);
    },
    async advanceByMs(ms) {
      const target = now + ms;
      // Drain all entries whose nextFireAt <= target, in chronological order
      // and re-arming for next cycle.
      while (true) {
        let nextEntry: Entry | undefined;
        let nextEntryId: number | undefined;
        for (const [id, e] of entries) {
          if (e.nextFireAt <= target) {
            if (nextEntry === undefined || e.nextFireAt < nextEntry.nextFireAt) {
              nextEntry = e;
              nextEntryId = id;
            }
          }
        }
        if (!nextEntry || nextEntryId === undefined) break;
        now = nextEntry.nextFireAt;
        nextEntry.nextFireAt += nextEntry.intervalMs;
        nextEntry.handler();
        // Yield to microtasks so async handlers inside the controller can settle.
        await Promise.resolve();
        await Promise.resolve();
      }
      now = target;
    },
    active: () => entries.size,
  };
}

// ── Helpers ─────────────────────────────────────────

async function flushMicrotasks(): Promise<void> {
  // Multiple microtask yields to drain promise chains inside start().
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

interface Capture {
  snapshots: MarketSnapshot[];
  states: Array<{ state: ConnectionState; error?: string }>;
}

function lastState(c: Capture): { state: ConnectionState; error?: string } | undefined {
  return c.states.length > 0 ? c.states[c.states.length - 1] : undefined;
}

function makeCapture(): Capture & {
  callbacks: {
    onSnapshot: (s: MarketSnapshot) => void;
    onState: (s: ConnectionState, e?: string) => void;
  };
} {
  const c: Capture = { snapshots: [], states: [] };
  return {
    ...c,
    callbacks: {
      onSnapshot: (s) => c.snapshots.push(s),
      onState: (state, error) => c.states.push({ state, error }),
    },
  };
}

const silentLogger = {
  info: vi.fn(),
  warn: vi.fn(),
  error: vi.fn(),
};

beforeEach(() => {
  silentLogger.info.mockReset();
  silentLogger.warn.mockReset();
  silentLogger.error.mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

// ── Tests ───────────────────────────────────────────

describe("createMarketController", () => {
  it("fetches initial snapshot, starts hub, and reports live state", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("succeed");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockResolvedValue(makeBackendQuote());

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();

    expect(fetchSnapshot).toHaveBeenCalledTimes(1);
    expect(hub.startMock).toHaveBeenCalledTimes(1);
    expect(cap.snapshots.length).toBeGreaterThan(0);
    // Final state should be "live".
    expect(lastState(cap)?.state).toBe("live");
    // Safety timer should be running, fallback should not be.
    expect(sched.active()).toBe(1);

    await controller.stop();
    expect(sched.active()).toBe(0);
    expect(hub.stopMock).toHaveBeenCalled();
  });

  it("falls back to REST polling every 5s when SignalR start fails", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("fail");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockResolvedValue(makeBackendQuote());

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();

    // After failure -> "stale" fallback state, fetchSnapshot called once (initial).
    expect(lastState(cap)?.state).toBe("stale");
    expect(fetchSnapshot).toHaveBeenCalledTimes(1);
    expect(sched.active()).toBe(1); // fallback timer only

    // Two fallback ticks at 5s each -> +2 fetches.
    await sched.advanceByMs(FALLBACK_POLL_INTERVAL_MS);
    await flushMicrotasks();
    await sched.advanceByMs(FALLBACK_POLL_INTERVAL_MS);
    await flushMicrotasks();

    expect(fetchSnapshot.mock.calls.length).toBeGreaterThanOrEqual(3);

    await controller.stop();
    expect(sched.active()).toBe(0);
  });

  it("runs the 30s safety resync while SignalR is live", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("succeed");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockResolvedValue(makeBackendQuote());

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();

    // 1 initial fetch.
    expect(fetchSnapshot).toHaveBeenCalledTimes(1);

    // Advance under safety interval - no extra fetches.
    await sched.advanceByMs(SAFETY_RESYNC_INTERVAL_MS - 1);
    await flushMicrotasks();
    expect(fetchSnapshot).toHaveBeenCalledTimes(1);

    // Cross the safety boundary - one more fetch.
    await sched.advanceByMs(2);
    await flushMicrotasks();
    expect(fetchSnapshot).toHaveBeenCalledTimes(2);

    // Another safety cycle.
    await sched.advanceByMs(SAFETY_RESYNC_INTERVAL_MS);
    await flushMicrotasks();
    expect(fetchSnapshot).toHaveBeenCalledTimes(3);

    await controller.stop();
  });

  it("enters fallback mode on hub close and swaps back to safety on restart", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("succeed");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockResolvedValue(makeBackendQuote());

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();
    expect(lastState(cap)?.state).toBe("live");
    expect(sched.active()).toBe(1); // safety timer

    // Hub closes -> should drop safety, start fallback, state -> stale.
    hub.emitClose(new Error("ws dropped"));
    await flushMicrotasks();
    expect(lastState(cap)?.state).toBe("stale");
    expect(sched.active()).toBe(1); // fallback timer

    // Fallback tick triggers restart - hub.start succeeds (we reset behavior).
    // Note: hub.startMock returned successfully the first time and our fake
    // hub.start always returns success in the "succeed" mode.
    await sched.advanceByMs(FALLBACK_POLL_INTERVAL_MS);
    await flushMicrotasks();

    // After successful restart, state should be "live" again and safety timer
    // active (fallback cleared).
    expect(lastState(cap)?.state).toBe("live");
    expect(sched.active()).toBe(1);

    await controller.stop();
  });

  it("treats incoming QuoteUpdate deltas as proof of liveness", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("fail");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockResolvedValue(makeBackendQuote());

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();
    // We're in fallback because the hub start failed.
    expect(lastState(cap)?.state).toBe("stale");
    expect(sched.active()).toBe(1); // fallback only

    // Now simulate a delta arriving anyway (e.g. hub auto-reconnect succeeded
    // out of band). The controller should switch to "live" and swap timers.
    hub.setState(HubConnectionState.Connected);
    hub.emitQuoteUpdate({
      ...makeBackendQuote(),
      latestSeriesPoint: { time: "2026-04-05T14:01:00Z", nav: 101, market: 401 },
    });
    await flushMicrotasks();

    expect(lastState(cap)?.state).toBe("live");
    expect(sched.active()).toBe(1); // safety only

    await controller.stop();
  });

  it("recovers via onreconnected by refetching snapshot and switching to live", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("succeed");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockResolvedValue(makeBackendQuote());

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();

    // 1 initial fetch.
    expect(fetchSnapshot).toHaveBeenCalledTimes(1);

    hub.emitReconnecting(new Error("dropped"));
    await flushMicrotasks();
    expect(lastState(cap)?.state).toBe("stale");

    hub.emitReconnected("conn-123");
    await flushMicrotasks();
    expect(lastState(cap)?.state).toBe("live");
    // Reconnect triggers a resync fetch.
    expect(fetchSnapshot).toHaveBeenCalledTimes(2);

    await controller.stop();
  });

  it("stop() clears all timers and stops the hub exactly once", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("succeed");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockResolvedValue(makeBackendQuote());

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();
    expect(sched.active()).toBe(1);

    await controller.stop();
    expect(sched.active()).toBe(0);
    expect(hub.stopMock).toHaveBeenCalledTimes(1);

    // Subsequent timer firings (if any leaked) would have no effect after
    // disposal. Verify no extra fetches happen after stop.
    const before = fetchSnapshot.mock.calls.length;
    await sched.advanceByMs(SAFETY_RESYNC_INTERVAL_MS * 2);
    await flushMicrotasks();
    expect(fetchSnapshot.mock.calls.length).toBe(before);
  });

  it("reports error state when initial fetch AND hub start both fail", async () => {
    const cap = makeCapture();
    const hub = makeFakeHub("fail");
    const sched = makeManualScheduler();
    const fetchSnapshot = vi.fn().mockRejectedValue(new Error("network down"));

    const controller = createMarketController(
      {
        fetchSnapshot,
        createHub: () => hub,
        logger: silentLogger,
        scheduler: sched,
      },
      cap.callbacks,
    );

    await controller.start();
    await flushMicrotasks();

    expect(lastState(cap)?.state).toBe("error");
    // Fallback timer must still be running so we can recover.
    expect(sched.active()).toBe(1);

    await controller.stop();
  });
});
