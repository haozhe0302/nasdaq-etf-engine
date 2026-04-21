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
/// <see cref="ApiKey"/> is required and a missing/placeholder value causes
/// ingress to fail fast at startup. Phase 2 ingress has a single
/// self-sufficient runtime path — there is no "hybrid" mode that accepts a
/// missing key.
/// </para>
/// </remarks>
public sealed class TiingoOptions
{
    /// <summary>Tiingo personal API key. Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Tiingo IEX websocket URL.</summary>
    public string WsUrl { get; set; } = "wss://api.tiingo.com/iex";

    /// <summary>Tiingo IEX REST base URL (used for the warm-up snapshot).</summary>
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
    /// Optional comma/semicolon-separated symbol override. Only used as a
    /// bootstrap fallback when the basket topic has not delivered its
    /// first <c>BasketActiveStateV1</c> event within
    /// <see cref="IngressBasketOptions.StartupWaitSeconds"/>, or for
    /// isolated tests that want to subscribe without spinning up
    /// reference-data. Leave empty in normal Phase 2 runtimes — ingress
    /// derives the active universe from <c>refdata.basket.active.v1</c>.
    /// </summary>
    public string Symbols { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, ingress fetches a one-shot REST snapshot for
    /// every subscribed symbol as soon as the first basket arrives so
    /// consumers (notably quote-engine) see a baseline price before the
    /// first websocket tick. Disable in tests.
    /// </summary>
    public bool SnapshotOnStartup { get; set; } = true;

    /// <summary>
    /// Returns the optional startup-override symbol set parsed from
    /// <see cref="Symbols"/>. Returns an empty collection when no
    /// override is configured (the normal case — symbols come from the
    /// basket topic).
    /// </summary>
    public IReadOnlyList<string> ResolveOverrideSymbols()
    {
        if (string.IsNullOrWhiteSpace(Symbols)) return Array.Empty<string>();

        return Symbols
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
