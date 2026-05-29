// Client-side America/New_York regular-session helper for the /market
// chart display policy. Pure time math, no holiday calendar (mirrors the
// backend RegularSessionClock posture). Authoritative session *state* still
// comes from the backend quote feeds; this only drives which series the
// chart renders and the x-axis bounds.

export type ChartMode = "regular" | "pre_open_reset" | "after_hours";

const PRE_OPEN_MINUTES = 9 * 60 + 25; // 09:25 ET
const OPEN_MINUTES = 9 * 60 + 30; // 09:30 ET
const CLOSE_MINUTES = 16 * 60; // 16:00 ET

interface EtParts {
  year: number;
  month: number; // 1-12
  day: number;
  hour: number; // 0-23
  minute: number;
  weekday: string; // "Mon".."Sun"
}

function getEtParts(date: Date): EtParts {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
    weekday: "short",
  }).formatToParts(date);

  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? "";
  let hour = parseInt(get("hour"), 10);
  if (hour === 24) hour = 0; // some engines emit "24" at midnight

  return {
    year: parseInt(get("year"), 10),
    month: parseInt(get("month"), 10),
    day: parseInt(get("day"), 10),
    hour,
    minute: parseInt(get("minute"), 10),
    weekday: get("weekday"),
  };
}

function isWeekendWeekday(weekday: string): boolean {
  return weekday === "Sat" || weekday === "Sun";
}

/**
 * Classifies the current chart display mode:
 *  - "regular":        09:30–16:00 ET on a weekday → render live series.
 *  - "pre_open_reset": 09:25–09:30 ET on a weekday → clear / "waiting".
 *  - "after_hours":    everything else (incl. weekends/overnight) → render
 *                      the most recent completed regular session.
 */
export function getChartMode(now: Date = new Date()): ChartMode {
  const et = getEtParts(now);
  if (isWeekendWeekday(et.weekday)) return "after_hours";

  const mins = et.hour * 60 + et.minute;
  if (mins >= OPEN_MINUTES && mins < CLOSE_MINUTES) return "regular";
  if (mins >= PRE_OPEN_MINUTES && mins < OPEN_MINUTES) return "pre_open_reset";
  return "after_hours";
}

/**
 * Eastern-time UTC offset (ET local minus UTC, in ms) at a given UTC
 * instant. Negative west of UTC (e.g. -4h during EDT). Runtime-timezone
 * independent — derived purely via Intl.
 */
function etOffsetMsAt(utcMs: number): number {
  const et = getEtParts(new Date(utcMs));
  const wallClockAsUtc = Date.UTC(et.year, et.month - 1, et.day, et.hour, et.minute);
  // Round the source instant down to the minute to match the parts precision.
  const sourceMinute = Math.floor(utcMs / 60_000) * 60_000;
  return wallClockAsUtc - sourceMinute;
}

/** Converts an ET wall-clock date+time to a UTC epoch-ms value. */
function etWallClockToUtcMs(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
): number {
  // Guess by treating the wall-clock as UTC, then subtract the ET offset at
  // that instant. 09:30/16:00 never sit in a DST transition gap, so a single
  // correction is exact.
  const guess = Date.UTC(year, month - 1, day, hour, minute);
  const offset = etOffsetMsAt(guess);
  return guess - offset;
}

/** ET calendar date of the most recent fully-completed regular session. */
function mostRecentCompletedSessionEtDate(now: Date): { year: number; month: number; day: number } {
  const et = getEtParts(now);
  // Anchor the ET calendar date in a UTC date object for safe day stepping.
  let anchor = new Date(Date.UTC(et.year, et.month - 1, et.day));
  const mins = et.hour * 60 + et.minute;

  // Today's session counts only once 16:00 ET has passed and it's a weekday.
  if (isWeekendWeekday(et.weekday) || mins < CLOSE_MINUTES) {
    anchor = new Date(anchor.getTime() - 86_400_000);
  }
  // Step back over weekends (UTC anchor day-of-week matches the ET date).
  while (anchor.getUTCDay() === 0 || anchor.getUTCDay() === 6) {
    anchor = new Date(anchor.getTime() - 86_400_000);
  }

  return {
    year: anchor.getUTCFullYear(),
    month: anchor.getUTCMonth() + 1,
    day: anchor.getUTCDate(),
  };
}

/**
 * Returns the UTC [open, close] epoch-ms bounds the chart x-axis should use.
 * During regular / pre-open these are today's 09:30–16:00 ET; after hours
 * they are the most recent completed session's bounds.
 */
export function getChartBoundsUtc(now: Date = new Date()): { open: number; close: number } {
  const mode = getChartMode(now);
  let date: { year: number; month: number; day: number };
  if (mode === "after_hours") {
    date = mostRecentCompletedSessionEtDate(now);
  } else {
    const et = getEtParts(now);
    date = { year: et.year, month: et.month, day: et.day };
  }

  return {
    open: etWallClockToUtcMs(date.year, date.month, date.day, 9, 30),
    close: etWallClockToUtcMs(date.year, date.month, date.day, 16, 0),
  };
}
