using System.Net;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
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

    [Fact]
    public async Task FetchAsync_Production_RealSource_NoOverride_RefusesSeedFallback()
    {
        // Production + RealSource + AllowDeterministicSeedInProduction=false
        // (the default): primaries exhausted → composite must return
        // Unavailable instead of quietly serving the seed. Readiness
        // stays Degraded; the orchestrator can take the pod out of
        // rotation instead of shipping a seed basket as "active".
        var composite = BuildCompositeWithPosture(
            live: new LiveHoldingsOptions { SourceType = HoldingsSourceType.None },
            environment: "Production",
            basket: new BasketOptions
            {
                Mode = BasketMode.RealSource,
                AllowDeterministicSeedInProduction = false,
            });

        var result = await composite.FetchAsync(CancellationToken.None);

        Assert.Equal(HoldingsFetchStatus.Unavailable, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Contains("production seed-fallback refused", result.Reason);
    }

    [Fact]
    public async Task FetchAsync_Production_RealSource_WithSeedOverride_ServesSeed()
    {
        // Operator explicitly opted in to the seed posture in Production
        // via AllowDeterministicSeedInProduction=true. Composite must
        // fall through to the seed just like outside Production.
        var composite = BuildCompositeWithPosture(
            live: new LiveHoldingsOptions { SourceType = HoldingsSourceType.None },
            environment: "Production",
            basket: new BasketOptions
            {
                Mode = BasketMode.RealSource,
                AllowDeterministicSeedInProduction = true,
            });

        var result = await composite.FetchAsync(CancellationToken.None);

        Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
        Assert.Equal(BasketSeedLoader.SourceTag, result.Snapshot!.Source);
    }

    [Fact]
    public async Task FetchAsync_Development_RealSource_StillFallsBackToSeed()
    {
        // The Production guard is environment-gated. Development always
        // keeps the historic "live → seed" fallback so local bring-up
        // stays friction-free.
        var composite = BuildCompositeWithPosture(
            live: new LiveHoldingsOptions { SourceType = HoldingsSourceType.None },
            environment: "Development",
            basket: new BasketOptions
            {
                Mode = BasketMode.RealSource,
                AllowDeterministicSeedInProduction = false,
            });

        var result = await composite.FetchAsync(CancellationToken.None);

        Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
        Assert.Equal(BasketSeedLoader.SourceTag, result.Snapshot!.Source);
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

    private static CompositeHoldingsSource BuildCompositeWithPosture(
        LiveHoldingsOptions live,
        string environment,
        BasketOptions basket)
    {
        var options = Options.Create(new ReferenceDataOptions
        {
            LiveHoldings = live,
            Validation = new ValidationOptions
            {
                Strict = true,
                MinConstituents = 1,
                MaxConstituents = 500,
            },
            Basket = basket,
        });

        var factory = new SingleHandlerHttpClientFactory(
            new ConstHandler(HttpStatusCode.NotFound, string.Empty));
        var liveSource = new LiveHoldingsSource(factory, options, NullLogger<LiveHoldingsSource>.Instance);
        var loader = new BasketSeedLoader(
            Options.Create(new ReferenceDataOptions()),
            NullLogger<BasketSeedLoader>.Instance);
        var fallback = new FallbackSeedHoldingsSource(loader, NullLogger<FallbackSeedHoldingsSource>.Instance);
        var validator = new HoldingsValidator(options);

        return new CompositeHoldingsSource(
            new IHoldingsSource[] { liveSource },
            fallback,
            validator,
            options,
            new StubEnvironment(environment),
            NullLogger<CompositeHoldingsSource>.Instance);
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public StubEnvironment(string name) { EnvironmentName = name; }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "hqqq-reference-data-tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
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
