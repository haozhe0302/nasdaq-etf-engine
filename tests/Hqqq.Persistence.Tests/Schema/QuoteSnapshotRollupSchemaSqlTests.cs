using Hqqq.Persistence.Schema;

namespace Hqqq.Persistence.Tests.Schema;

public class QuoteSnapshotRollupSchemaSqlTests
{
    [Fact]
    public void OneMinuteView_IsIdempotentContinuousAggregate()
    {
        Assert.Contains(
            "CREATE MATERIALIZED VIEW IF NOT EXISTS quote_snapshots_1m",
            QuoteSnapshotRollupSchemaSql.CreateOneMinuteView);
        Assert.Contains(
            "WITH (timescaledb.continuous)",
            QuoteSnapshotRollupSchemaSql.CreateOneMinuteView);
        Assert.Contains(
            "WITH NO DATA",
            QuoteSnapshotRollupSchemaSql.CreateOneMinuteView);
    }

    [Fact]
    public void FiveMinuteView_IsIdempotentContinuousAggregate()
    {
        Assert.Contains(
            "CREATE MATERIALIZED VIEW IF NOT EXISTS quote_snapshots_5m",
            QuoteSnapshotRollupSchemaSql.CreateFiveMinuteView);
        Assert.Contains(
            "WITH (timescaledb.continuous)",
            QuoteSnapshotRollupSchemaSql.CreateFiveMinuteView);
    }

    [Fact]
    public void OneMinuteView_BucketsOnOneMinute()
    {
        Assert.Contains(
            "time_bucket('1 minute', ts)",
            QuoteSnapshotRollupSchemaSql.CreateOneMinuteView);
    }

    [Fact]
    public void FiveMinuteView_BucketsOnFiveMinutes()
    {
        Assert.Contains(
            "time_bucket('5 minutes', ts)",
            QuoteSnapshotRollupSchemaSql.CreateFiveMinuteView);
    }

    [Theory]
    [InlineData("basket_id")]
    [InlineData("last(nav, ts)")]
    [InlineData("last(market_proxy_price, ts)")]
    [InlineData("last(premium_discount_pct, ts)")]
    [InlineData("count(*)")]
    [InlineData("avg(max_component_age_ms)")]
    [InlineData("sum(stale_count)")]
    [InlineData("sum(fresh_count)")]
    public void OneMinuteView_ProjectsRequiredFields(string fragment)
    {
        Assert.Contains(fragment, QuoteSnapshotRollupSchemaSql.CreateOneMinuteView);
    }

    [Theory]
    [InlineData("basket_id")]
    [InlineData("last(nav, ts)")]
    [InlineData("last(market_proxy_price, ts)")]
    [InlineData("last(premium_discount_pct, ts)")]
    [InlineData("count(*)")]
    [InlineData("avg(max_component_age_ms)")]
    public void FiveMinuteView_ProjectsRequiredFields(string fragment)
    {
        Assert.Contains(fragment, QuoteSnapshotRollupSchemaSql.CreateFiveMinuteView);
    }

    [Fact]
    public void OneMinutePolicy_IsWrappedForIdempotency()
    {
        Assert.Contains(
            "add_continuous_aggregate_policy(",
            QuoteSnapshotRollupSchemaSql.AddOneMinutePolicy);
        Assert.Contains(
            "'quote_snapshots_1m'",
            QuoteSnapshotRollupSchemaSql.AddOneMinutePolicy);
        Assert.Contains("DO $$", QuoteSnapshotRollupSchemaSql.AddOneMinutePolicy);
        Assert.Contains(
            "EXCEPTION WHEN duplicate_object",
            QuoteSnapshotRollupSchemaSql.AddOneMinutePolicy);
    }

    [Fact]
    public void FiveMinutePolicy_IsWrappedForIdempotency()
    {
        Assert.Contains(
            "add_continuous_aggregate_policy(",
            QuoteSnapshotRollupSchemaSql.AddFiveMinutePolicy);
        Assert.Contains(
            "'quote_snapshots_5m'",
            QuoteSnapshotRollupSchemaSql.AddFiveMinutePolicy);
        Assert.Contains("DO $$", QuoteSnapshotRollupSchemaSql.AddFiveMinutePolicy);
        Assert.Contains(
            "EXCEPTION WHEN duplicate_object",
            QuoteSnapshotRollupSchemaSql.AddFiveMinutePolicy);
    }

    [Fact]
    public void Statements_RunViewsFirstThenPolicies()
    {
        Assert.Equal(4, QuoteSnapshotRollupSchemaSql.Statements.Count);
        Assert.Same(
            QuoteSnapshotRollupSchemaSql.CreateOneMinuteView,
            QuoteSnapshotRollupSchemaSql.Statements[0]);
        Assert.Same(
            QuoteSnapshotRollupSchemaSql.CreateFiveMinuteView,
            QuoteSnapshotRollupSchemaSql.Statements[1]);
        Assert.Same(
            QuoteSnapshotRollupSchemaSql.AddOneMinutePolicy,
            QuoteSnapshotRollupSchemaSql.Statements[2]);
        Assert.Same(
            QuoteSnapshotRollupSchemaSql.AddFiveMinutePolicy,
            QuoteSnapshotRollupSchemaSql.Statements[3]);
    }

    [Fact]
    public void RollupViews_ExposesBothNamesInOrder()
    {
        Assert.Equal(
            new[] { "quote_snapshots_1m", "quote_snapshots_5m" },
            QuoteSnapshotRollupSchemaSql.RollupViews);
    }
}
