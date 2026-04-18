using Hqqq.QuoteEngine.Abstractions;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// Scriptable clock for deterministic engine tests.
/// </summary>
public sealed class FakeSystemClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; }

    public FakeSystemClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    public FakeSystemClock Advance(TimeSpan delta)
    {
        UtcNow += delta;
        return this;
    }

    public FakeSystemClock SetTo(DateTimeOffset at)
    {
        UtcNow = at;
        return this;
    }
}
