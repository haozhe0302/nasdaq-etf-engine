using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hqqq.Bench;

public sealed record BenchmarkReport
{
    // ── Session ──────────────────────────────────────────

    public required DateTimeOffset SessionStart { get; init; }
    public required DateTimeOffset SessionEnd { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int TickCount { get; init; }
    public required int QuoteCount { get; init; }
    public required int TransportEventCount { get; init; }
    public required int ActivationCount { get; init; }
    public required int SymbolsCovered { get; init; }

    // ── Latency ──────────────────────────────────────────

    public required double TickToQuoteP50Ms { get; init; }
    public required double TickToQuoteP95Ms { get; init; }
    public required double TickToQuoteP99Ms { get; init; }
    public required double BroadcastP50Ms { get; init; }
    public required double BroadcastP95Ms { get; init; }

    // ── Failover ─────────────────────────────────────────

    public required int FallbackActivationCount { get; init; }
    [JsonIgnore]
    public double[] RecoveryDurationsSeconds { get; init; } = [];
    public double? MaxRecoverySeconds { get; init; }
    public double? P95RecoverySeconds { get; init; }

    // ── Freshness ────────────────────────────────────────

    public required double AvgStaleSymbolRatio { get; init; }
    public required double MaxStaleSymbolRatio { get; init; }
    public required double PctQuotesWithStale { get; init; }

    // ── Tracking / basis ─────────────────────────────────

    public required double BasisRmseBps { get; init; }
    public required double MaxAbsBasisBps { get; init; }
    public required double AvgAbsBasisBps { get; init; }

    // ── Serialization ────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TimeSpanConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HQQQ Benchmark Report");
        sb.AppendLine();
        sb.AppendLine("## Session");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Start | {SessionStart:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine($"| End | {SessionEnd:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine($"| Duration | {Duration:hh\\:mm\\:ss} |");
        sb.AppendLine($"| Ticks recorded | {TickCount:N0} |");
        sb.AppendLine($"| Quotes emitted | {QuoteCount:N0} |");
        sb.AppendLine($"| Transport events | {TransportEventCount:N0} |");
        sb.AppendLine($"| Basket activations | {ActivationCount:N0} |");
        sb.AppendLine($"| Symbols covered | {SymbolsCovered:N0} |");
        sb.AppendLine();

        sb.AppendLine("## Latency");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Tick→Quote p50 | {TickToQuoteP50Ms:F1} ms |");
        sb.AppendLine($"| Tick→Quote p95 | {TickToQuoteP95Ms:F1} ms |");
        sb.AppendLine($"| Tick→Quote p99 | {TickToQuoteP99Ms:F1} ms |");
        sb.AppendLine($"| Broadcast p50 | {BroadcastP50Ms:F2} ms |");
        sb.AppendLine($"| Broadcast p95 | {BroadcastP95Ms:F2} ms |");
        sb.AppendLine();

        sb.AppendLine("## Failover");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Fallback activations | {FallbackActivationCount} |");
        sb.AppendLine($"| Max recovery | {Fmt(MaxRecoverySeconds, "s")} |");
        sb.AppendLine($"| p95 recovery | {Fmt(P95RecoverySeconds, "s")} |");
        sb.AppendLine();

        sb.AppendLine("## Freshness");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Avg stale ratio | {AvgStaleSymbolRatio:P1} |");
        sb.AppendLine($"| Max stale ratio | {MaxStaleSymbolRatio:P1} |");
        sb.AppendLine($"| Quotes with stale | {PctQuotesWithStale:F1}% |");
        sb.AppendLine();

        sb.AppendLine("## Tracking / Basis vs QQQ");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Basis RMSE | {BasisRmseBps:F2} bps |");
        sb.AppendLine($"| Max abs basis | {MaxAbsBasisBps:F2} bps |");
        sb.AppendLine($"| Avg abs basis | {AvgAbsBasisBps:F2} bps |");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string Fmt(double? v, string unit) =>
        v is not null ? $"{v:F2} {unit}" : "—";

    private sealed class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => TimeSpan.Parse(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, TimeSpan value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(@"hh\:mm\:ss"));
    }
}
