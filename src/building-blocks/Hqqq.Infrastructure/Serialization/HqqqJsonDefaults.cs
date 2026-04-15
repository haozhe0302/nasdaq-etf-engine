using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hqqq.Infrastructure.Serialization;

/// <summary>
/// Canonical JSON serializer options shared across all services
/// for consistent Kafka event serialization and REST responses.
/// </summary>
public static class HqqqJsonDefaults
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
