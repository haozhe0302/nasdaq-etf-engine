using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Tests.TestDoubles;

/// <summary>
/// Minimal <see cref="IHoldingsSource"/> that returns a queued sequence
/// of predetermined <see cref="HoldingsFetchResult"/>s — used by the
/// pipeline tests to drive scripted scenarios (first refresh ok, second
/// refresh unchanged, etc.).
/// </summary>
internal sealed class StubHoldingsSource : IHoldingsSource
{
    private readonly Queue<HoldingsFetchResult> _results = new();

    public string Name { get; set; } = "stub";

    public int FetchCount { get; private set; }

    public void Enqueue(HoldingsFetchResult result) => _results.Enqueue(result);

    public Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
    {
        FetchCount++;
        if (_results.Count == 0)
        {
            return Task.FromResult(HoldingsFetchResult.Unavailable("StubHoldingsSource: no result queued"));
        }
        return Task.FromResult(_results.Dequeue());
    }
}
