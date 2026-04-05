import { describe, it, expect } from "vitest";
import {
  adaptQuoteDelta,
  mergeQuoteDelta,
  MAX_SERIES_POINTS,
  adaptQuote,
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
  };
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

  it("appends new series point from delta", () => {
    const prev = makeSnapshot();
    const delta = adaptQuoteDelta(
      makeBackendDelta({
        latestSeriesPoint: {
          time: "2026-04-05T14:02:00Z",
          nav: 101,
          market: 401,
        },
      }),
    );
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.series).toHaveLength(3);
    expect(merged.series[2]).toEqual({
      time: "2026-04-05T14:02:00Z",
      nav: 101,
      market: 401,
    });
  });

  it("does not create duplicate when timestamp matches last point", () => {
    const prev = makeSnapshot([
      { time: "2026-04-05T14:01:00Z", nav: 100.5, market: 400.2 },
    ]);
    const delta = adaptQuoteDelta(
      makeBackendDelta({
        latestSeriesPoint: {
          time: "2026-04-05T14:01:00Z",
          nav: 100.6,
          market: 400.3,
        },
      }),
    );
    const merged = mergeQuoteDelta(prev, delta);
    expect(merged.series).toHaveLength(1);
  });

  it("caps series at MAX_SERIES_POINTS", () => {
    const bigSeries: TimeSeriesPoint[] = Array.from(
      { length: MAX_SERIES_POINTS },
      (_, i) => ({
        time: `2026-04-05T10:${String(i).padStart(4, "0")}Z`,
        nav: 100 + i * 0.01,
        market: 400 + i * 0.01,
      }),
    );
    const prev = makeSnapshot(bigSeries);
    const delta = adaptQuoteDelta(
      makeBackendDelta({
        latestSeriesPoint: {
          time: "2026-04-05T20:00:00Z",
          nav: 200,
          market: 500,
        },
      }),
    );
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.series).toHaveLength(MAX_SERIES_POINTS);
    expect(merged.series[merged.series.length - 1].time).toBe(
      "2026-04-05T20:00:00Z",
    );
    expect(merged.series[0].time).not.toBe(bigSeries[0].time);
  });

  it("replaces movers and freshness from delta", () => {
    const prev = makeSnapshot();
    const delta = adaptQuoteDelta(makeBackendDelta());
    const merged = mergeQuoteDelta(prev, delta);

    expect(merged.movers).toHaveLength(1);
    expect(merged.movers[0].symbol).toBe("AAPL");
    expect(merged.freshness.totalSymbols).toBe(100);
  });

  it("full snapshot replace on reconnect works", () => {
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
      },
    };
    const snapshot = adaptQuote(fullBackend);
    expect(snapshot.series).toHaveLength(1);
    expect(snapshot.nav).toBe(200);
    expect(snapshot.asOf).toBeInstanceOf(Date);
  });
});
