using Hqqq.Persistence.Options;
using Microsoft.Extensions.Configuration;

namespace Hqqq.Persistence.Tests.Configuration;

public class PersistenceOptionsBindingTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var opts = new PersistenceOptions();

        Assert.True(opts.SchemaBootstrapOnStart);
        Assert.Equal(128, opts.SnapshotWriteBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(500), opts.SnapshotFlushInterval);
        Assert.Equal(2048, opts.SnapshotChannelCapacity);

        Assert.Equal(256, opts.RawTickWriteBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(500), opts.RawTickFlushInterval);
        Assert.Equal(8192, opts.RawTickChannelCapacity);

        Assert.Equal(TimeSpan.FromDays(30), opts.RawTickRetention);
        Assert.Equal(TimeSpan.FromDays(365), opts.QuoteSnapshotRetention);
        Assert.Equal(TimeSpan.FromDays(730), opts.RollupRetention);
    }

    [Fact]
    public void BindsFromPersistenceSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:SchemaBootstrapOnStart"] = "false",
                ["Persistence:SnapshotWriteBatchSize"] = "256",
                ["Persistence:SnapshotFlushInterval"] = "00:00:01",
                ["Persistence:SnapshotChannelCapacity"] = "4096",
                ["Persistence:RawTickWriteBatchSize"] = "512",
                ["Persistence:RawTickFlushInterval"] = "00:00:00.250",
                ["Persistence:RawTickChannelCapacity"] = "16384",
                ["Persistence:RawTickRetention"] = "7.00:00:00",
                ["Persistence:QuoteSnapshotRetention"] = "90.00:00:00",
                ["Persistence:RollupRetention"] = "180.00:00:00",
            })
            .Build();

        var opts = new PersistenceOptions();
        config.GetSection("Persistence").Bind(opts);

        Assert.False(opts.SchemaBootstrapOnStart);
        Assert.Equal(256, opts.SnapshotWriteBatchSize);
        Assert.Equal(TimeSpan.FromSeconds(1), opts.SnapshotFlushInterval);
        Assert.Equal(4096, opts.SnapshotChannelCapacity);

        Assert.Equal(512, opts.RawTickWriteBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(250), opts.RawTickFlushInterval);
        Assert.Equal(16384, opts.RawTickChannelCapacity);

        Assert.Equal(TimeSpan.FromDays(7), opts.RawTickRetention);
        Assert.Equal(TimeSpan.FromDays(90), opts.QuoteSnapshotRetention);
        Assert.Equal(TimeSpan.FromDays(180), opts.RollupRetention);
    }

    [Fact]
    public void MissingSection_KeepsDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var opts = new PersistenceOptions();
        config.GetSection("Persistence").Bind(opts);

        Assert.True(opts.SchemaBootstrapOnStart);
        Assert.Equal(128, opts.SnapshotWriteBatchSize);
        Assert.Equal(256, opts.RawTickWriteBatchSize);
        Assert.Equal(TimeSpan.FromDays(30), opts.RawTickRetention);
    }
}
