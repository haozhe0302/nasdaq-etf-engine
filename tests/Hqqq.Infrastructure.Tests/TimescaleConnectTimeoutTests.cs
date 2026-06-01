using Hqqq.Infrastructure.Timescale;
using Npgsql;

namespace Hqqq.Infrastructure.Tests;

/// <summary>
/// Locks the fail-fast contract for TimescaleDB connections. Npgsql's default
/// connect timeout is 15s; if a stopped/unreachable DB is allowed to block for
/// that long, the aggregated /api/system/health probe and /api/history both
/// hang ~15s while the realtime core is perfectly healthy. The factory caps
/// the connect timeout so the "DB down only degrades history" posture stays
/// responsive.
/// </summary>
public class TimescaleConnectTimeoutTests
{
    [Fact]
    public void WithBoundedConnectTimeout_CapsTheNpgsqlDefault()
    {
        const string raw = "Host=myhost;Database=mydb;Username=u;Password=p";

        var bounded = TimescaleConnectionFactory.WithBoundedConnectTimeout(raw);

        var builder = new NpgsqlConnectionStringBuilder(bounded);
        Assert.Equal(TimescaleConnectionFactory.DefaultConnectTimeoutSeconds, builder.Timeout);
    }

    [Fact]
    public void WithBoundedConnectTimeout_LowersAnExplicitlyLargerTimeout()
    {
        const string raw = "Host=myhost;Database=mydb;Timeout=30";

        var bounded = TimescaleConnectionFactory.WithBoundedConnectTimeout(raw);

        var builder = new NpgsqlConnectionStringBuilder(bounded);
        Assert.Equal(TimescaleConnectionFactory.DefaultConnectTimeoutSeconds, builder.Timeout);
    }

    [Fact]
    public void WithBoundedConnectTimeout_PreservesAnExplicitlySmallerTimeout()
    {
        const string raw = "Host=myhost;Database=mydb;Timeout=2";

        var bounded = TimescaleConnectionFactory.WithBoundedConnectTimeout(raw);

        var builder = new NpgsqlConnectionStringBuilder(bounded);
        Assert.Equal(2, builder.Timeout);
    }
}
