namespace Hqqq.Infrastructure.Redis;

/// <summary>
/// Redis connection settings, bound to the "Redis" configuration section.
/// </summary>
public sealed class RedisOptions
{
    public string Configuration { get; set; } = "localhost:6379";
}
