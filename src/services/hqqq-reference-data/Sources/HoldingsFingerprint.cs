using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Deterministic SHA-256 fingerprint over the canonical projection of a
/// <see cref="HoldingsSnapshot"/>. Two processes loading equivalent
/// snapshot content produce the same fingerprint, which is what the
/// quote-engine's idempotency guard relies on so a re-publish of the same
/// basket does not reset state.
///
/// Only content fields contribute to the fingerprint:
/// <c>basketId, version, asOfDate, scaleFactor, navPreviousClose,
/// qqqPreviousClose, [symbol, name, sector, sharesHeld, referencePrice,
/// targetWeight]*</c> (constituents sorted by symbol ordinal). Lineage
/// metadata such as <see cref="HoldingsSnapshot.Source"/> is intentionally
/// excluded — flipping the same basket between live and seed must not
/// churn the fingerprint.
/// </summary>
public static class HoldingsFingerprint
{
    public static string Compute(HoldingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var canonical = new
        {
            basketId = snapshot.BasketId,
            version = snapshot.Version,
            asOfDate = snapshot.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            scaleFactor = snapshot.ScaleFactor,
            navPreviousClose = snapshot.NavPreviousClose,
            qqqPreviousClose = snapshot.QqqPreviousClose,
            constituents = snapshot.Constituents
                .OrderBy(c => c.Symbol, StringComparer.Ordinal)
                .Select(c => new
                {
                    symbol = c.Symbol,
                    name = c.Name,
                    sector = c.Sector,
                    sharesHeld = c.SharesHeld,
                    referencePrice = c.ReferencePrice,
                    targetWeight = c.TargetWeight,
                })
                .ToArray(),
        };

        var json = JsonSerializer.Serialize(canonical, new JsonSerializerOptions
        {
            WriteIndented = false,
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return hex.ToString();
    }
}
