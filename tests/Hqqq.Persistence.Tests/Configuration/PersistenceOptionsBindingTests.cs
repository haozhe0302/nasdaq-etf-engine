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
            })
            .Build();

        var opts = new PersistenceOptions();
        config.GetSection("Persistence").Bind(opts);

        Assert.False(opts.SchemaBootstrapOnStart);
        Assert.Equal(256, opts.SnapshotWriteBatchSize);
        Assert.Equal(TimeSpan.FromSeconds(1), opts.SnapshotFlushInterval);
        Assert.Equal(4096, opts.SnapshotChannelCapacity);
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
    }
}
