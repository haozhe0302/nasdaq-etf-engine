import { useState, useEffect } from "react";

export function TopStatusBar() {
  const [now, setNow] = useState(new Date());

  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(id);
  }, []);

  return (
    <header className="flex h-9 shrink-0 items-center justify-between border-b border-edge bg-surface px-4 text-xs">
      <div className="flex items-center gap-3">
        <span className="font-mono font-bold tracking-wide text-accent">
          HQQQ
        </span>
        <span className="text-muted">iNAV Engine</span>
        <span className="text-edge">│</span>
        <span className="text-muted">101 symbols</span>
        <span className="text-edge">│</span>
        <span className="font-mono text-muted">
          {now.toISOString().slice(11, 19)} UTC
        </span>
      </div>
      <div className="flex items-center gap-4">
        <span className="flex items-center gap-1.5 text-positive">
          <span className="h-1.5 w-1.5 rounded-full bg-positive" />
          Market Open
        </span>
        <span className="flex items-center gap-1.5 text-muted">
          <span className="h-1.5 w-1.5 rounded-full bg-positive" />
          API
        </span>
      </div>
    </header>
  );
}
