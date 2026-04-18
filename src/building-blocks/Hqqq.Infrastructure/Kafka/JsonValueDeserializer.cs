using System.Text.Json;
using Confluent.Kafka;
using Hqqq.Infrastructure.Serialization;

namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Reusable Confluent.Kafka <see cref="IDeserializer{T}"/> that decodes the
/// message value as JSON using the shared <see cref="HqqqJsonDefaults.Options"/>
/// so every service sees identical serialization semantics.
/// </summary>
/// <remarks>
/// Tombstone messages (null payload, <c>IsNull == true</c>) deserialize to
/// <c>default(T)</c>; compacted-topic consumers must treat <c>null</c> as
/// "key deleted" rather than a malformed event.
/// </remarks>
public sealed class JsonValueDeserializer<T> : IDeserializer<T?>
{
    private readonly JsonSerializerOptions _options;

    public JsonValueDeserializer() : this(HqqqJsonDefaults.Options) { }

    public JsonValueDeserializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    public T? Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        if (isNull) return default;
        if (data.IsEmpty) return default;

        return JsonSerializer.Deserialize<T>(data, _options);
    }
}
