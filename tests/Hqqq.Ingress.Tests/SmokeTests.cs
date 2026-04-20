using Hqqq.Ingress.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests;

public class SmokeTests
{
    [Fact]
    public void TiingoOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tiingo:ApiKey"] = "test-key",
                ["Tiingo:WsUrl"] = "wss://test.example.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<TiingoOptions>(config.GetSection("Tiingo"));
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<TiingoOptions>>().Value;
        Assert.Equal("test-key", opts.ApiKey);
        Assert.Equal("wss://test.example.com", opts.WsUrl);
    }

    [Fact]
    public void TiingoOptions_HasSensibleDefaults()
    {
        var opts = new TiingoOptions();
        Assert.Null(opts.ApiKey);
        Assert.Equal("wss://api.tiingo.com/iex", opts.WsUrl);
        Assert.Equal("https://api.tiingo.com/iex", opts.RestBaseUrl);
        Assert.Equal(6, opts.WebSocketThresholdLevel);
        Assert.True(opts.SnapshotOnStartup);
    }
}
