using System.Net;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Sources;

/// <summary>
/// The composite is the main credibility story of the service: live first,
/// fall back to the deterministic seed on any unavailability / invalidity,
/// and never surface a misleading "active basket" to downstream.
/// </summary>
public class CompositeHoldingsSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsLiveSnapshot_WhenLiveIsOkAndValid()
    {
        var path = WriteTemp(SampleLiveJson());
        try
        {
            var composite = BuildComposite(new LiveHoldingsOptions
            {
                SourceType = HoldingsSourceType.File,
                FilePath = path,
            }, validationMin: 1);

            var result = await composite.FetchAsync(CancellationToken.None);

            Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
            Assert.Equal("live:file", result.Snapshot!.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FetchAsync_FallsBackToSeed_WhenLiveIsUnavailable()
    {
        var composite = BuildComposite(new LiveHoldingsOptions
        {
            SourceType = HoldingsSourceType.None,
        });

        var result = await composite.FetchAsync(CancellationToken.None);

        Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
        Assert.Equal(BasketSeedLoader.SourceTag, result.Snapshot!.Source);
    }

    [Fact]
    public async Task FetchAsync_FallsBackToSeed_WhenLiveIsInvalid()
    {
        var path = WriteTemp("{ not-json");
        try
        {
            var composite = BuildComposite(new LiveHoldingsOptions
            {
                SourceType = HoldingsSourceType.File,
                FilePath = path,
            });

            var result = await composite.FetchAsync(CancellationToken.None);

            Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
            Assert.Equal(BasketSeedLoader.SourceTag, result.Snapshot!.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FetchAsync_FallsBackToSeed_WhenLiveFailsValidation()
    {
        // Live returns OK but with a single constituent — validator blocks
        // because MinConstituents=50, so composite must fall back to seed
        // rather than publish a 1-name basket.
        var path = WriteTemp(SampleLiveJson(oneConstituentOnly: true));
        try
        {
            var composite = BuildComposite(
                new LiveHoldingsOptions { SourceType = HoldingsSourceType.File, FilePath = path },
                validationMin: 50);

            var result = await composite.FetchAsync(CancellationToken.None);

            Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
            Assert.Equal(BasketSeedLoader.SourceTag, result.Snapshot!.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CompositeHoldingsSource BuildComposite(
        LiveHoldingsOptions live,
        int validationMin = 50,
        int validationMax = 150)
    {
        var options = Options.Create(new ReferenceDataOptions
        {
            LiveHoldings = live,
            Validation = new ValidationOptions
            {
                Strict = true,
                MinConstituents = validationMin,
                MaxConstituents = validationMax,
            },
        });

        var factory = new SingleHandlerHttpClientFactory(
            new ConstHandler(HttpStatusCode.NotFound, ""));
        var liveSource = new LiveHoldingsSource(factory, options, NullLogger<LiveHoldingsSource>.Instance);
        var loader = new BasketSeedLoader(
            Options.Create(new ReferenceDataOptions()),
            NullLogger<BasketSeedLoader>.Instance);
        var fallback = new FallbackSeedHoldingsSource(loader, NullLogger<FallbackSeedHoldingsSource>.Instance);
        var validator = new HoldingsValidator(options);

        return new CompositeHoldingsSource(liveSource, fallback, validator,
            NullLogger<CompositeHoldingsSource>.Instance);
    }

    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hqqq-live-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string SampleLiveJson(bool oneConstituentOnly = false)
    {
        var constituents = oneConstituentOnly
            ? """[ { "symbol": "AAPL", "name": "Apple", "sector": "Technology", "sharesHeld": 100, "referencePrice": 215.30 } ]"""
            : """
              [
                { "symbol": "AAPL", "name": "Apple", "sector": "Technology", "sharesHeld": 100, "referencePrice": 215.30 },
                { "symbol": "MSFT", "name": "Microsoft", "sector": "Technology", "sharesHeld": 100, "referencePrice": 432.10 }
              ]
              """;

        return $$"""
        {
          "basketId": "HQQQ",
          "version": "v-live",
          "asOfDate": "2026-04-15",
          "scaleFactor": 1.0,
          "constituents": {{constituents}}
        }
        """;
    }

    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleHandlerHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class ConstHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public ConstHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }
}
