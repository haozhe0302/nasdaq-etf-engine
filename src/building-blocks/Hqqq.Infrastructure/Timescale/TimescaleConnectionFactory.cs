using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hqqq.Infrastructure.Timescale;

/// <summary>
/// Lightweight factory for <see cref="NpgsqlDataSource"/> instances backed by
/// <see cref="TimescaleOptions"/>.
/// </summary>
public static class TimescaleConnectionFactory
{
    /// <summary>
    /// Default cap (seconds) applied to the Npgsql connect timeout. Npgsql's
    /// own default is 15s, which means a stopped/unreachable TimescaleDB
    /// blocks every probe and query for the full 15s — that is what turns a
    /// DB outage into a 15s "no response" on <c>/api/system/health</c> and
    /// <c>/api/history</c> even though the realtime core is fine. Capping it
    /// keeps the "DB down only degrades history" posture fast.
    /// </summary>
    public const int DefaultConnectTimeoutSeconds = 2;

    public static NpgsqlDataSource Create(TimescaleOptions options, ILogger? logger = null)
    {
        var connectionString = WithBoundedConnectTimeout(options.ConnectionString);

        logger?.LogInformation("Creating Timescale data source for {ConnectionString}",
            MaskConnectionString(connectionString));

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        return builder.Build();
    }

    /// <summary>
    /// Returns <paramref name="connectionString"/> with its connect timeout
    /// capped at <paramref name="maxConnectTimeoutSeconds"/>. Only ever
    /// lowers the timeout (an explicit, smaller value is preserved); it never
    /// raises it. Applied to both the shared data source and the health-check
    /// connection so a stopped DB fails fast instead of hanging callers.
    /// </summary>
    public static string WithBoundedConnectTimeout(
        string connectionString,
        int maxConnectTimeoutSeconds = DefaultConnectTimeoutSeconds)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Timeout <= 0 || builder.Timeout > maxConnectTimeoutSeconds)
        {
            builder.Timeout = maxConnectTimeoutSeconds;
        }
        return builder.ConnectionString;
    }

    private static string MaskConnectionString(string cs)
    {
        var builder = new NpgsqlConnectionStringBuilder(cs);
        if (!string.IsNullOrEmpty(builder.Password))
            builder.Password = "****";
        return builder.ConnectionString;
    }
}
