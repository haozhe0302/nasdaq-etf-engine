import type { ReactNode } from "react";

interface PanelProps {
  title?: string;
  className?: string;
  children: ReactNode;
}

export function Panel({ title, className, children }: PanelProps) {
  return (
    <div className={`rounded border border-edge bg-surface ${className ?? ""}`}>
      {title && (
        <div className="border-b border-edge px-3 py-1.5 text-xs font-medium text-muted">
          {title}
        </div>
      )}
      {children}
    </div>
  );
}
