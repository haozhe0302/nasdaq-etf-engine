using Hqqq.Persistence.Schema;

namespace Hqqq.Persistence.Tests.Schema;

public class RawTickSchemaSqlTests
{
    [Fact]
    public void CreateTable_DeclaresIdempotentTable()
    {
        Assert.Contains("CREATE TABLE IF NOT EXISTS raw_ticks", RawTickSchemaSql.CreateTable);
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("provider_timestamp")]
    [InlineData("ingress_timestamp")]
    [InlineData("last")]
    [InlineData("bid")]
    [InlineData("ask")]
    [InlineData("currency")]
    [InlineData("provider")]
    [InlineData("sequence")]
    [InlineData("inserted_at_utc")]
    public void CreateTable_IncludesColumn(string column)
    {
        Assert.Contains(column, RawTickSchemaSql.CreateTable);
    }

    [Fact]
    public void CreateTable_HasInsertedAtUtcDefault()
    {
        Assert.Contains("inserted_at_utc", RawTickSchemaSql.CreateTable);
        Assert.Contains("DEFAULT now()", RawTickSchemaSql.CreateTable);
    }

    [Fact]
    public void CreateTable_DeclaresReplaySafeUniqueConstraint()
    {
        // Chosen identity: (symbol, provider_timestamp, sequence).
        // Provider is intentionally NOT in the key — documented in the
        // CreateTable doc comment and README.
        Assert.Contains(
            "UNIQUE (symbol, provider_timestamp, sequence)",
            RawTickSchemaSql.CreateTable);
    }

    [Fact]
    public void CreateHypertable_PartitionsOnProviderTimestamp()
    {
        Assert.Contains("create_hypertable", RawTickSchemaSql.CreateHypertable);
        Assert.Contains("'raw_ticks'", RawTickSchemaSql.CreateHypertable);
        Assert.Contains("'provider_timestamp'", RawTickSchemaSql.CreateHypertable);
        Assert.Contains("if_not_exists => TRUE", RawTickSchemaSql.CreateHypertable);
    }

    [Fact]
    public void CreateSymbolTimeIndex_IsIdempotentAndUsesSymbolTsDesc()
    {
        Assert.Contains(
            "CREATE INDEX IF NOT EXISTS ix_raw_ticks_symbol_ts_desc",
            RawTickSchemaSql.CreateSymbolTimeIndex);
        Assert.Contains(
            "raw_ticks (symbol, provider_timestamp DESC)",
            RawTickSchemaSql.CreateSymbolTimeIndex);
    }

    [Fact]
    public void CreateTimeIndex_IsIdempotentAndUsesTsDesc()
    {
        Assert.Contains(
            "CREATE INDEX IF NOT EXISTS ix_raw_ticks_ts_desc",
            RawTickSchemaSql.CreateTimeIndex);
        Assert.Contains(
            "raw_ticks (provider_timestamp DESC)",
            RawTickSchemaSql.CreateTimeIndex);
    }

    [Fact]
    public void Statements_RunTableFirstThenHypertableThenIndexes()
    {
        Assert.Equal(4, RawTickSchemaSql.Statements.Count);
        Assert.Same(RawTickSchemaSql.CreateTable, RawTickSchemaSql.Statements[0]);
        Assert.Same(RawTickSchemaSql.CreateHypertable, RawTickSchemaSql.Statements[1]);
        Assert.Same(RawTickSchemaSql.CreateSymbolTimeIndex, RawTickSchemaSql.Statements[2]);
        Assert.Same(RawTickSchemaSql.CreateTimeIndex, RawTickSchemaSql.Statements[3]);
    }
}
