using Hqqq.QuoteEngine.Persistence;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ICalibrationStore"/> for tests. The production
/// store is Redis-backed; this fake keeps the same observable behaviour
/// (last-write-wins per basket) without needing a Redis container in CI.
/// </summary>
/// <remarks>
/// Thread-safe via a lock on the backing dictionary so tests that
/// exercise the coordinator from background tasks see consistent
/// reads / writes.
/// </remarks>
public sealed class InMemoryCalibrationStore : ICalibrationStore
{
    private readonly Dictionary<string, CalibrationRecord> _records = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int ReadCount { get; private set; }
    public int WriteCount { get; private set; }

    /// <summary>Returns the currently stored record for the basket (without bumping ReadCount).</summary>
    public CalibrationRecord? Peek(string basketId)
    {
        lock (_gate)
        {
            return _records.TryGetValue(basketId, out var r) ? r : null;
        }
    }

    public CalibrationRecord? TryGet(string basketId)
    {
        lock (_gate)
        {
            ReadCount++;
            return _records.TryGetValue(basketId, out var r) ? r : null;
        }
    }

    public void Set(CalibrationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _records[record.BasketId] = record;
            WriteCount++;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
            ReadCount = 0;
            WriteCount = 0;
        }
    }
}
