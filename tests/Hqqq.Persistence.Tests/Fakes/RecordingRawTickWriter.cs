using System.Collections.Concurrent;
using Hqqq.Persistence.Persistence;

namespace Hqqq.Persistence.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRawTickWriter"/> that records each flushed batch
/// (as a snapshot) so worker tests can assert exact batching and
/// flush-interval behavior. Optionally raises an exception on the first
/// <paramref name="failFirst"/> calls to simulate transient DB failures.
/// </summary>
public sealed class RecordingRawTickWriter : IRawTickWriter
{
    private readonly ConcurrentQueue<IReadOnlyList<RawTickRow>> _batches = new();
    private int _failuresRemaining;

    public RecordingRawTickWriter(int failFirst = 0)
    {
        _failuresRemaining = failFirst;
    }

    public IReadOnlyCollection<IReadOnlyList<RawTickRow>> Batches => _batches;

    public int CallCount { get; private set; }

    public Task WriteBatchAsync(IReadOnlyList<RawTickRow> rows, CancellationToken ct)
    {
        CallCount++;

        if (_failuresRemaining > 0)
        {
            _failuresRemaining--;
            throw new InvalidOperationException("simulated raw-tick writer failure");
        }

        _batches.Enqueue(rows.ToArray());
        return Task.CompletedTask;
    }
}
