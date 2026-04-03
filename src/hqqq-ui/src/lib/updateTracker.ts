const EMA_ALPHA = 0.3;
const STALE_THRESHOLD_MS = 30_000;

interface FeedState {
  lastUpdateAt: number;
  emaMs: number;
}

const feeds = new Map<string, FeedState>();

export function recordUpdate(feedName: string): void {
  const now = Date.now();
  const existing = feeds.get(feedName);

  if (existing) {
    const interval = now - existing.lastUpdateAt;
    existing.emaMs = existing.emaMs > 0
      ? EMA_ALPHA * interval + (1 - EMA_ALPHA) * existing.emaMs
      : interval;
    existing.lastUpdateAt = now;
  } else {
    feeds.set(feedName, { lastUpdateAt: now, emaMs: 0 });
  }
}

export function unregisterFeed(feedName: string): void {
  feeds.delete(feedName);
}

export function getMinIntervalMs(): number {
  const now = Date.now();
  let min = Infinity;
  for (const state of feeds.values()) {
    if (state.emaMs > 0 && now - state.lastUpdateAt < STALE_THRESHOLD_MS) {
      min = Math.min(min, state.emaMs);
    }
  }
  return min === Infinity ? 0 : Math.round(min);
}

export function formatInterval(ms: number): string {
  if (ms <= 0) return "—";
  if (ms < 1_000) return `${ms}ms`;
  if (ms < 60_000) return `${(ms / 1_000).toFixed(1)}s`;
  return `${Math.floor(ms / 60_000)}m`;
}
