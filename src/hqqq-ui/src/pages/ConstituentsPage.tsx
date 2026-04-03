import { useState, useEffect, useMemo } from "react";
import { useConstituentData } from "@/lib/hooks";
import { Panel } from "@/components/Panel";
import { MetricRow } from "@/components/MetricRow";
import type { Constituent } from "@/lib/types";

type SortField = "weight" | "symbol" | "name" | "shares" | "price" | "changePct" | "mktValue";
type SortDir = "asc" | "desc";

const SORT_OPTIONS: { value: SortField; label: string }[] = [
  { value: "weight", label: "Weight" },
  { value: "symbol", label: "Symbol" },
  { value: "name", label: "Name" },
  { value: "shares", label: "Shares" },
  { value: "price", label: "Price" },
  { value: "changePct", label: "Chg %" },
  { value: "mktValue", label: "Mkt Value" },
];

function formatElapsed(ms: number): string {
  if (ms <= 0) return "—";
  const seconds = Math.floor(ms / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${seconds % 60}s ago`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m ago`;
}

function compareConstituents(a: Constituent, b: Constituent, field: SortField): number {
  switch (field) {
    case "symbol": return a.symbol.localeCompare(b.symbol);
    case "name": return a.name.localeCompare(b.name);
    case "weight": return a.weight - b.weight;
    case "shares": return a.shares - b.shares;
    case "price": return a.price - b.price;
    case "changePct": return a.changePct - b.changePct;
    case "mktValue": return (a.shares * a.price) - (b.shares * b.price);
  }
}

function ConnectionBanner({ connectionState, error }: { connectionState: string; error?: string }) {
  if (connectionState === "live") return null;
  const isConnecting = connectionState === "connecting";
  const cls = isConnecting
    ? "border-accent/30 bg-accent/10 text-accent"
    : "border-yellow-500/30 bg-yellow-500/10 text-yellow-400";
  return (
    <div className={`rounded border px-3 py-1.5 text-xs ${cls}`}>
      {isConnecting ? "Connecting to backend\u2026" : error ?? "Connection lost \u2014 showing last known data"}
    </div>
  );
}

export function ConstituentsPage() {
  const { data: d, connectionState, error } = useConstituentData();
  const [refreshAgo, setRefreshAgo] = useState("—");
  const [search, setSearch] = useState("");
  const [sortField, setSortField] = useState<SortField>("weight");
  const [sortDir, setSortDir] = useState<SortDir>("desc");

  useEffect(() => {
    if (!d.lastRefreshAt) return;
    const tick = () => setRefreshAgo(formatElapsed(Date.now() - d.lastRefreshAt));
    tick();
    const id = setInterval(tick, 1_000);
    return () => clearInterval(id);
  }, [d.lastRefreshAt]);

  const filtered = useMemo(() => {
    let result = d.holdings;
    if (search) {
      const q = search.toLowerCase();
      result = result.filter(
        (h) => h.symbol.toLowerCase().includes(q) || h.name.toLowerCase().includes(q),
      );
    }
    return [...result].sort((a, b) => {
      const cmp = compareConstituents(a, b, sortField);
      return sortDir === "desc" ? -cmp : cmp;
    });
  }, [d.holdings, search, sortField, sortDir]);

  const toggleSort = (field: SortField) => {
    if (sortField === field) {
      setSortDir((prev) => (prev === "desc" ? "asc" : "desc"));
    } else {
      setSortField(field);
      setSortDir(field === "symbol" || field === "name" ? "asc" : "desc");
    }
  };

  const SortArrow = ({ field }: { field: SortField }) =>
    sortField === field ? (
      <span className="ml-0.5 text-accent">{sortDir === "desc" ? "↓" : "↑"}</span>
    ) : null;

  return (
    <div className="flex h-full flex-col gap-3">
      <ConnectionBanner connectionState={connectionState} error={error} />

      <div className="flex shrink-0 items-center gap-3 rounded border border-edge bg-surface px-3 py-2 text-xs">
        <div className="relative">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search symbol or name…"
            className="w-52 rounded border border-edge bg-canvas px-2 py-1 pr-7 text-content placeholder:text-muted focus:border-accent/50 focus:outline-none"
          />
          {search && (
            <button
              onClick={() => setSearch("")}
              className="absolute right-1.5 top-1/2 -translate-y-1/2 text-muted hover:text-content"
            >
              ✕
            </button>
          )}
        </div>
        <span className="text-edge">│</span>
        <span className="text-muted">Sort:</span>
        <select
          value={sortField}
          onChange={(e) => {
            const f = e.target.value as SortField;
            setSortField(f);
            setSortDir(f === "symbol" || f === "name" ? "asc" : "desc");
          }}
          className="rounded border border-edge bg-canvas px-1.5 py-0.5 text-content focus:border-accent/50 focus:outline-none"
        >
          {SORT_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
        <button
          onClick={() => setSortDir((prev) => (prev === "desc" ? "asc" : "desc"))}
          className="rounded border border-edge bg-canvas px-1.5 py-0.5 text-accent hover:bg-accent/10"
        >
          {sortDir === "desc" ? "↓" : "↑"}
        </button>
        <span className="ml-auto text-muted">
          Showing <span className="text-content">{filtered.length}</span> of{" "}
          <span className="text-content">{d.totalCount}</span> constituents &middot; As of{" "}
          <span className="font-mono text-content">{d.asOfDate}</span>
        </span>
      </div>

      <div className="grid min-h-0 flex-1 grid-cols-4 gap-3">
        <Panel className="col-span-3 flex min-h-0 flex-col overflow-hidden">
          <div className="flex-1 overflow-y-auto">
            <table className="w-full text-left text-xs">
              <thead className="sticky top-0 z-10 bg-surface">
                <tr className="border-b border-edge text-[11px] text-muted">
                  <th className="px-3 py-2 font-medium">#</th>
                  <th className="cursor-pointer select-none px-3 py-2 font-medium hover:text-content" onClick={() => toggleSort("symbol")}>
                    Symbol<SortArrow field="symbol" />
                  </th>
                  <th className="cursor-pointer select-none px-3 py-2 font-medium hover:text-content" onClick={() => toggleSort("name")}>
                    Name<SortArrow field="name" />
                  </th>
                  <th className="cursor-pointer select-none px-3 py-2 font-medium text-right hover:text-content" onClick={() => toggleSort("weight")}>
                    Weight<SortArrow field="weight" />
                  </th>
                  <th className="cursor-pointer select-none px-3 py-2 font-medium text-right hover:text-content" onClick={() => toggleSort("shares")}>
                    Shares<SortArrow field="shares" />
                  </th>
                  <th className="cursor-pointer select-none px-3 py-2 font-medium text-right hover:text-content" onClick={() => toggleSort("price")}>
                    Price<SortArrow field="price" />
                  </th>
                  <th className="cursor-pointer select-none px-3 py-2 font-medium text-right hover:text-content" onClick={() => toggleSort("changePct")}>
                    Chg %<SortArrow field="changePct" />
                  </th>
                  <th className="cursor-pointer select-none px-3 py-2 font-medium text-right hover:text-content" onClick={() => toggleSort("mktValue")}>
                    Mkt Value<SortArrow field="mktValue" />
                  </th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((h, i) => (
                  <tr key={h.symbol} className="border-b border-edge/30 hover:bg-overlay">
                    <td className="px-3 py-1.5 text-muted">{i + 1}</td>
                    <td className="px-3 py-1.5 font-mono font-medium text-accent">{h.symbol}</td>
                    <td className="px-3 py-1.5 text-muted">{h.name}</td>
                    <td className="px-3 py-1.5 text-right font-mono">{h.weight.toFixed(2)}%</td>
                    <td className="px-3 py-1.5 text-right font-mono text-muted">{h.shares.toLocaleString()}</td>
                    <td className="px-3 py-1.5 text-right font-mono">${h.price.toFixed(2)}</td>
                    <td className={`px-3 py-1.5 text-right font-mono ${h.changePct >= 0 ? "text-positive" : "text-negative"}`}>
                      {h.changePct >= 0 ? "+" : ""}{h.changePct.toFixed(2)}%
                    </td>
                    <td className="px-3 py-1.5 text-right font-mono text-muted">
                      ${((h.shares * h.price) / 1e9).toFixed(2)}B
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>

        <div className="flex flex-col gap-3 self-start">
          <Panel title="Concentration">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Top 5 weight" value={`${d.concentration.top5}%`} />
              <MetricRow label="Top 10 weight" value={`${d.concentration.top10}%`} />
              <MetricRow label="Top 20 weight" value={`${d.concentration.top20}%`} />
              <MetricRow label="HHI index" value={String(d.concentration.hhi)} />
            </div>
          </Panel>

          <Panel title="Top Weights">
            <div className="space-y-2.5 p-3">
              {d.holdings.slice(0, 5).map((h) => (
                <div key={h.symbol} className="text-xs">
                  <div className="mb-0.5 flex justify-between">
                    <span className="font-mono text-accent">{h.symbol}</span>
                    <span className="font-mono">{h.weight.toFixed(2)}%</span>
                  </div>
                  <div className="h-1 rounded bg-edge">
                    <div className="h-1 rounded bg-accent" style={{ width: `${(h.weight / 10) * 100}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </Panel>

          <Panel title="Data Quality">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Stale prices" value={<span className="text-positive">{d.quality.stalePrices}</span>} />
              <MetricRow label="Missing symbols" value={<span className="text-positive">{d.quality.missingSymbols}</span>} />
              <MetricRow label="Last refresh" value={refreshAgo} />
              <MetricRow label="Coverage" value={`${d.quality.coverage} / ${d.quality.totalSymbols}`} />
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}
