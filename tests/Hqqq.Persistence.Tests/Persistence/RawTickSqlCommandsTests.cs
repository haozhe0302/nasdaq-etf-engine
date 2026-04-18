using Hqqq.Persistence.Persistence;
using Npgsql;

namespace Hqqq.Persistence.Tests.Persistence;

public class RawTickSqlCommandsTests
{
    private static RawTickRow BuildRow(decimal? bid = 899.5m, decimal? ask = 900.5m) => new()
    {
        Symbol = "NVDA",
        ProviderTimestamp = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
        IngressTimestamp = new DateTimeOffset(2026, 4, 16, 13, 30, 0, 50, TimeSpan.Zero),
        Last = 900m,
        Bid = bid,
        Ask = ask,
        Currency = "USD",
        Provider = "tiingo",
        Sequence = 42L,
    };

    [Fact]
    public void InsertSql_TargetsRawTicksTable()
    {
        Assert.Contains("INSERT INTO raw_ticks", RawTickSqlCommands.InsertSql);
    }

    [Fact]
    public void InsertSql_UsesReplaySafeConflictClause()
    {
        // DO NOTHING — not DO UPDATE — preserves the first inserted_at_utc
        // on replay. The conflict target matches the UNIQUE constraint
        // declared by the schema bootstrapper.
        Assert.Contains(
            "ON CONFLICT (symbol, provider_timestamp, sequence) DO NOTHING",
            RawTickSqlCommands.InsertSql);
        Assert.DoesNotContain("DO UPDATE", RawTickSqlCommands.InsertSql);
    }

    [Fact]
    public void InsertSql_MentionsEveryInsertColumn()
    {
        foreach (var column in RawTickSqlCommands.InsertColumns)
        {
            Assert.Contains(column, RawTickSqlCommands.InsertSql);
        }
    }

    [Fact]
    public void InsertColumns_DoNotIncludeInsertedAtUtc()
    {
        // inserted_at_utc is populated by the table DEFAULT on first insert
        // and preserved by DO NOTHING on replay — never written by the app.
        Assert.DoesNotContain("inserted_at_utc", RawTickSqlCommands.InsertColumns);
    }

    [Fact]
    public void BindRow_AttachesOneParameterPerInsertColumn()
    {
        using var command = new NpgsqlCommand(RawTickSqlCommands.InsertSql);
        var row = BuildRow();

        RawTickSqlCommands.BindRow(command, row);

        Assert.Equal(
            RawTickSqlCommands.InsertColumns.Count,
            command.Parameters.Count);

        foreach (var column in RawTickSqlCommands.InsertColumns)
        {
            Assert.True(
                command.Parameters.Contains(column),
                $"expected parameter @{column} to be bound");
        }

        Assert.Equal("NVDA", command.Parameters["symbol"].Value);
        Assert.Equal(row.ProviderTimestamp.UtcDateTime, command.Parameters["provider_timestamp"].Value);
        Assert.Equal(row.IngressTimestamp.UtcDateTime, command.Parameters["ingress_timestamp"].Value);
        Assert.Equal(42L, command.Parameters["sequence"].Value);
        Assert.Equal("USD", command.Parameters["currency"].Value);
        Assert.Equal("tiingo", command.Parameters["provider"].Value);
    }

    [Fact]
    public void BindRow_BindsDbNullForMissingBidAsk()
    {
        using var command = new NpgsqlCommand(RawTickSqlCommands.InsertSql);
        var row = BuildRow(bid: null, ask: null);

        RawTickSqlCommands.BindRow(command, row);

        Assert.Equal(DBNull.Value, command.Parameters["bid"].Value);
        Assert.Equal(DBNull.Value, command.Parameters["ask"].Value);
    }

    [Fact]
    public void BindRow_ReplacesPreviouslyBoundParameters()
    {
        using var command = new NpgsqlCommand(RawTickSqlCommands.InsertSql);
        var first = BuildRow();
        var second = first with { Symbol = "MSFT", Sequence = 99L };

        RawTickSqlCommands.BindRow(command, first);
        RawTickSqlCommands.BindRow(command, second);

        Assert.Equal(
            RawTickSqlCommands.InsertColumns.Count,
            command.Parameters.Count);
        Assert.Equal("MSFT", command.Parameters["symbol"].Value);
        Assert.Equal(99L, command.Parameters["sequence"].Value);
    }
}
