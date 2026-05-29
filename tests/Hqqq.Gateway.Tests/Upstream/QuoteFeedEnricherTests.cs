using Hqqq.Contracts.Dtos;
using Hqqq.Gateway.Services.MarketSession;
using Hqqq.Gateway.Services.Upstream;

namespace Hqqq.Gateway.Tests.Upstream;

public class QuoteFeedEnricherTests
{
    // April 16 2026 is a Thursday; EDT is UTC-4.
    // 09:30 ET = 13:30 UTC, 16:00 ET = 20:00 UTC.
    private static readonly DateTimeOffset RegularSessionUtc = new(2026, 4, 16, 14, 0, 0, TimeSpan.Zero);  // 10:00 ET
    private static readonly DateTimeOffset PreOpenUtc = new(2026, 4, 16, 13, 27, 0, TimeSpan.Zero);       // 09:27 ET
    private static readonly DateTimeOffset ClosedUtc = new(2026, 4, 16, 21, 0, 0, TimeSpan.Zero);          // 17:00 ET

    private static FeedInfoDto EnginePlaceholderFeeds() => new()
    {
        WebSocketConnected = false,
        FallbackActive = false,
        PricingActive = true,
        BasketState = "active",
        PendingActivationBlocked = false,
    };

    private sealed class FakeUpstream : IIngressUpstreamState
    {
        private readonly bool _has;
        private readonly bool _ws;
        private readonly bool _fb;

        public FakeUpstream(bool has, bool ws = false, bool fb = false)
        {
            _has = has;
            _ws = ws;
            _fb = fb;
        }

        public bool TryGet(out bool webSocketConnected, out bool fallbackActive)
        {
            webSocketConnected = _ws;
            fallbackActive = _fb;
            return _has;
        }
    }

    private static QuoteFeedEnricher Build(IIngressUpstreamState upstream, DateTimeOffset now) =>
        new(upstream, new RegularSessionClock(), () => now);

    [Fact]
    public void Patch_RegularSession_WithLiveWebSocket_MarksConnectedAndRegularOpen()
    {
        var enricher = Build(new FakeUpstream(has: true, ws: true), RegularSessionUtc);

        var feeds = enricher.Patch(EnginePlaceholderFeeds());

        Assert.True(feeds.WebSocketConnected);
        Assert.False(feeds.FallbackActive);
        Assert.Equal("regular_open", feeds.MarketSessionState);
        Assert.True(feeds.IsRegularSessionOpen);
    }

    [Fact]
    public void Patch_RegularSession_WithFallbackActive_MarksFallback()
    {
        var enricher = Build(new FakeUpstream(has: true, ws: false, fb: true), RegularSessionUtc);

        var feeds = enricher.Patch(EnginePlaceholderFeeds());

        Assert.False(feeds.WebSocketConnected);
        Assert.True(feeds.FallbackActive);
        Assert.Equal("regular_open", feeds.MarketSessionState);
    }

    [Fact]
    public void Patch_StaleUpstream_LeavesTransportFlagsUnchanged()
    {
        // No fresh ingress reading → pass engine flags through untouched,
        // but still annotate the session phase.
        var enricher = Build(new FakeUpstream(has: false), RegularSessionUtc);

        var feeds = enricher.Patch(EnginePlaceholderFeeds());

        Assert.False(feeds.WebSocketConnected);
        Assert.False(feeds.FallbackActive);
        Assert.Equal("regular_open", feeds.MarketSessionState);
    }

    [Fact]
    public void Patch_OutsideSession_MarksClosed()
    {
        var enricher = Build(new FakeUpstream(has: true, ws: true), ClosedUtc);

        var feeds = enricher.Patch(EnginePlaceholderFeeds());

        Assert.Equal("closed", feeds.MarketSessionState);
        Assert.Equal("Closed", feeds.SessionLabel);
        Assert.False(feeds.IsRegularSessionOpen);
        // Transport flags are still overlaid with the live reading.
        Assert.True(feeds.WebSocketConnected);
    }

    [Fact]
    public void Patch_PreOpenWindow_MarksPreOpen()
    {
        var enricher = Build(new FakeUpstream(has: true, ws: true), PreOpenUtc);

        var feeds = enricher.Patch(EnginePlaceholderFeeds());

        Assert.Equal("pre_open", feeds.MarketSessionState);
        Assert.Equal("Pre-open", feeds.SessionLabel);
        Assert.False(feeds.IsRegularSessionOpen);
    }
}
