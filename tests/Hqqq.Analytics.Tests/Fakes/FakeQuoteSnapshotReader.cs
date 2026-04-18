using Hqqq.Analytics.Timescale;

namespace Hqqq.Analytics.Tests.Fakes;

internal sealed class FakeQuoteSnapshotReader : IQuoteSnapshotReader
{
    private readonly IReadOnlyList<QuoteSnapshotRecord> _rows;
    private readonly Exception? _throw;

    public FakeQuoteSnapshotReader(IReadOnlyList<QuoteSnapshotRecord> rows)
    {
        _rows = rows;
    }

    public FakeQuoteSnapshotReader(Exception toThrow)
    {
        _rows = Array.Empty<QuoteSnapshotRecord>();
        _throw = toThrow;
    }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<QuoteSnapshotRecord>> LoadAsync(
        string basketId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int maxRows,
        CancellationToken ct)
    {
        CallCount++;
        if (_throw is not null) throw _throw;
        return Task.FromResult(_rows);
    }
}

internal sealed class FakeRawTickAggregateReader : IRawTickAggregateReader
{
    private readonly long _count;

    public FakeRawTickAggregateReader(long count = 0) => _count = count;

    public int CallCount { get; private set; }

    public Task<long> CountAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(_count);
    }
}
