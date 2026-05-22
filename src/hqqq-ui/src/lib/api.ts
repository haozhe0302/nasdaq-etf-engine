import * as signalR from "@microsoft/signalr";

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly detail: string,
  ) {
    super(detail);
    this.name = "ApiError";
  }
}

async function get(path: string): Promise<unknown> {
  const res = await fetch(`${BASE_URL}${path}`);
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new ApiError(res.status, body?.message ?? `HTTP ${res.status}`);
  }
  return res.json();
}

export function fetchQuote(): Promise<unknown> {
  return get("/api/quote");
}

export function fetchConstituents(): Promise<unknown> {
  return get("/api/constituents");
}

export function fetchSystemHealth(): Promise<unknown> {
  return get("/api/system/health");
}

export function fetchHistory(range: string): Promise<unknown> {
  return get(`/api/history?range=${encodeURIComponent(range)}`);
}

export function createMarketHubConnection(): signalR.HubConnection {
  // Default transport order is WebSockets -> ServerSentEvents -> LongPolling,
  // which already prefers WebSockets with graceful fallback. We do NOT set
  // skipNegotiation so the client can negotiate and fall back if needed.
  //
  // withCredentials: false is critical for our Static Web Apps -> Gateway CORS
  // setup: the app does not require cookies/auth and many CORS policies reject
  // credentialed preflights against wildcard or multi-origin allow lists.
  return new signalR.HubConnectionBuilder()
    .withUrl(`${BASE_URL}/hubs/market`, {
      withCredentials: false,
    })
    .withAutomaticReconnect([0, 1_000, 2_000, 5_000, 10_000, 30_000])
    .configureLogging(signalR.LogLevel.Information)
    .build();
}
