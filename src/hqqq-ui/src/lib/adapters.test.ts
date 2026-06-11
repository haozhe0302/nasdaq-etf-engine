import { describe, it, expect } from "vitest";
import {
  adaptQuoteDelta,
  mergeQuoteDelta,
  adaptQuote,
  adaptConstituents,
  resolveSessionSeriesWindow,
} from "./adapters";
import type { MarketSnapshot, TimeSeriesPoint } from "./types";

// ── Helpers ─────────────────────────────────────────

function makeBackendDelta(overrides: Record<string, unknown> = {}) {
  return {
    nav: 123.45,
    navChangePct: 0.12,
    marketPrice: 456.78,
    premiumDiscountPct: 0.05,
    qqq: 456.78,
    qqqChangePct: 0.5,
    basketValueB: 1.23,
    asOf: "2026-04-05T14:30:00Z",
    latestSeriesPoint: null,
    movers: [
      {
        symbol: "AAPL",
        name: "Apple Inc",
        changePct: 1.5,
        impact: 12.3,
        direction: "up",
      },
    ],
    freshness: {
      symbolsTotal: 100,
      symbolsFresh: 95,
      symbolsStale: 5,
      freshPct: 95,
      lastTickUtc: "2026-04-05T14:29:59Z",
      avgTickIntervalMs: 250,
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

function makeSnapshot(seriesOverride?: TimeSeriesPoint[]): MarketSnapshot {
  return {
    nav: 100,
    navChangePct: 0,
    marketPrice: 400,
    premiumDiscountPct: 0,
    qqq: 400,
    qqqChangePct: 0,
    basketValueB: 1,
    asOf: new Date("2026-04-05T14:00:00Z"),
    series: seriesOverride ?? [
      { time: "2026-04-05T14:00:00Z", nav: 100, market: 400 },
      { time: "2026-04-05T14:01:00Z", nav: 100.5, market: 400.2 },
    ],
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
    quoteState: "live",
    isLive: true,
    isFrozen: false,
    pauseReason: null,
    marketSession: {
      state: "regular_open",
      label: "Regular Session",
      isRegularSessionOpen: true,
      isTradingDay: true,
      nextOpenUtc: null,
    },
  };
}

function makeDeltaWith(time: string, nav: number, market: number) {
  return adaptQuoteDelta(
    makeBackendDelta({
      latestSeriesPoint: { time, nav, market },
    }),
  );
}

// ── adaptQuoteDelta ─────────────────────────────────

describe("adaptQuoteDelta", () => {
  it("parses scalar fields correctly", () => {
    const raw = makeBackendDelta();
    const delta = adaptQuoteDelta(raw);

    expect(delta.nav).toBe(123.45);
    expect(delta.navChangePct).toBe(0.12);
    expect(delta.marketPrice).toBe(456.78);
    expect(delta.qqq).toBe(456.78);
    expect(delta.basketValueB).toBe(1.23);
    expect(delta.asOf).toBeInstanceOf(Date);
  });

  it("returns null latestSeriesPoint when backend sends null", () => {
    const raw = makeBackendDelta({ latestSeriesPoint: null });
    const delta = adaptQuoteDelta(raw);
    expect(delta.latestSeriesPoint).toBeNull();
  });

  it("parses latestSeriesPoint when present", () => {
    const raw = makeBackendDelta({
      latestSeriesPoint: {
        time: "2026-04-05T14:30:00Z",
        nav: 123.45,
        market: 456.78,
      },
    });
    const delta = adaptQuoteDelta(raw);
    expect(delta.latestSeriesPoint).toEqual({
      time: "2026-04-05T14:30:00Z",
      nav: 123.45,
      market: 456.78,
    });
  });

  it("maps movers with impactBps from impact", () => {
    const raw = makeBackendDelta();
    const delta = adaptQuoteDelta(raw);
    expect(delta.movers).toHaveLength(1);
    expect(delta.movers[0].impactBps).toBe(12.3);
  });

  it("maps feed statuses", () => {
    const raw = makeBackendDelta();
    const delta = adaptQuoteDelta(raw);
    expect(delta.feeds.length).toBeGreaterThanOrEqual(3);
    expect(delta.feeds[0].name).toBe("Market Data");
  });

  it("adds REST Fallback feed when fallbackActive is true", () => {
    const raw = makeBackendDelta({
      feeds: {
        webSocketConnected: true,
        fallbackActive: true,
        pricingActive: true,
        basketState: "active",
        pendingActivationBlocked: false,
        pendingBlockedReason: null,
      },
    });
    const delta = adaptQuoteDelta(raw);
    const fallback = delta.feeds.find((f) => f.name === "REST Fallback");
    expect(fallback).toBeDefined();
    expect(fallback!.status).toBe("degraded");
  });
});

// ── Session window resolution ───────────────────────

describe("resolveSessionSeriesWindow", () => {
  it("returns today's regular session window when regular_open", () => {
    const asOf = new Date("2026-04-17T19:40:00Z"); // 15:40 ET
    const now = new Date("2026-04-17T19:40:00Z");
    const w = resolveSessionSeriesWindow(
      {
        state: "regular_open",
        label: "Regular Session",
        isRegularSessionOpen: true,
        isTradingDay: true,
        nextOpenUtc: null,
      },
      asOf,
      now,
    );
    expect(w.mode).toBe("regular_open");
    expect(w.windowStartUtcMs).not.toBeNull();
    expect(w.windowEndUtcMs).not.toBeNull();
  });

  it("returns most recent completed session when closed", () => {
    const asOf = new Date("2026-04-11T13:10:00Z"); // Saturday 09:10 ET
    const now = new Date("2026-04-11T13:10:00Z");
    const w = resolveSessionSeriesWindow(
      {
        state: "closed",
        label: "Closed",
        isRegularSessionOpen: false,
        isTradingDay: false,
        nextOpenUtc: null,
      },
      asOf,
      now,
    );
    expect(w.mode).toBe("closed_session");
    expect(w.windowStartUtcMs).not.toBeNull();
    expect(w.windowEndUtcMs).not.toBeNull();
    // Last completed session should be Friday close window.
    expect(new Date(w.windowStartUtcMs!).toISOString().startsWith("2026-04-10")).toBe(true);
  });

  it("returns empty mode during pre_open", () => {
    const w = resolveSessionSeriesWindow(
      {
        state: "pre_open",
        label: "Pre-open",
        isRegularSessionOpen: false,
        isTradingDay: true,
        nextOpenUtc: null,
      },
      new Date("2026-04-17T13:26:00Z"),
      new Date("2026-04-17T13:26:00Z"),
    );
    expect(w.mode).toBe("pre_open_empty");
    expect(w.windowStartUtcMs).toBeNull();
    expect(w.windowEndUtcMs).toBeNull();
  });

  it("returns passthrough for unknown state", () => {
    const w = resolveSessionSeriesWindow(
      {
        state: "unknown",
        label: "",
        isRegularSessionOpen: false,
        isTradingDay: false,
        nextOpenUtc: null,
      },
      new Date("2026-04-17T15:00:00Z"),
      new Date("2026-04-17T15:00:00Z"),
    );
    expect(w.mode).toBe("passthrough");
  });
});

// ── mergeQuoteDelta ─────────────────────────────────

describe("mergeQuoteDelta", () => {
  it("updates scalar fields from delta", () => {
    const prev = makeSnapshot();
    const delta = adaptQuoteDelta(makeBackendDelta());
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.nav).toBe(123.45);
    expect(merged.marketPrice).toBe(456.78);
    expect(merged.navChangePct).toBe(0.12);
  });

  it("preserves existing series when delta has no new point", () => {
    const prev = makeSnapshot();
    const delta = adaptQuoteDelta(makeBackendDelta({ latestSeriesPoint: null }));
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.series).toHaveLength(2);
    expect(merged.series).toEqual(prev.series);
  });

  it("appends point when timestamp is newer than last", () => {
    const prev = makeSnapshot();
    const delta = makeDeltaWith("2026-04-05T14:02:00Z", 101, 401);
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.series).toHaveLength(3);
    expect(merged.series[2]).toEqual({
      time: "2026-04-05T14:02:00Z",
      nav: 101,
      market: 401,
    });
  });

  it("replaces last point when timestamp equals last", () => {
    const prev = makeSnapshot([
      { time: "2026-04-05T14:00:00Z", nav: 100, market: 400 },
      { time: "2026-04-05T14:01:00Z", nav: 100.5, market: 400.2 },
    ]);
    const delta = makeDeltaWith("2026-04-05T14:01:00Z", 100.9, 400.5);
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.series).toHaveLength(2);
    expect(merged.series[1]).toEqual({
      time: "2026-04-05T14:01:00Z",
      nav: 100.9,
      market: 400.5,
    });
    expect(merged.series[0]).toEqual(prev.series[0]);
  });

  it("ignores point when timestamp is older than last", () => {
    const prev = makeSnapshot([
      { time: "2026-04-05T14:00:00Z", nav: 100, market: 400 },
      { time: "2026-04-05T14:01:00Z", nav: 100.5, market: 400.2 },
    ]);
    const delta = makeDeltaWith("2026-04-05T13:59:00Z", 99, 399);
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.series).toHaveLength(2);
    expect(merged.series).toEqual(prev.series);
  });

  it("appends into empty series", () => {
    const prev = makeSnapshot([]);
    const delta = makeDeltaWith("2026-04-05T14:00:00Z", 100, 400);
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.series).toHaveLength(1);
    expect(merged.series[0].time).toBe("2026-04-05T14:00:00Z");
  });

  it("keeps morning data at 15:40 regular session (no fixed-point truncation)", () => {
    const prev = makeSnapshot([
      { time: "2026-04-05T13:49:00Z", nav: 99, market: 399 }, // 09:49 ET
      { time: "2026-04-05T19:39:00Z", nav: 100, market: 400 }, // 15:39 ET
    ]);
    const delta = adaptQuoteDelta(
      makeBackendDelta({
        asOf: "2026-04-05T19:40:00Z", // 15:40 ET
        latestSeriesPoint: { time: "2026-04-05T19:40:00Z", nav: 101, market: 401 },
        feeds: {
          webSocketConnected: true,
          fallbackActive: false,
          pricingActive: true,
          basketState: "active",
          pendingActivationBlocked: false,
          pendingBlockedReason: null,
          marketSessionState: "regular_open",
          isRegularSessionOpen: true,
          isTradingDay: true,
          nextOpenUtc: null,
          sessionLabel: "Regular Session",
        },
      }),
    );
    const merged = mergeQuoteDelta(prev, delta);
    expect(merged.series.map((p) => p.time)).toContain("2026-04-05T13:49:00Z");
    expect(merged.series.map((p) => p.time)).toContain("2026-04-05T19:40:00Z");
  });

  it("keeps last completed session during closed state", () => {
    const prev = makeSnapshot([
      { time: "2026-04-10T13:30:00Z", nav: 90, market: 390 }, // Friday 09:30 ET
      { time: "2026-04-10T19:59:00Z", nav: 95, market: 395 }, // Friday 15:59 ET
      { time: "2026-04-11T13:10:00Z", nav: 96, market: 396 }, // Saturday stray point
    ]);
    const delta = adaptQuoteDelta(
      makeBackendDelta({
        asOf: "2026-04-11T13:10:00Z", // Saturday 09:10 ET
        latestSeriesPoint: null,
        feeds: {
          webSocketConnected: true,
          fallbackActive: false,
          pricingActive: true,
          basketState: "active",
          pendingActivationBlocked: false,
          pendingBlockedReason: null,
          marketSessionState: "closed",
          isRegularSessionOpen: false,
          isTradingDay: false,
          nextOpenUtc: null,
          sessionLabel: "Closed",
        },
      }),
    );
    const merged = mergeQuoteDelta(prev, delta);
    expect(merged.series.map((p) => p.time)).toContain("2026-04-10T13:30:00Z");
    expect(merged.series.map((p) => p.time)).toContain("2026-04-10T19:59:00Z");
    expect(merged.series.map((p) => p.time)).not.toContain("2026-04-11T13:10:00Z");
  });

  it("returns empty series during pre_open", () => {
    const prev = makeSnapshot([
      { time: "2026-04-10T13:30:00Z", nav: 90, market: 390 },
      { time: "2026-04-10T19:59:00Z", nav: 95, market: 395 },
    ]);
    const delta = adaptQuoteDelta(
      makeBackendDelta({
        asOf: "2026-04-11T13:26:00Z",
        latestSeriesPoint: null,
        feeds: {
          webSocketConnected: true,
          fallbackActive: false,
          pricingActive: true,
          basketState: "active",
          pendingActivationBlocked: false,
          pendingBlockedReason: null,
          marketSessionState: "pre_open",
          isRegularSessionOpen: false,
          isTradingDay: true,
          nextOpenUtc: null,
          sessionLabel: "Pre-open",
        },
      }),
    );
    const merged = mergeQuoteDelta(prev, delta);
    expect(merged.series).toHaveLength(0);
  });

  it("replaces movers and freshness from delta", () => {
    const prev = makeSnapshot();
    const delta = adaptQuoteDelta(makeBackendDelta());
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.movers).toHaveLength(1);
    expect(merged.movers[0].symbol).toBe("AAPL");
    expect(merged.freshness.totalSymbols).toBe(100);
  });
});

