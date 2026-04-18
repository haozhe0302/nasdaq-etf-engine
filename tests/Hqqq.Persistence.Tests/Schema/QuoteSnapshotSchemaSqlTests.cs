using Hqqq.Persistence.Schema;

namespace Hqqq.Persistence.Tests.Schema;

public class QuoteSnapshotSchemaSqlTests
{
    [Fact]
    public void CreateTable_DeclaresIdempotentTable()
    {
        Assert.Contains("CREATE TABLE IF NOT EXISTS quote_snapshots", QuoteSnapshotSchemaSql.CreateTable);
    }

    [Theory]
    [InlineData("basket_id")]
    [InlineData("ts")]
    [InlineData("nav")]
    [InlineData("market_proxy_price")]
    [InlineData("premium_discount_pct")]
    [InlineData("stale_count")]
    [InlineData("fresh_count")]
    [InlineData("max_component_age_ms")]
    [InlineData("quote_quality")]
    [InlineData("inserted_at_utc")]
    public void CreateTable_IncludesColumn(string column)
    {
        Assert.Contains(column, QuoteSnapshotSchemaSql.CreateTable);
    }

    [Fact]
    public void CreateTable_HasInsertedAtUtcDefault()
    {
        Assert.Contains("inserted_at_utc", QuoteSnapshotSchemaSql.CreateTable);
        Assert.Contains("DEFAULT now()", QuoteSnapshotSchemaSql.CreateTable);
    }

    [Fact]
    public void CreateTable_DeclaresUniqueConstraintForIdempotency()
    {
        Assert.Contains("UNIQUE (basket_id, ts)", QuoteSnapshotSchemaSql.CreateTable);
    }

    [Fact]
    public void CreateHypertable_UsesTsAndIsIdempotent()
    {
        Assert.Contains("create_hypertable", QuoteSnapshotSchemaSql.CreateHypertable);
        Assert.Contains("'quote_snapshots'", QuoteSnapshotSchemaSql.CreateHypertable);
        Assert.Contains("'ts'", QuoteSnapshotSchemaSql.CreateHypertable);
        Assert.Contains("if_not_exists => TRUE", QuoteSnapshotSchemaSql.CreateHypertable);
    }

    [Fact]
    public void CreateReadIndex_IsIdempotentAndUsesBasketTsDesc()
    {
        Assert.Contains("CREATE INDEX IF NOT EXISTS ix_quote_snapshots_basket_ts_desc",
            QuoteSnapshotSchemaSql.CreateReadIndex);
        Assert.Contains("quote_snapshots (basket_id, ts DESC)",
            QuoteSnapshotSchemaSql.CreateReadIndex);
    }

    [Fact]
    public void Statements_RunTableFirstThenHypertableThenIndex()
    {
        Assert.Equal(3, QuoteSnapshotSchemaSql.Statements.Count);
        Assert.Same(QuoteSnapshotSchemaSql.CreateTable, QuoteSnapshotSchemaSql.Statements[0]);
        Assert.Same(QuoteSnapshotSchemaSql.CreateHypertable, QuoteSnapshotSchemaSql.Statements[1]);
        Assert.Same(QuoteSnapshotSchemaSql.CreateReadIndex, QuoteSnapshotSchemaSql.Statements[2]);
    }
}
