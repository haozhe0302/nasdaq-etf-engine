using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.MarketData.Contracts;
using Hqqq.Api.Modules.MarketData.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Tests.MarketData;

public class TiingoWebSocketDiagnosticsTests
{
    private static TiingoWebSocketClient CreateClient(TiingoOptions? opts = null)
    {
        opts ??= new TiingoOptions();
        var store = new StubPriceStore();
        return new TiingoWebSocketClient(
            Options.Create(opts),
            store,
            NullLogger<TiingoWebSocketClient>.Instance);
    }


    [Fact]
    public void ThresholdLevel_DefaultsTo6()
    {
        var opts = new TiingoOptions();
        Assert.Equal(6, opts.WebSocketThresholdLevel);
    }

    [Fact]
    public void ThresholdLevel_Configurable()
    {
        var opts = new TiingoOptions { WebSocketThresholdLevel = 3 };
        Assert.Equal(3, opts.WebSocketThresholdLevel);
    }


    [Fact]
    public void RestPollingInterval_DefaultsTo2()
    {
        var opts = new TiingoOptions();
        Assert.Equal(2, opts.RestPollingIntervalSeconds);
    }


    [Fact]
    public void HistoryRecordInterval_DefaultsTo15s()
    {
        var opts = new PricingOptions();
        Assert.Equal(15_000, opts.HistoryRecordIntervalMs);
    }

    [Fact]
    public void SeriesRecordInterval_StaysAt5s()
    {
        var opts = new PricingOptions();
        Assert.Equal(5_000, opts.SeriesRecordIntervalMs);
    }


    [Fact]
    public void ProcessMessage_E_CapturesErrorFields()
    {
        var client = CreateClient();

        var json = """
        {
            "messageType": "E",
            "response": { "code": 400, "message": "thresholdLevel not valid for your subscription tier" }
        }
        """;

        client.ProcessMessage(json);

        Assert.Equal("thresholdLevel not valid for your subscription tier", client.LastUpstreamError);
        Assert.Equal(400, client.LastUpstreamErrorCode);
        Assert.NotNull(client.LastUpstreamErrorAtUtc);
        Assert.True((DateTimeOffset.UtcNow - client.LastUpstreamErrorAtUtc!.Value).TotalSeconds < 5);
    }

    [Fact]
    public void ProcessMessage_E_FallsBackToDataField()
    {
        var client = CreateClient();

        var json = """
        {
            "messageType": "E",
            "data": "Some error occurred"
        }
        """;

        client.ProcessMessage(json);

        Assert.Equal("Some error occurred", client.LastUpstreamError);
        Assert.Null(client.LastUpstreamErrorCode);
    }

    [Fact]
    public void ProcessMessage_E_FallsBackToUnknown()
    {
        var client = CreateClient();

        var json = """{ "messageType": "E" }""";

        client.ProcessMessage(json);

        Assert.Equal("Unknown upstream error", client.LastUpstreamError);
    }

    [Fact]
    public void ProcessMessage_E_OverwritesPreviousError()
    {
        var client = CreateClient();

        client.ProcessMessage("""
            { "messageType": "E", "response": { "code": 400, "message": "first error" } }
        """);
        client.ProcessMessage("""
            { "messageType": "E", "response": { "code": 500, "message": "second error" } }
        """);

        Assert.Equal("second error", client.LastUpstreamError);
        Assert.Equal(500, client.LastUpstreamErrorCode);
    }

    [Fact]
    public void ProcessMessage_H_UpdatesHeartbeat()
    {
        var client = CreateClient();
        var before = client.LastHeartbeatUtc;

        client.ProcessMessage("""{ "messageType": "H" }""");

        Assert.True(client.LastHeartbeatUtc > before);
    }

    [Fact]
    public void ProcessMessage_I_UpdatesHeartbeat()
    {
        var client = CreateClient();
        var before = client.LastHeartbeatUtc;

        client.ProcessMessage("""{ "messageType": "I", "data": {"subscriptionId":123} }""");

        Assert.True(client.LastHeartbeatUtc > before);
    }

    [Fact]
    public void ProcessMessage_A_UpdatesHeartbeatAndStore()
    {
        var store = new StubPriceStore();
        var client = new TiingoWebSocketClient(
            Options.Create(new TiingoOptions()),
            store,
            NullLogger<TiingoWebSocketClient>.Instance);

        var before = client.LastHeartbeatUtc;

        client.ProcessMessage("""
        {
            "messageType": "A",
            "data": ["T", "2026-04-06T14:00:00Z", "2026-04-06T14:00:00Z", "AAPL",
                     100, 180.50, 180.55, 180.60, 200, 180.55, 50, "2026-04-06T14:00:00Z"]
        }
        """);

        Assert.True(client.LastHeartbeatUtc > before);
        Assert.True(store.UpdateCount > 0);
        Assert.Equal("AAPL", store.LastTick?.Symbol);
    }

    [Fact]
    public void ProcessMessage_UnknownType_NoEffect()
    {
        var client = CreateClient();
        var before = client.LastHeartbeatUtc;

        client.ProcessMessage("""{ "messageType": "X", "data": {} }""");

        Assert.Equal(before, client.LastHeartbeatUtc);
        Assert.Null(client.LastUpstreamError);
    }

    [Fact]
    public void ProcessMessage_MalformedJson_NoException()
    {
        var client = CreateClient();

        client.ProcessMessage("not valid json {{{");

        Assert.Null(client.LastUpstreamError);
    }


    [Fact]
    public void IMarketDataIngestionService_ExposesUpstreamErrorProperties()
    {
        var iface = typeof(IMarketDataIngestionService);
        Assert.NotNull(iface.GetProperty("LastUpstreamError"));
        Assert.NotNull(iface.GetProperty("LastUpstreamErrorCode"));
        Assert.NotNull(iface.GetProperty("LastUpstreamErrorAtUtc"));
    }

    private sealed class StubPriceStore : ILatestPriceStore
    {
        public int UpdateCount { get; private set; }
        public PriceTick? LastTick { get; private set; }

        public void Update(PriceTick tick)
        {
            UpdateCount++;
            LastTick = tick;
        }

        public LatestPriceState? Get(string symbol) => null;
        public IReadOnlyDictionary<string, LatestPriceState> GetAll() =>
            new Dictionary<string, LatestPriceState>();
        public IReadOnlyList<LatestPriceState> GetLatest(IEnumerable<string> symbols) => [];
        public FeedHealthSnapshot GetHealthSnapshot() => new()
        {
            WebSocketConnected = false,
            FallbackActive = false,
            SymbolsTracked = 0,
            SymbolsWithPrice = 0,
            StaleSymbolCount = 0,
            AsOfUtc = DateTimeOffset.UtcNow,
            ActiveSymbolCount = 0,
            PendingSymbolCount = 0,
            ActiveWithPriceCount = 0,
            PendingWithPriceCount = 0,
            StaleActiveCount = 0,
            StalePendingCount = 0,
            ActiveCoveragePct = 0,
            PendingCoveragePct = 0,
            IsPendingBasketReady = false,
        };
        public void SetTrackedSymbols(IReadOnlyDictionary<string, SymbolRole> symbolRoles) { }
        public IReadOnlyDictionary<string, SymbolRole> GetTrackedSymbols() =>
            new Dictionary<string, SymbolRole>();
    }
}