// ── Reconnect full snapshot replace ─────────────────

describe("reconnect full snapshot replace", () => {
  it("adaptQuote produces complete MarketSnapshot from REST response", () => {
    const fullBackend = {
      nav: 200,
      navChangePct: 0.5,
      marketPrice: 500,
      premiumDiscountPct: 0.1,
      qqq: 500,
      basketValueB: 2,
      asOf: "2026-04-05T15:00:00Z",
      series: [
        { time: "2026-04-05T15:00:00Z", nav: 200, market: 500 },
        { time: "2026-04-05T15:01:00Z", nav: 201, market: 501 },
      ],
      movers: [],
      freshness: {
        symbolsTotal: 50,
        symbolsFresh: 48,
        symbolsStale: 2,
        freshPct: 96,
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
        marketSessionState: "regular_open",
        isRegularSessionOpen: true,
        isTradingDay: true,
        nextOpenUtc: null,
        sessionLabel: "Regular Session",
      },
    };
    const snapshot = adaptQuote(fullBackend);

    expect(snapshot.series).toHaveLength(2);
    expect(snapshot.nav).toBe(200);
    expect(snapshot.asOf).toBeInstanceOf(Date);
  });

  it("full snapshot completely replaces stale local state", () => {
    const staleLocal = makeSnapshot([
      { time: "2026-04-05T12:00:00Z", nav: 90, market: 380 },
    ]);

    const freshBackend = {
      nav: 200,
      navChangePct: 0.5,
      marketPrice: 500,
      premiumDiscountPct: 0.1,
      qqq: 500,
      basketValueB: 2,
      asOf: "2026-04-05T15:00:00Z",
      series: [
        { time: "2026-04-05T14:00:00Z", nav: 195, market: 495 },
        { time: "2026-04-05T15:00:00Z", nav: 200, market: 500 },
      ],
      movers: [],
      freshness: {
        symbolsTotal: 50,
        symbolsFresh: 48,
        symbolsStale: 2,
        freshPct: 96,
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
        marketSessionState: "regular_open",
        isRegularSessionOpen: true,
        isTradingDay: true,
        nextOpenUtc: null,
        sessionLabel: "Regular Session",
      },
    };
    const freshSnapshot = adaptQuote(freshBackend);

    expect(freshSnapshot.series).toHaveLength(2);
    expect(freshSnapshot.nav).toBe(200);
    expect(freshSnapshot.series[0].time).toBe("2026-04-05T14:00:00Z");
    expect(staleLocal.series[0].time).toBe("2026-04-05T12:00:00Z");
    expect(freshSnapshot.series).not.toEqual(staleLocal.series);
  });
});

