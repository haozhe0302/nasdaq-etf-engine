using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hqqq.Infrastructure.Redis;

/// <summary>
/// Lightweight factory for shared <see cref="IConnectionMultiplexer"/> instances.
/// Services should register the multiplexer as a singleton via
/// <see cref="Hosting.ServiceRegistrationExtensions"/>.
/// </summary>
public static class RedisConnectionFactory
{
    public static async Task<IConnectionMultiplexer> ConnectAsync(
        RedisOptions options, ILogger? logger = null)
    {
        logger?.LogInformation("Connecting to Redis at {Configuration}", options.Configuration);
        var mux = await ConnectionMultiplexer.ConnectAsync(options.Configuration);
        logger?.LogInformation("Redis connected (endpoints: {Endpoints})",
            string.Join(", ", mux.GetEndPoints().Select(e => e.ToString())));
        return mux;
    }
}
