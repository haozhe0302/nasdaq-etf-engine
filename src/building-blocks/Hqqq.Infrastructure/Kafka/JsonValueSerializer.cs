using System.Text.Json;
using Confluent.Kafka;
using Hqqq.Infrastructure.Serialization;

namespace Hqqq.Infrastructure.Kafka;

/// <summary>
/// Reusable Confluent.Kafka <see cref="ISerializer{T}"/> that encodes the
/// message value as JSON using the shared <see cref="HqqqJsonDefaults.Options"/>
/// so every service emits identical serialization semantics and matches the
/// wire shape produced by <see cref="JsonValueDeserializer{T}"/>.
/// </summary>
/// <remarks>
/// Null values serialize to an empty payload so downstream consumers see
/// tombstones consistently with the deserializer.
/// </remarks>
public sealed class JsonValueSerializer<T> : ISerializer<T> where T : class
{
    private readonly JsonSerializerOptions _options;

    public JsonValueSerializer() : this(HqqqJsonDefaults.Options) { }

    public JsonValueSerializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    public byte[] Serialize(T data, SerializationContext context)
    {
        if (data is null) return [];
        return JsonSerializer.SerializeToUtf8Bytes(data, _options);
    }
}
