using Hqqq.Contracts.Dtos;
using Hqqq.Gateway.Services.MarketSession;

namespace Hqqq.Gateway.Services.Upstream;

/// <summary>
/// Overlays gateway-known signals onto the quote-engine's
/// <see cref="FeedInfoDto"/> before it is served on the REST snapshot and
/// SignalR delta. The quote-engine cannot observe the ingress transport
/// (it only consumes Kafka ticks), so it ships conservative placeholder
/// flags; the gateway, which already polls ingress health, fills in the
/// real WebSocket / REST-fallback state and the current market-session
/// phase so the Market page "Market Data" tile reflects reality.
/// </summary>
public interface IQuoteFeedEnricher
{
    FeedInfoDto Patch(FeedInfoDto feeds);
}

public sealed class QuoteFeedEnricher : IQuoteFeedEnricher
{
    private readonly IIngressUpstreamState _upstream;
    private readonly RegularSessionClock _session;
    private readonly Func<DateTimeOffset> _utcNow;

    public QuoteFeedEnricher(IIngressUpstreamState upstream, RegularSessionClock session)
        : this(upstream, session, static () => DateTimeOffset.UtcNow)
    {
    }

    public QuoteFeedEnricher(
        IIngressUpstreamState upstream,
        RegularSessionClock session,
        Func<DateTimeOffset> utcNow)
    {
        _upstream = upstream;
        _session = session;
        _utcNow = utcNow;
    }

    public FeedInfoDto Patch(FeedInfoDto feeds)
    {
        if (feeds is null) return feeds!;

        var now = _utcNow();
        var regularOpen = _session.IsRegularSessionPoint(now);
        var preOpen = _session.IsPreOpenResetWindow(now);

        // Frontend buildFeeds() treats any marketSessionState other than
        // "regular_open" as a (healthy) labelled session and only consults
        // the WS / fallback flags during the regular session. Keeping the
        // "regular_open" token exact preserves that contract.
        var sessionState = regularOpen ? "regular_open" : preOpen ? "pre_open" : "closed";
        var sessionLabel = regularOpen ? "Regular session" : preOpen ? "Pre-open" : "Closed";

        var ws = feeds.WebSocketConnected;
        var fallback = feeds.FallbackActive;
        if (_upstream.TryGet(out var liveWs, out var liveFallback))
        {
            ws = liveWs;
            fallback = liveFallback;
        }

        return feeds with
        {
            WebSocketConnected = ws,
            FallbackActive = fallback,
            MarketSessionState = sessionState,
            IsRegularSessionOpen = regularOpen,
            SessionLabel = sessionLabel,
        };
    }
}

/// <summary>
/// No-op enricher used when upstream wiring is not applicable (non-Redis
/// quote sources) or in tests. Returns feeds unchanged.
/// </summary>
public sealed class NullQuoteFeedEnricher : IQuoteFeedEnricher
{
    public static readonly NullQuoteFeedEnricher Instance = new();

    public FeedInfoDto Patch(FeedInfoDto feeds) => feeds;
}
