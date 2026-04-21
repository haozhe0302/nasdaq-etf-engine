using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Tests.TestDoubles;

/// <summary>
/// Tiny fluent helper used across Source / Pipeline / Mapper tests to
/// build valid <see cref="HoldingsSnapshot"/> fixtures without repeating
/// the decimals/dates in every test method.
/// </summary>
internal static class SnapshotBuilder
{
    public static HoldingsSnapshot Build(
        string basketId = "HQQQ",
        string version = "v-test",
        string source = "test",
        int count = 60,
        DateOnly? asOfDate = null)
    {
        var constituents = new List<HoldingsConstituent>();
        for (var i = 0; i < count; i++)
        {
            var symbol = $"SYM{i:000}";
            constituents.Add(new HoldingsConstituent
            {
                Symbol = symbol,
                Name = $"{symbol} Corp.",
                Sector = "Technology",
                SharesHeld = 100m + i,
                ReferencePrice = 10m + i,
                TargetWeight = 1m / count,
            });
        }

        return new HoldingsSnapshot
        {
            BasketId = basketId,
            Version = version,
            AsOfDate = asOfDate ?? new DateOnly(2026, 4, 15),
            ScaleFactor = 1.0m,
            NavPreviousClose = 540m,
            QqqPreviousClose = 480m,
            Constituents = constituents,
            Source = source,
        };
    }
}
