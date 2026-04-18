using Hqqq.Gateway.Services.Sources;
using Hqqq.Gateway.Services.Timescale;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="ITimescaleHistoryQueryService"/> used by gateway
/// tests to exercise <c>TimescaleHistorySource</c> without a real
/// TimescaleDB. Captures the call parameters so range-mapping tests can
/// assert the window the source issued.
/// </summary>
public sealed class FakeTimescaleHistoryQueryService : ITimescaleHistoryQueryService
{
    public sealed record Call(string BasketId, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

    private IReadOnlyList<HistoryRow> _rows = Array.Empty<HistoryRow>();
    private Exception? _throw;

    public List<Call> Calls { get; } = new();

    public FakeTimescaleHistoryQueryService SetRows(IReadOnlyList<HistoryRow> rows)
    {
        _rows = rows;
        _throw = null;
        return this;
    }

    public FakeTimescaleHistoryQueryService ThrowOnLoad(Exception ex)
    {
        _throw = ex;
        return this;
    }

    public Task<IReadOnlyList<HistoryRow>> LoadAsync(
        string basketId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct)
    {
        Calls.Add(new Call(basketId, fromUtc, toUtc));
        if (_throw is not null) throw _throw;
        return Task.FromResult(_rows);
    }
}
