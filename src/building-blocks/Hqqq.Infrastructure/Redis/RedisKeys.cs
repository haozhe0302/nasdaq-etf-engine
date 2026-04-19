namespace Hqqq.Infrastructure.Redis;

/// <summary>
/// Central registry of Redis key patterns and channel names.
/// Builder methods produce fully-qualified keys from parameterized patterns.
/// </summary>
public static class RedisKeys
{
    // ── Key patterns ────────────────────────────────────────────

    /// <summary>Latest iNAV snapshot hash. Pattern: <c>hqqq:snapshot:{basketId}</c>.</summary>
    public const string SnapshotPattern = "hqqq:snapshot:{0}";

    /// <summary>Latest constituents snapshot. Pattern: <c>hqqq:constituents:{basketId}</c>.</summary>
    public const string ConstituentsPattern = "hqqq:constituents:{0}";

    /// <summary>Latest price per symbol. Pattern: <c>hqqq:latest:{symbol}</c>.</summary>
    public const string LatestQuotePattern = "hqqq:latest:{0}";

    /// <summary>Active basket version metadata. Pattern: <c>hqqq:basket:active:{basketId}</c>.</summary>
    public const string ActiveBasketPattern = "hqqq:basket:active:{0}";

    /// <summary>Tick freshness summary. Pattern: <c>hqqq:freshness:{basketId}</c>.</summary>
    public const string FreshnessPattern = "hqqq:freshness:{0}";

    // ── Channels ────────────────────────────────────────────────

    /// <summary>Redis pub/sub channel for snapshot-update notifications to the gateway.</summary>
    public const string SnapshotChannel = "hqqq:channel:snapshot";

    /// <summary>
    /// Phase 2D2: Redis pub/sub channel carrying slim
    /// <c>QuoteUpdateEnvelope</c> JSON payloads from the quote-engine to every
    /// gateway instance for SignalR fan-out on <c>/hubs/market</c>.
    /// </summary>
    public const string QuoteUpdateChannel = "hqqq:channel:quote-update";

    // ── Key builders ────────────────────────────────────────────

    public static string Snapshot(string basketId) => string.Format(SnapshotPattern, basketId);
    public static string Constituents(string basketId) => string.Format(ConstituentsPattern, basketId);
    public static string LatestQuote(string symbol) => string.Format(LatestQuotePattern, symbol);
    public static string ActiveBasket(string basketId) => string.Format(ActiveBasketPattern, basketId);
    public static string Freshness(string basketId) => string.Format(FreshnessPattern, basketId);
}
