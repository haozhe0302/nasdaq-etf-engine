import { describe, it, expect } from "vitest";
import { getChartMode, getChartBoundsUtc } from "./marketSessionClient";

// All anchors are weekdays in April 2026 (EDT, UTC-4):
//   09:25 ET = 13:25Z, 09:30 ET = 13:30Z, 16:00 ET = 20:00Z.
const D = (iso: string) => new Date(iso);

describe("getChartMode", () => {
  it("is after_hours before the pre-open window", () => {
    expect(getChartMode(D("2026-04-16T13:00:00Z"))).toBe("after_hours"); // 09:00 ET
  });

  it("is pre_open_reset between 09:25 and 09:30 ET", () => {
    expect(getChartMode(D("2026-04-16T13:25:00Z"))).toBe("pre_open_reset");
    expect(getChartMode(D("2026-04-16T13:29:00Z"))).toBe("pre_open_reset");
  });

  it("is regular from 09:30 (inclusive) to 16:00 (exclusive) ET", () => {
    expect(getChartMode(D("2026-04-16T13:30:00Z"))).toBe("regular"); // 09:30 ET
    expect(getChartMode(D("2026-04-16T18:00:00Z"))).toBe("regular"); // 14:00 ET
  });

  it("is after_hours at and after the close", () => {
    expect(getChartMode(D("2026-04-16T20:00:00Z"))).toBe("after_hours"); // 16:00 ET
    expect(getChartMode(D("2026-04-16T23:00:00Z"))).toBe("after_hours"); // 19:00 ET
  });

  it("is after_hours on weekends", () => {
    expect(getChartMode(D("2026-04-18T16:00:00Z"))).toBe("after_hours"); // Saturday noon ET
    expect(getChartMode(D("2026-04-19T16:00:00Z"))).toBe("after_hours"); // Sunday noon ET
  });
});

describe("getChartBoundsUtc", () => {
  it("uses today's session bounds during the regular session", () => {
    const bounds = getChartBoundsUtc(D("2026-04-16T18:00:00Z"));
    expect(bounds.open).toBe(Date.parse("2026-04-16T13:30:00Z"));
    expect(bounds.close).toBe(Date.parse("2026-04-16T20:00:00Z"));
  });

  it("uses the most recent completed session after hours", () => {
    // Thursday 19:00 ET → today's completed session.
    const after = getChartBoundsUtc(D("2026-04-16T23:00:00Z"));
    expect(after.open).toBe(Date.parse("2026-04-16T13:30:00Z"));
    expect(after.close).toBe(Date.parse("2026-04-16T20:00:00Z"));
  });

  it("rolls back over the weekend to Friday's session", () => {
    // Saturday → Friday 2026-04-17.
    const sat = getChartBoundsUtc(D("2026-04-18T16:00:00Z"));
    expect(sat.open).toBe(Date.parse("2026-04-17T13:30:00Z"));
    expect(sat.close).toBe(Date.parse("2026-04-17T20:00:00Z"));
  });

  it("rolls back to the prior weekday before the open", () => {
    // Thursday 08:00 ET (12:00Z) before open → Wednesday 2026-04-15.
    const preOpen = getChartBoundsUtc(D("2026-04-16T12:00:00Z"));
    expect(preOpen.open).toBe(Date.parse("2026-04-15T13:30:00Z"));
    expect(preOpen.close).toBe(Date.parse("2026-04-15T20:00:00Z"));
  });
});
