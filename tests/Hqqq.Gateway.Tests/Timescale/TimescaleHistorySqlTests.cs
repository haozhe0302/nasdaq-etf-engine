using Hqqq.Gateway.Services.Timescale;

namespace Hqqq.Gateway.Tests.Timescale;

/// <summary>
/// Locks the defensive regular-session (RTH) semantics of the
/// <c>/api/history</c> Timescale query. The actual RTH row filtering runs
/// in Postgres (<c>AT TIME ZONE</c>), so it cannot be exercised through the
/// in-memory fake; instead we assert the SQL contract directly.
/// </summary>
public class TimescaleHistorySqlTests
{
    [Fact]
    public void SelectHistorySql_UsesEndExclusiveUpperBound()
    {
        var sql = TimescaleHistoryQueryService.SelectHistorySql;

        // End-exclusive `ts < @to_utc` avoids double-counting a boundary row.
        Assert.Contains("ts <  @to_utc", sql);
        Assert.DoesNotContain("ts <= @to_utc", sql);
    }

    [Fact]
    public void SelectHistorySql_AppliesRegularSessionFilter()
    {
        var sql = TimescaleHistoryQueryService.SelectHistorySql;

        Assert.Contains("America/New_York", sql);
        Assert.Contains("TIME '09:30'", sql);
        Assert.Contains("TIME '16:00'", sql);
        // Weekday-only (Mon=1 .. Fri=5).
        Assert.Contains("ISODOW", sql);
        Assert.Contains("BETWEEN 1 AND 5", sql);
    }
}
