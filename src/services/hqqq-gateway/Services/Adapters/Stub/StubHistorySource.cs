using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Stub;

// TODO: Phase 2B5 — replace with TimescaleHistorySource reading from TimescaleDB
public sealed class StubHistorySource : IHistorySource
{
    private static readonly DateTimeOffset StubTimestamp =
        new(2025, 1, 2, 14, 30, 0, TimeSpan.Zero);

    public Task<IResult> GetHistoryAsync(string? range, CancellationToken ct)
    {
        var r = range?.ToUpperInvariant() ?? "1D";

        var payload = new
        {
            range = r,
            startDate = "2025-01-02",
            endDate = "2025-01-02",
            pointCount = 3,
            totalPoints = 3,
            isPartial = false,
            series = new[]
            {
                new { time = StubTimestamp.AddMinutes(-10), nav = 99.80m, marketPrice = 99.75m },
                new { time = StubTimestamp.AddMinutes(-5), nav = 99.90m, marketPrice = 99.85m },
                new { time = StubTimestamp, nav = 100.00m, marketPrice = 99.95m },
            },
            trackingError = new
            {
                rmseBps = 5.0,
                maxAbsBasisBps = 8.0,
                avgAbsBasisBps = 4.0,
                maxDeviationPct = 0.08,
                correlation = 0.9999,
            },
            distribution = Enumerable.Range(-10, 21).Select(i => new
            {
                label = i.ToString(),
                count = i == 0 ? 3 : 0,
            }).ToArray(),
            diagnostics = new
            {
                snapshots = 3,
                gaps = 0,
                completenessPct = 100.0,
                daysLoaded = 1,
            },
        };

        return Task.FromResult(Results.Ok(payload));
    }
}
