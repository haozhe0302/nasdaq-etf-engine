using Hqqq.Persistence.Options;
using Hqqq.Persistence.Schema;

namespace Hqqq.Persistence.Tests.Schema;

public class RetentionPolicySchemaSqlTests
{
    [Fact]
    public void BuildTargets_EnumeratesEveryHypertableAndRollup()
    {
        var options = new PersistenceOptions
        {
            RawTickRetention = TimeSpan.FromDays(30),
            QuoteSnapshotRetention = TimeSpan.FromDays(365),
            RollupRetention = TimeSpan.FromDays(730),
        };

        var targets = RetentionPolicySchemaSql.BuildTargets(options);

        Assert.Equal(4, targets.Count);
        Assert.Equal("raw_ticks", targets[0].Relation);
        Assert.Equal(TimeSpan.FromDays(30), targets[0].Window);
        Assert.Equal("quote_snapshots", targets[1].Relation);
        Assert.Equal(TimeSpan.FromDays(365), targets[1].Window);
        Assert.Equal("quote_snapshots_1m", targets[2].Relation);
        Assert.Equal(TimeSpan.FromDays(730), targets[2].Window);
        Assert.Equal("quote_snapshots_5m", targets[3].Relation);
        Assert.Equal(TimeSpan.FromDays(730), targets[3].Window);
    }

    [Fact]
    public void BuildStatements_EmitsIdempotentAddRetentionPolicyPerTarget()
    {
        var options = new PersistenceOptions
        {
            RawTickRetention = TimeSpan.FromDays(30),
            QuoteSnapshotRetention = TimeSpan.FromDays(365),
            RollupRetention = TimeSpan.FromDays(730),
        };

        var statements = RetentionPolicySchemaSql.BuildStatements(options);

        Assert.Equal(4, statements.Count);
        foreach (var stmt in statements)
        {
            Assert.Contains("add_retention_policy", stmt);
            Assert.Contains("if_not_exists => TRUE", stmt);
        }

        Assert.Contains("'raw_ticks'", statements[0]);
        Assert.Contains("INTERVAL '30 days'", statements[0]);
        Assert.Contains("'quote_snapshots'", statements[1]);
        Assert.Contains("INTERVAL '365 days'", statements[1]);
        Assert.Contains("'quote_snapshots_1m'", statements[2]);
        Assert.Contains("INTERVAL '730 days'", statements[2]);
        Assert.Contains("'quote_snapshots_5m'", statements[3]);
        Assert.Contains("INTERVAL '730 days'", statements[3]);
    }

    [Theory]
    [InlineData(30, 0, 0, 0, "30 days")]
    [InlineData(1, 0, 0, 0, "1 days")]
    [InlineData(0, 6, 0, 0, "6 hours")]
    [InlineData(0, 0, 15, 0, "15 minutes")]
    [InlineData(0, 0, 0, 30, "30 seconds")]
    public void FormatInterval_UsesLargestWholeUnit(
        int days, int hours, int minutes, int seconds, string expected)
    {
        var window = new TimeSpan(days, hours, minutes, seconds);

        var formatted = RetentionPolicySchemaSql.FormatInterval(window);

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void FormatInterval_FallsBackToHoursForNonDayMultiples()
    {
        var window = TimeSpan.FromHours(36);

        var formatted = RetentionPolicySchemaSql.FormatInterval(window);

        Assert.Equal("36 hours", formatted);
    }

    [Fact]
    public void FormatInterval_RejectsZeroOrNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RetentionPolicySchemaSql.FormatInterval(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RetentionPolicySchemaSql.FormatInterval(TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void BuildStatement_RejectsEmptyRelation()
    {
        var target = new RetentionPolicySchemaSql.RetentionTarget("   ", TimeSpan.FromDays(1));

        Assert.Throws<ArgumentException>(() => RetentionPolicySchemaSql.BuildStatement(target));
    }

    [Fact]
    public void BuildStatement_RejectsZeroOrNegativeWindow()
    {
        var target = new RetentionPolicySchemaSql.RetentionTarget("raw_ticks", TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => RetentionPolicySchemaSql.BuildStatement(target));
    }

    [Fact]
    public void BuildStatement_ProducesCompleteSelect()
    {
        var target = new RetentionPolicySchemaSql.RetentionTarget("raw_ticks", TimeSpan.FromDays(7));

        var sql = RetentionPolicySchemaSql.BuildStatement(target);

        Assert.Equal(
            "SELECT add_retention_policy('raw_ticks', INTERVAL '7 days', if_not_exists => TRUE);",
            sql);
    }
}
