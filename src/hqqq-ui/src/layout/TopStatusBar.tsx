export function TopStatusBar() {
  return (
    <header className="flex h-9 shrink-0 items-center justify-between border-b border-edge bg-surface px-4 text-xs">
      <div className="flex items-center gap-2">
        <span className="font-mono font-bold text-accent tracking-wide">
          HQQQ
        </span>
        <span className="text-muted">iNAV Engine</span>
      </div>
      <div className="flex items-center gap-1.5 text-muted">
        <span className="h-1.5 w-1.5 rounded-full bg-positive" />
        <span>Connected</span>
      </div>
    </header>
  );
}
