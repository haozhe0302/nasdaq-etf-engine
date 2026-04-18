using Hqqq.Persistence.Persistence;
using Npgsql;

namespace Hqqq.Persistence.Tests.Persistence;

public class QuoteSnapshotSqlCommandsTests
{
    [Fact]
    public void InsertSql_TargetsQuoteSnapshotsTable()
    {
        Assert.Contains("INSERT INTO quote_snapshots", QuoteSnapshotSqlCommands.InsertSql);
    }

    [Fact]
    public void InsertSql_UsesReplaySafeConflictClause()
    {
        // DO NOTHING — not DO UPDATE — preserves the first inserted_at_utc
        // on replay. The conflict target matches the UNIQUE constraint
        // declared by the schema bootstrapper.
        Assert.Contains(
            "ON CONFLICT (basket_id, ts) DO NOTHING",
            QuoteSnapshotSqlCommands.InsertSql);
        Assert.DoesNotContain("DO UPDATE", QuoteSnapshotSqlCommands.InsertSql);
    }

    [Fact]
    public void InsertSql_MentionsEveryInsertColumn()
    {
        foreach (var column in QuoteSnapshotSqlCommands.InsertColumns)
        {
            Assert.Contains(column, QuoteSnapshotSqlCommands.InsertSql);
        }
    }

    [Fact]
    public void InsertColumns_DoNotIncludeInsertedAtUtc()
    {
        // inserted_at_utc is populated by the table DEFAULT on first insert
        // and preserved by DO NOTHING on replay — never written by the app.
        Assert.DoesNotContain("inserted_at_utc", QuoteSnapshotSqlCommands.InsertColumns);
    }

    [Fact]
    public void BindRow_AttachesOneParameterPerInsertColumn()
    {
        using var command = new NpgsqlCommand(QuoteSnapshotSqlCommands.InsertSql);
        var row = new QuoteSnapshotRow
        {
            BasketId = "HQQQ",
            Ts = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
            Nav = 600m,
            MarketProxyPrice = 500m,
            PremiumDiscountPct = -16.6667m,
            StaleCount = 1,
            FreshCount = 47,
            MaxComponentAgeMs = 42d,
            QuoteQuality = "live",
        };

        QuoteSnapshotSqlCommands.BindRow(command, row);

        Assert.Equal(
            QuoteSnapshotSqlCommands.InsertColumns.Count,
            command.Parameters.Count);

        foreach (var column in QuoteSnapshotSqlCommands.InsertColumns)
        {
            Assert.True(
                command.Parameters.Contains(column),
                $"expected parameter @{column} to be bound");
        }

        Assert.Equal("HQQQ", command.Parameters["basket_id"].Value);
        Assert.Equal(row.Ts.UtcDateTime, command.Parameters["ts"].Value);
        Assert.Equal("live", command.Parameters["quote_quality"].Value);
    }

    [Fact]
    public void BindRow_ReplacesPreviouslyBoundParameters()
    {
        using var command = new NpgsqlCommand(QuoteSnapshotSqlCommands.InsertSql);
        var first = new QuoteSnapshotRow
        {
            BasketId = "FIRST",
            Ts = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
            Nav = 1m,
            MarketProxyPrice = 1m,
            PremiumDiscountPct = 0m,
            StaleCount = 0,
            FreshCount = 0,
            MaxComponentAgeMs = 0d,
            QuoteQuality = "live",
        };
        var second = first with { BasketId = "SECOND" };

        QuoteSnapshotSqlCommands.BindRow(command, first);
        QuoteSnapshotSqlCommands.BindRow(command, second);

        Assert.Equal(
            QuoteSnapshotSqlCommands.InsertColumns.Count,
            command.Parameters.Count);
        Assert.Equal("SECOND", command.Parameters["basket_id"].Value);
    }
}
