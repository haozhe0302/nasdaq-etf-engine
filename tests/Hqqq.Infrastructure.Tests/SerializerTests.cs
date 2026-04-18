using System.Text.Json;
using Hqqq.Infrastructure.Serialization;

namespace Hqqq.Infrastructure.Tests;

public class SerializerTests
{
    [Fact]
    public void HqqqJsonDefaults_UsesCamelCase()
    {
        var obj = new { FooBar = 42 };
        var json = JsonSerializer.Serialize(obj, HqqqJsonDefaults.Options);

        Assert.Contains("\"fooBar\"", json);
        Assert.DoesNotContain("\"FooBar\"", json);
    }

    [Fact]
    public void HqqqJsonDefaults_OmitsNullValues()
    {
        var obj = new { Value = (string?)null, Present = "yes" };
        var json = JsonSerializer.Serialize(obj, HqqqJsonDefaults.Options);

        Assert.DoesNotContain("\"value\"", json);
        Assert.Contains("\"present\"", json);
    }

    [Fact]
    public void HqqqJsonDefaults_SerializesEnumsAsStrings()
    {
        var obj = new { Status = DayOfWeek.Monday };
        var json = JsonSerializer.Serialize(obj, HqqqJsonDefaults.Options);

        Assert.Contains("\"Monday\"", json);
    }

    [Fact]
    public void HqqqJsonDefaults_RoundTrips()
    {
        var original = new TestDto("AAPL", 150.25m);
        var json = JsonSerializer.Serialize(original, HqqqJsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<TestDto>(json, HqqqJsonDefaults.Options);

        Assert.NotNull(restored);
        Assert.Equal(original.Symbol, restored.Symbol);
        Assert.Equal(original.Price, restored.Price);
    }

    private record TestDto(string Symbol, decimal Price);
}
