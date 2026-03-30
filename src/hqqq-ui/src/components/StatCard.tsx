interface StatCardProps {
  label: string;
  value: string;
  sub?: string;
  status?: "positive" | "negative" | "neutral";
}

export function StatCard({
  label,
  value,
  sub,
  status = "neutral",
}: StatCardProps) {
  const color =
    status === "positive"
      ? "text-positive"
      : status === "negative"
        ? "text-negative"
        : "text-content";

  return (
    <div className="rounded border border-edge bg-surface px-3 py-2">
      <div className="text-[11px] text-muted">{label}</div>
      <div className={`mt-0.5 font-mono text-lg font-semibold leading-tight ${color}`}>
        {value}
      </div>
      {sub && <div className="mt-0.5 text-[11px] text-muted">{sub}</div>}
    </div>
  );
}
