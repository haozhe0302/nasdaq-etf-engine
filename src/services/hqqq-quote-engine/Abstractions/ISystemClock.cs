namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Tiny clock abstraction so tests can drive the engine with a
/// fixed or scriptable <c>UtcNow</c>.
/// </summary>
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Production clock — just <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
