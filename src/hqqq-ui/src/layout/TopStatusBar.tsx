import { useAppStatus, useEstClock } from "@/lib/hooks";
import { formatInterval } from "@/lib/updateTracker";
import { StatusBadge } from "@/components/StatusBadge";

export function TopStatusBar() {
  const s = useAppStatus();
  const estTime = useEstClock();

  return (
    <header className="flex h-9 shrink-0 items-center justify-between border-b border-edge bg-surface px-4 text-xs">
      <div className="flex items-center gap-3">
        <span className="font-mono font-bold tracking-wide text-accent">
          HQQQ
        </span>
        <span className="text-muted">iNAV Engine</span>
        <span className="text-edge">│</span>
        <span className="text-muted">{s.symbolCount} symbols</span>
        <span className="text-edge">│</span>
        <span className="font-mono text-muted">
          {estTime} EST
        </span>
      </div>
      <div className="flex items-center gap-4">
        <span className="rounded bg-accent/15 px-1.5 py-0.5 font-mono text-[11px] text-accent">
          {s.mode.toUpperCase()}
        </span>
        <span className="font-mono text-muted">↻ {formatInterval(s.updateIntervalMs)}</span>
        <StatusBadge status={s.overallHealth} label="System" />
      </div>
    </header>
  );
}
