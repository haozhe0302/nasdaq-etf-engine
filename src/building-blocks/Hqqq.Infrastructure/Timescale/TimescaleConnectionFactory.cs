using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hqqq.Infrastructure.Timescale;

/// <summary>
/// Lightweight factory for <see cref="NpgsqlDataSource"/> instances backed by
/// <see cref="TimescaleOptions"/>.
/// </summary>
public static class TimescaleConnectionFactory
{
    public static NpgsqlDataSource Create(TimescaleOptions options, ILogger? logger = null)
    {
        logger?.LogInformation("Creating Timescale data source for {ConnectionString}",
            MaskConnectionString(options.ConnectionString));

        var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);
        return builder.Build();
    }

    private static string MaskConnectionString(string cs)
    {
        var builder = new NpgsqlConnectionStringBuilder(cs);
        if (!string.IsNullOrEmpty(builder.Password))
            builder.Password = "****";
        return builder.ConnectionString;
    }
}
