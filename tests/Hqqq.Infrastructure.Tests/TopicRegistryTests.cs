using Hqqq.Infrastructure.Kafka;

namespace Hqqq.Infrastructure.Tests;

public class TopicRegistryTests
{
    [Fact]
    public void AllTopics_ContainsExpectedCount()
    {
        Assert.Equal(6, KafkaTopicRegistry.All.Count);
    }

    [Fact]
    public void AllTopics_HaveUniqueNames()
    {
        var names = KafkaTopicRegistry.All.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [InlineData("market.raw_ticks.v1", false)]
    [InlineData("market.latest_by_symbol.v1", true)]
    [InlineData("refdata.basket.active.v1", true)]
    [InlineData("refdata.basket.events.v1", false)]
    [InlineData("pricing.snapshots.v1", false)]
    [InlineData("ops.incidents.v1", false)]
    public void TopicCompactionPolicy_MatchesExpected(string topicName, bool expectedCompacted)
    {
        var topic = KafkaTopicRegistry.All.Single(t => t.Name == topicName);
        Assert.Equal(expectedCompacted, topic.Compacted);
    }

    [Fact]
    public void TopicConstants_MatchRegistryEntries()
    {
        var registryNames = KafkaTopicRegistry.All.Select(t => t.Name).ToHashSet();

        Assert.Contains(KafkaTopics.RawTicks, registryNames);
        Assert.Contains(KafkaTopics.LatestBySymbol, registryNames);
        Assert.Contains(KafkaTopics.BasketActive, registryNames);
        Assert.Contains(KafkaTopics.BasketEvents, registryNames);
        Assert.Contains(KafkaTopics.PricingSnapshots, registryNames);
        Assert.Contains(KafkaTopics.Incidents, registryNames);
    }

    [Fact]
    public void AllTopics_HaveVersionSuffix()
    {
        foreach (var topic in KafkaTopicRegistry.All)
        {
            Assert.EndsWith(".v1", topic.Name);
        }
    }
}
