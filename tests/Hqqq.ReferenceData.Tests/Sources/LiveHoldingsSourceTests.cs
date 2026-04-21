using System.Net;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Sources;

/// <summary>
/// <see cref="LiveHoldingsSource"/> must never throw — it always reports
/// back through the <see cref="HoldingsFetchResult"/> so
/// <see cref="CompositeHoldingsSource"/> can fall back cleanly. We cover
/// the three configured <c>SourceType</c>s (None / File / Http) and each
/// failure mode produces the expected status + a human-readable reason.
/// </summary>
public class LiveHoldingsSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsUnavailable_WhenSourceTypeIsNone()
    {
        var source = BuildSource(new LiveHoldingsOptions { SourceType = HoldingsSourceType.None });
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Equal(HoldingsFetchStatus.Unavailable, result.Status);
        Assert.Contains("None", result.Reason);
    }

    [Fact]
    public async Task FetchAsync_File_ReturnsUnavailable_WhenPathMissing()
    {
        var source = BuildSource(new LiveHoldingsOptions
        {
            SourceType = HoldingsSourceType.File,
            FilePath = null,
        });
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Equal(HoldingsFetchStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task FetchAsync_File_ReturnsUnavailable_WhenFileDoesNotExist()
    {
        var source = BuildSource(new LiveHoldingsOptions
        {
            SourceType = HoldingsSourceType.File,
            FilePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"),
        });
        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Equal(HoldingsFetchStatus.Unavailable, result.Status);
        Assert.Contains("file not found", result.Reason);
    }

    [Fact]
    public async Task FetchAsync_File_ReturnsOk_WhenValid()
    {
        var path = WriteTemp("""
        {
          "basketId": "HQQQ",
          "version": "v-live",
          "asOfDate": "2026-04-15",
          "scaleFactor": 1.0,
          "constituents": [
            { "symbol": "AAPL", "name": "Apple", "sector": "Technology", "sharesHeld": 100, "referencePrice": 215.30 },
            { "symbol": "MSFT", "name": "Microsoft", "sector": "Technology", "sharesHeld": 100, "referencePrice": 432.10 }
          ]
        }
        """);
        try
        {
            var source = BuildSource(new LiveHoldingsOptions
            {
                SourceType = HoldingsSourceType.File,
                FilePath = path,
            });
            var result = await source.FetchAsync(CancellationToken.None);

            Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
            Assert.NotNull(result.Snapshot);
            Assert.Equal("live:file", result.Snapshot!.Source);
            Assert.Equal(2, result.Snapshot.Constituents.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FetchAsync_File_ReturnsInvalid_WhenJsonMalformed()
    {
        var path = WriteTemp("{ not-json: true,");
        try
        {
            var source = BuildSource(new LiveHoldingsOptions
            {
                SourceType = HoldingsSourceType.File,
                FilePath = path,
            });
            var result = await source.FetchAsync(CancellationToken.None);
            Assert.Equal(HoldingsFetchStatus.Invalid, result.Status);
            Assert.Contains("malformed", result.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FetchAsync_File_ReturnsInvalid_WhenAsOfDateBad()
    {
        var path = WriteTemp("""
        {
          "basketId": "HQQQ",
          "version": "v-live",
          "asOfDate": "not-a-date",
          "scaleFactor": 1.0,
          "constituents": [
            { "symbol": "AAPL", "name": "Apple", "sector": "Technology", "sharesHeld": 100, "referencePrice": 215.30 }
          ]
        }
        """);
        try
        {
            var source = BuildSource(new LiveHoldingsOptions
            {
                SourceType = HoldingsSourceType.File,
                FilePath = path,
            });
            var result = await source.FetchAsync(CancellationToken.None);
            Assert.Equal(HoldingsFetchStatus.Invalid, result.Status);
            Assert.Contains("asOfDate", result.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FetchAsync_Http_ReturnsOk_OnValidResponse()
    {
        var body = """
        {
          "basketId": "HQQQ",
          "version": "v-live",
          "asOfDate": "2026-04-15",
          "scaleFactor": 1.0,
          "constituents": [
            { "symbol": "AAPL", "name": "Apple", "sector": "Technology", "sharesHeld": 100, "referencePrice": 215.30 }
          ]
        }
        """;
        var source = BuildSourceWithHttp(
            new LiveHoldingsOptions { SourceType = HoldingsSourceType.Http, HttpUrl = "https://example.test/holdings.json" },
            new StubHandler(HttpStatusCode.OK, body));

        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Equal(HoldingsFetchStatus.Ok, result.Status);
        Assert.Equal("live:http", result.Snapshot!.Source);
    }

    [Fact]
    public async Task FetchAsync_Http_ReturnsUnavailable_OnNon2xx()
    {
        var source = BuildSourceWithHttp(
            new LiveHoldingsOptions { SourceType = HoldingsSourceType.Http, HttpUrl = "https://example.test/holdings.json" },
            new StubHandler(HttpStatusCode.InternalServerError, "{}"));

        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Equal(HoldingsFetchStatus.Unavailable, result.Status);
        Assert.Contains("500", result.Reason);
    }

    [Fact]
    public async Task FetchAsync_Http_ReturnsInvalid_OnMalformedBody()
    {
        var source = BuildSourceWithHttp(
            new LiveHoldingsOptions { SourceType = HoldingsSourceType.Http, HttpUrl = "https://example.test/holdings.json" },
            new StubHandler(HttpStatusCode.OK, "{ bad json"));

        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Equal(HoldingsFetchStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task FetchAsync_Http_ReturnsUnavailable_WhenUrlMissing()
    {
        var source = BuildSourceWithHttp(
            new LiveHoldingsOptions { SourceType = HoldingsSourceType.Http, HttpUrl = null },
            new StubHandler(HttpStatusCode.OK, "{}"));

        var result = await source.FetchAsync(CancellationToken.None);
        Assert.Equal(HoldingsFetchStatus.Unavailable, result.Status);
    }

    private static LiveHoldingsSource BuildSource(LiveHoldingsOptions options)
    {
        var factory = new StubHttpClientFactory(new StubHandler(HttpStatusCode.NotFound, ""));
        return new LiveHoldingsSource(
            factory,
            Options.Create(new ReferenceDataOptions { LiveHoldings = options }),
            NullLogger<LiveHoldingsSource>.Instance);
    }

    private static LiveHoldingsSource BuildSourceWithHttp(LiveHoldingsOptions options, StubHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        return new LiveHoldingsSource(
            factory,
            Options.Create(new ReferenceDataOptions { LiveHoldings = options }),
            NullLogger<LiveHoldingsSource>.Instance);
    }

    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hqqq-live-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly StubHandler _handler;
        public StubHttpClientFactory(StubHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body),
            });
        }
    }
}
