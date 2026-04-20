namespace Hqqq.Ingress.Configuration;

/// <summary>
/// Tiingo provider settings, bound to the "Tiingo" configuration section.
/// </summary>
/// <remarks>
/// <para>
/// The websocket URL property is named <see cref="WsUrl"/> (not
/// <c>WebSocketUrl</c>) so it aligns with both the docker-compose env
/// (<c>Tiingo__WsUrl</c>) and the <see cref="Hqqq.Infrastructure.Hosting.LegacyConfigShim"/>
/// mapping for <c>TIINGO_WS_URL</c>. Without that alignment,
/// flat-key fallback would silently miss the websocket URL.
/// </para>
/// <para>
/// In <see cref="Hqqq.Infrastructure.Hosting.OperatingMode.Standalone"/>
/// mode, <see cref="ApiKey"/> is required and a missing/placeholder value
/// causes ingress to fail fast at startup. In
/// <see cref="Hqqq.Infrastructure.Hosting.OperatingMode.Hybrid"/> mode
/// the API key is ignored entirely (the legacy monolith bridges ticks).
/// </para>
/// </remarks>
public sealed class TiingoOptions
{
    /// <summary>Tiingo personal API key (required in standalone mode).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Tiingo IEX websocket URL.</summary>
    public string WsUrl { get; set; } = "wss://api.tiingo.com/iex";

    /// <summary>Tiingo IEX REST base URL (used for the warm-up snapshot in standalone).</summary>
    public string RestBaseUrl { get; set; } = "https://api.tiingo.com/iex";

    /// <summary>Initial reconnect backoff after the websocket drops (seconds).</summary>
    public int ReconnectBaseDelaySeconds { get; set; } = 5;

    /// <summary>Maximum reconnect backoff cap (seconds).</summary>
    public int MaxReconnectDelaySeconds { get; set; } = 60;

    /// <summary>REST polling interval (reserved for the future REST fallback path).</summary>
    public int RestPollingIntervalSeconds { get; set; } = 15;

    /// <summary>Number of seconds without a tick that classifies the upstream as stale (health probe).</summary>
    public int StaleAfterSeconds { get; set; } = 60;

    /// <summary>
    /// Tiingo IEX threshold level. Default <c>6</c> matches the legacy
    /// monolith and the free-tier-friendly trade-only feed.
    /// </summary>
    public int WebSocketThresholdLevel { get; set; } = 6;

    /// <summary>
    /// Comma-separated upstream subscription. When empty, ingress falls
    /// back to a default symbol list (top-weight HQQQ holdings) so a
    /// minimal standalone deploy works without extra config.
    /// </summary>
    public string Symbols { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, ingress fetches a one-shot REST snapshot for
    /// every subscribed symbol on startup so consumers (notably
    /// quote-engine) see a baseline price before the first websocket
    /// tick arrives. Disable in tests.
    /// </summary>
    public bool SnapshotOnStartup { get; set; } = true;

    /// <summary>
    /// Returns the resolved subscription symbol set. Falls back to a
    /// deterministic top-25 default that matches the standalone
    /// reference-data seed when <see cref="Symbols"/> is empty.
    /// </summary>
    public IReadOnlyList<string> ResolveSymbols()
    {
        if (!string.IsNullOrWhiteSpace(Symbols))
        {
            return Symbols
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return DefaultSymbols;
    }

    /// <summary>
    /// Default subscription set used when <see cref="Symbols"/> is empty.
    /// Mirrors the standalone basket seed in <c>hqqq-reference-data</c>
    /// so a vanilla deploy publishes ticks for every constituent.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSymbols = new[]
    {
        "AAPL", "MSFT", "NVDA", "AMZN", "META", "GOOGL", "GOOG", "TSLA",
        "AVGO", "COST", "NFLX", "AMD",  "PEP",  "ADBE", "CSCO", "TMUS",
        "INTC", "CMCSA","QCOM", "AMGN", "TXN",  "HON",  "INTU", "ISRG",
        "BKNG",
    };
}
