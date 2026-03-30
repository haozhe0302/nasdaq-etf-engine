import { Panel } from "@/components/Panel";
import { MetricRow } from "@/components/MetricRow";

const holdings = [
  { symbol: "AAPL", name: "Apple Inc.", weight: 8.92, shares: 7_710_000, price: 213.07, change: 1.24, sector: "Technology" },
  { symbol: "MSFT", name: "Microsoft Corp.", weight: 8.43, shares: 3_320_000, price: 467.56, change: -0.31, sector: "Technology" },
  { symbol: "NVDA", name: "NVIDIA Corp.", weight: 7.81, shares: 1_615_000, price: 891.12, change: 2.87, sector: "Technology" },
  { symbol: "AMZN", name: "Amazon.com Inc.", weight: 5.47, shares: 4_478_000, price: 225.01, change: 0.45, sector: "Consumer Disc." },
  { symbol: "META", name: "Meta Platforms", weight: 4.89, shares: 1_441_000, price: 624.91, change: -1.12, sector: "Technology" },
  { symbol: "GOOG", name: "Alphabet Inc. A", weight: 3.21, shares: 3_262_000, price: 181.23, change: 0.65, sector: "Technology" },
  { symbol: "GOOGL", name: "Alphabet Inc. C", weight: 2.98, shares: 3_053_000, price: 179.84, change: 0.62, sector: "Technology" },
  { symbol: "AVGO", name: "Broadcom Inc.", weight: 2.74, shares: 289_000, price: 1748.92, change: -0.85, sector: "Technology" },
  { symbol: "COST", name: "Costco Wholesale", weight: 2.48, shares: 485_000, price: 942.31, change: 0.18, sector: "Consumer Staples" },
  { symbol: "TSLA", name: "Tesla Inc.", weight: 2.31, shares: 1_712_000, price: 248.42, change: -2.14, sector: "Consumer Disc." },
];

export function ConstituentsPage() {
  return (
    <div className="space-y-3">
      {/* toolbar */}
      <div className="flex items-center gap-3 rounded border border-edge bg-surface px-3 py-2 text-xs">
        <input
          readOnly
          placeholder="Search symbol or name…"
          className="w-52 rounded border border-edge bg-canvas px-2 py-1 text-content placeholder:text-muted focus:outline-none"
        />
        <span className="text-edge">│</span>
        <span className="text-muted">Sector:</span>
        <span className="rounded bg-accent/10 px-2 py-0.5 text-accent">All</span>
        <span className="text-edge">│</span>
        <span className="text-muted">Sort: Weight ↓</span>
        <span className="ml-auto text-muted">
          Showing <span className="text-content">10</span> of{" "}
          <span className="text-content">101</span> constituents &middot; As of{" "}
          <span className="font-mono text-content">2026-03-28</span>
        </span>
      </div>

      <div className="grid grid-cols-4 gap-3">
        {/* holdings table */}
        <Panel className="col-span-3 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="border-b border-edge text-[11px] text-muted">
                  <th className="px-3 py-2 font-medium">#</th>
                  <th className="px-3 py-2 font-medium">Symbol</th>
                  <th className="px-3 py-2 font-medium">Name</th>
                  <th className="px-3 py-2 font-medium">Sector</th>
                  <th className="px-3 py-2 font-medium text-right">Weight</th>
                  <th className="px-3 py-2 font-medium text-right">Shares</th>
                  <th className="px-3 py-2 font-medium text-right">Price</th>
                  <th className="px-3 py-2 font-medium text-right">Chg %</th>
                  <th className="px-3 py-2 font-medium text-right">Mkt Value</th>
                </tr>
              </thead>
              <tbody>
                {holdings.map((h, i) => (
                  <tr key={h.symbol} className="border-b border-edge/30 hover:bg-overlay">
                    <td className="px-3 py-1.5 text-muted">{i + 1}</td>
                    <td className="px-3 py-1.5 font-mono font-medium text-accent">{h.symbol}</td>
                    <td className="px-3 py-1.5 text-muted">{h.name}</td>
                    <td className="px-3 py-1.5 text-muted">{h.sector}</td>
                    <td className="px-3 py-1.5 text-right font-mono">{h.weight.toFixed(2)}%</td>
                    <td className="px-3 py-1.5 text-right font-mono text-muted">
                      {h.shares.toLocaleString()}
                    </td>
                    <td className="px-3 py-1.5 text-right font-mono">${h.price.toFixed(2)}</td>
                    <td
                      className={`px-3 py-1.5 text-right font-mono ${h.change >= 0 ? "text-positive" : "text-negative"}`}
                    >
                      {h.change >= 0 ? "+" : ""}
                      {h.change.toFixed(2)}%
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

        {/* insight sidebar */}
        <div className="flex flex-col gap-3">
          <Panel title="Concentration">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Top 5 weight" value="35.52%" />
              <MetricRow label="Top 10 weight" value="49.24%" />
              <MetricRow label="Top 20 weight" value="68.91%" />
              <MetricRow label="Sectors" value="7" />
              <MetricRow label="HHI index" value="0.041" />
            </div>
          </Panel>

          <Panel title="Top Weights">
            <div className="space-y-2.5 p-3">
              {holdings.slice(0, 5).map((h) => (
                <div key={h.symbol} className="text-xs">
                  <div className="mb-0.5 flex justify-between">
                    <span className="font-mono text-accent">{h.symbol}</span>
                    <span className="font-mono">{h.weight.toFixed(2)}%</span>
                  </div>
                  <div className="h-1 rounded bg-edge">
                    <div
                      className="h-1 rounded bg-accent"
                      style={{ width: `${(h.weight / 10) * 100}%` }}
                    />
                  </div>
                </div>
              ))}
            </div>
          </Panel>

          <Panel title="Data Quality">
            <div className="space-y-0.5 p-3">
              <MetricRow label="Stale prices" value={<span className="text-positive">0</span>} />
              <MetricRow label="Missing symbols" value={<span className="text-positive">0</span>} />
              <MetricRow label="Last refresh" value="< 1s ago" />
              <MetricRow label="Coverage" value="101 / 101" />
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}