// ── adaptConstituents.changePct null handling ───────

describe("adaptConstituents", () => {
  function makeBackendConstituents(changePct: number | null) {
    return {
      holdings: [
        {
          symbol: "AAPL",
          name: "Apple",
          sector: "Tech",
          weight: 60,
          shares: 1000,
          price: 200,
          changePct,
          marketValue: 200_000,
          sharesOrigin: "official",
          isStale: false,
        },
      ],
      concentration: {
        top5Pct: 100,
        top10Pct: 100,
        top20Pct: 100,
        sectorCount: 1,
        herfindahlIndex: 1,
      },
      quality: {
        totalSymbols: 1,
        officialSharesCount: 1,
        derivedSharesCount: 0,
        pricedCount: 1,
        staleCount: 0,
        priceCoveragePct: 100,
        basketMode: "official",
      },
      source: {
        anchorSource: "test",
        tailSource: "test",
        basketMode: "official",
        isDegraded: false,
        asOfDate: "2026-04-16",
        fingerprint: "fp",
      },
      asOf: "2026-04-16T13:30:00Z",
    };
  }

  it("preserves a numeric changePct as-is", () => {
    const snapshot = adaptConstituents(makeBackendConstituents(1.23));
    expect(snapshot.holdings[0].changePct).toBe(1.23);
  });

  it("preserves changePct=null instead of coercing to 0", () => {
    // Regression: the old adapter mapped `h.changePct ?? 0`, which forced
    // the UI to render "+0.00%" whenever the backend had no previous-close.
    const snapshot = adaptConstituents(makeBackendConstituents(null));
    expect(snapshot.holdings[0].changePct).toBeNull();
  });

  it("preserves changePct=0 as a real zero (no movement vs prev close)", () => {
    const snapshot = adaptConstituents(makeBackendConstituents(0));
    expect(snapshot.holdings[0].changePct).toBe(0);
  });
});
