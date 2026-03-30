const styles = {
  healthy: { dot: "bg-positive", text: "text-positive" },
  degraded: { dot: "bg-yellow-500", text: "text-yellow-500" },
  unhealthy: { dot: "bg-negative", text: "text-negative" },
  unknown: { dot: "bg-muted", text: "text-muted" },
} as const;

interface StatusBadgeProps {
  status: keyof typeof styles;
  label?: string;
}

export function StatusBadge({ status, label }: StatusBadgeProps) {
  const s = styles[status];
  return (
    <span className={`inline-flex items-center gap-1.5 text-xs ${s.text}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${s.dot}`} />
      {label ?? status}
    </span>
  );
}
